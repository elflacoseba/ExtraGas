using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integración del filtro <c>Activo</c> en
/// <see cref="RecepcionService.LoadProductosByIdAsync"/> y la validación de
/// productos activos en <see cref="PedidoService.RegistrarCanjePedidoAsync"/>
/// contra un MySQL real dentro de un container Docker (Testcontainers.MySql).
///
/// Issue #145 Slice 4: el bug crítico era que ambos servicios aceptaban
/// productos desactivados (Activo=false, DeletedAt=null) o soft-deleted
/// (DeletedAt!=null). Estos tests reproducen el escenario de race real
/// — desactivar el producto entre la carga del formulario y el submit —
/// para validar que la query SQL y la validación cubren ambos casos contra
/// un MySQL real con sus triggers / FKs.
///
/// Comparte <see cref="PedidoCanjeMySqlFixture"/> (extendido con
/// <c>proveedores</c> + <c>usuarios</c> en el schema mínimo). Sin este
/// segundo container Docker (los containers son caros de arrancar).
/// </summary>
public class ProductoActivoRaceIntegrationTests : IClassFixture<PedidoCanjeMySqlFixture>
{
    private readonly PedidoCanjeMySqlFixture _fixture;

    public ProductoActivoRaceIntegrationTests(PedidoCanjeMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    // ====================================================================
    // RecepcionService: producto desactivado no debe poder crear una recepción
    // ====================================================================

    [Fact]
    public async Task RecepcionCreateAsync_ProductoInactivo_ThrowsInvalidOperationExceptionConId()
    {
        // El test del bug: seed un producto Activo=false, Intent
        // CreateAsync con un item apuntando a ese producto. El service debe
        // tirar InvalidOperationException con mensaje que mencione el id
        // (formato: "no existe o está inactivo" del comentario línea 148 de
        // RecepcionService.cs). Y no debe persistir ninguna fila.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RecepcionCreateAsync_ProductoInactivo_ThrowsInvalidOperationExceptionConId));
        try
        {
            var now = DateTime.UtcNow;
            var fecha = DateOnly.FromDateTime(now);

            var tipoProductoId = (await ctx.TiposProducto.FirstAsync()).Id;
            var empleado = await SeedEmpleadoAsync(ctx, now, fecha);
            var proveedor = await SeedProveedorAsync(ctx, now);

            // Producto desactivado (Activo=false, DeletedAt=null).
            var productoInactivo = new Producto
            {
                Codigo = "GAS-INACT",
                Nombre = "Producto Inactivo",
                TipoProductoId = tipoProductoId,
                UnidadVenta = "UNIDAD",
                PrecioActual = 1500m,
                ManejaGarrafaIndividual = false, // no tracking de garrafas
                Activo = false, // <-- la condición bajo prueba
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.Productos.Add(productoInactivo);
            await ctx.SaveChangesAsync();

            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            var productoService = new NotImplementedIProductoServiceForRaceTests();
            var service = new RecepcionService(ctx, productoService);

            var dto = new CrearRecepcionDto
            {
                Fecha = now,
                ProveedorId = proveedor.Id,
                Subtotal = 1500m,
                Descuento = 0m,
                Items = new List<CrearRecepcionItemDto>
                {
                    new()
                    {
                        ProductoId = productoInactivo.Id,
                        Cantidad = 1m,
                        PrecioUnitario = 1500m,
                    },
                },
            };

            var act = async () => await service.CreateAsync(dto, usuarioId: 1, ct: default);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"*{productoInactivo.Id}*");

            // Ninguna fila persistida.
            (await ctx.RecepcionesProveedor.CountAsync()).Should().Be(0,
                "el rechazo debe ocurrir ANTES del BEGIN TRANSACTION");
        }
        finally
        {
            await _fixture.DropDatabaseAsyncForDbContext(ctx);
        }
    }

    // ====================================================================
    // PedidoService: race real — producto desactivado entre draft y confirm
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_ProductoDesactivadoEntreDraftYConfirm_RechazaConfirmacion()
    {
        // El test del race real:
        // 1) Sembramos un pedido PENDIENTE con un item referenciando un
        //    producto activo (simulamos el "draft abierto").
        // 2) Por raw SQL desactivamos el producto (simulamos la acción de
        //    un admin entre que el operador abrió el form y apretó CONFIRMAR).
        // 3) Llamamos RegistrarCanjePedidoAsync (path VENTA-only,
        //    ConfirmarSinCanjeAsync). El service debe tirar
        //    InvalidOperationException nombrando el producto y el pedido
        //    debe quedar en PENDIENTE — sin transacciones parcialmente
        //    commiteadas.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_ProductoDesactivadoEntreDraftYConfirm_RechazaConfirmacion));
        try
        {
            var seed = await SeedPedidoActivoAsync(ctx);

            // Step 2: race real — desactivar el producto DESPUÉS de crear el draft.
            // Usamos SaveChanges convencional en lugar de ExecuteUpdateAsync para
            // esquivar quirks del change tracker + Pomelo. IgnoreQueryFilters()
            // es necesario porque el QueryFilter global ocultaría el producto si
            // ya estuviera soft-deleted (no es el caso acá, pero deja el patrón
            // claro para futuros tests con soft-delete).
            var producto = await ctx.Productos.IgnoreQueryFilters()
                .FirstAsync(p => p.Id == seed.ProductoId);
            producto.Activo = false;
            await ctx.SaveChangesAsync();

            // Re-leer con la entidad fresca para confirmar el estado desactivado.
            await ctx.Entry(producto).ReloadAsync();
            producto.Activo.Should().BeFalse("simulamos la desactivación por el admin");

            var service = NewPedidoService(ctx);

            // Step 3: el operador confirma. codigosPorItem vacío fuerza el
            // path VENTA-only (ConfirmarSinCanjeAsync) — la validación corre
            // ANTES del fork así que este path ejercita la guarda igual.
            var codigosPorItem = new Dictionary<ulong, List<string>>();

            var act = async () => await service.RegistrarCanjePedidoAsync(
                seed.PedidoId, codigosPorItem, usuarioId: 1, ct: default);

            var ex = await act.Should().ThrowAsync<InvalidOperationException>();
            ex.WithMessage("*Garrafa 10kg*")
              .WithMessage("*desactivado*");

            // El pedido NO pasó a CONFIRMADO — la validación cortó antes.
            ctx.ChangeTracker.Clear();
            var pedidoFinal = await ctx.Pedidos.IgnoreQueryFilters()
                .FirstAsync(p => p.Id == seed.PedidoId);
            var confirmadoId = (await ctx.EstadosPedido.FirstAsync(e => e.Codigo == "CONFIRMADO")).Id;
            pedidoFinal.EstadoPedidoId.Should().NotBe(confirmadoId,
                "el pedido debe seguir en PENDIENTE — sin escrituras parciales");
        }
        finally
        {
            await _fixture.DropDatabaseAsyncForDbContext(ctx);
        }
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static async Task<Empleado> SeedEmpleadoAsync(ExtraGasDbContext ctx, DateTime now, DateOnly fecha)
    {
        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Operador",
            UsuarioId = 1,
            Activo = true,
            FechaIngreso = fecha,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Empleados.Add(empleado);
        await ctx.SaveChangesAsync();
        return empleado;
    }

    private static async Task<Proveedor> SeedProveedorAsync(ExtraGasDbContext ctx, DateTime now)
    {
        var proveedor = new Proveedor
        {
            RazonSocial = "Distribuidora Test",
            Cuit = "20-12345678-9",
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();
        return proveedor;
    }

    private async Task<RacePedidoSeed> SeedPedidoActivoAsync(ExtraGasDbContext ctx)
    {
        var now = DateTime.UtcNow;
        var fecha = DateOnly.FromDateTime(now);

        var pendienteId = (await ctx.EstadosPedido.FirstAsync(e => e.Codigo == "PENDIENTE")).Id;
        var canalVentaId = (await ctx.CanalesVenta.FirstAsync()).Id;
        var tipoProductoId = (await ctx.TiposProducto.FirstAsync()).Id;

        // SaveChanges separados — patrón de PedidoCanjeIntegrationTests. EF
        // puede asignar IDs en un solo SaveChanges, pero separar evita race
        // conditions sutiles entre el INSERT de padre (empleado/cliente) y el
        // INSERT de hijo (pedido) cuando los FKs están activos.
        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Operador",
            UsuarioId = 1,
            Telefono = "1100000000",
            Activo = true,
            FechaIngreso = fecha,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Empleados.Add(empleado);
        await ctx.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Pedro",
            Apellido = "Garcia",
            Dni = "11222333",
            TelefonoPrincipal = "1144556677",
            FechaAlta = fecha,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = "GAS-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = tipoProductoId,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var pedido = new Pedido
        {
            Fecha = now,
            ClienteId = cliente.Id,
            EmpleadoId = empleado.Id,
            EstadoPedidoId = pendienteId,
            CanalVentaId = canalVentaId,
            Subtotal = 0m,
            Descuento = 0m,
            Total = 0m,
            MontoPagado = 0m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Pedidos.Add(pedido);
        await ctx.SaveChangesAsync();

        var item = new PedidoItem
        {
            PedidoId = pedido.Id,
            ProductoId = producto.Id,
            TipoLinea = TipoLinea.VENTA,
            Cantidad = 1m,
            PrecioUnitario = 1500m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.PedidoItems.Add(item);
        await ctx.SaveChangesAsync();

        return new RacePedidoSeed(pedido.Id, producto.Id, cliente.Id);
    }

    private static PedidoService NewPedidoService(ExtraGasDbContext context)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new NotImplementedGarrafaServiceForRaceTests();
        return new PedidoService(context, mapper, cache, garrafaService);
    }

    private sealed record RacePedidoSeed(ulong PedidoId, ulong ProductoId, ulong ClienteId);

    /// <summary>
    /// Stub de <see cref="IProductoService"/> que lanza si algo lo invoca.
    /// El flujo bajo prueba (CreateAsync → LoadProductosByIdAsync →
    /// ValidarItemsPreCommitAsync → throw) no llega a GetProductosActivosAsync.
    /// </summary>
    private sealed class NotImplementedIProductoServiceForRaceTests : IProductoService
    {
        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<UnidadVentaDto>> GetUnidadesVentaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDeleteImpactDto> GetDeleteImpactAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// Stub de <see cref="IGarrafaService"/> que lanza si algo lo invoca.
    /// El path VENTA-only (ConfirmarSinCanjeAsync) no usa GarrafaService.
    /// </summary>
    private sealed class NotImplementedGarrafaServiceForRaceTests : IGarrafaService
    {
        public Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<GarrafaDto>> GetPagedAsync(string? codigo, byte? capacidad, int page = 1, int pageSize = 20, string sortBy = "codigo", string sortDir = "asc", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RegistrarMovimientoPorCanjeAsync(ulong garrafaId, ulong estadoDestinoId, ulong? clienteId, ulong pedidoId, string tipoMovimientoCodigo, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VGarrafaEnCliente>> GetEnClientesAsync(ulong? clienteId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
