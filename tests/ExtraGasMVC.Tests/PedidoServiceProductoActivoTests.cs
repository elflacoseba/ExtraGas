using AutoMapper;
using ExtraGasMVC.Constants;
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
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión para la validación de productos activos al confirmar
/// pedido (issue #145 — Tarea 4.3/4.4).
///
/// El bug: <c>PedidoService.RegistrarCanjePedidoAsync</c> confirmaba pedidos
/// cuyos <c>PedidoItem.ProductoId</c> podía haber sido desactivado o
/// soft-deleted entre la creación del draft y la confirmación. Esto
/// corrompía inventario y dejaba la BD con FKs apuntando a productos
/// inactivos.
///
/// El fix: nuevo <c>ValidarProductosActivosAsync</c> ejecutado después de
/// <c>AsegurarNoCanjeadoAsync</c> y antes de <c>LoadCatalogosParaCanjeAsync</c>,
/// cubriendo tanto el path con canje como el path VENTA-only
/// (<c>ConfirmarSinCanjeAsync</c>).
///
/// Patrón: tests end-to-end contra <c>RegistrarCanjePedidoAsync</c> usando
/// EFC.InMemory. La validación corre ANTES de tocar transacciones reales,
/// así que InMemory es suficiente para cubrir la lógica.
/// </summary>
public class PedidoServiceProductoActivoTests
{
    private static (PedidoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new NotImplementedGarrafaService();
        return (new PedidoService(context, mapper, cache, garrafaService), context);
    }

    /// <summary>
    /// Sembrado mínimo: cliente + empleado + producto (por defecto activo) +
    /// pedido PENDIENTE con UN item que referencia ese producto. Devuelve los
    /// IDs para que el test arme codigosPorItem según el scenario.
    /// </summary>
    // Issue #158 (CA1822): static — no usa estado de instancia, recibe el
    // DbContext como parámetro.
    private static async Task<(ulong pedidoId, ulong productoId, ulong clienteId)> SeedPedidoActivoAsync(
        ExtraGasDbContext context, bool incluirProductoInactivo = false, bool softDeleteProducto = false)
    {
        // InMemory no aplica migrations ni siembra catálogos. Sembramos los
        // mínimos: estados_pedido (PENDIENTE + CONFIRMADO), canales_venta
        // (PRESENCIAL) y tipos_producto (GAS).
        if (!await context.EstadosPedido.AnyAsync(e => e.Codigo == PedidoEstados.Pendiente))
        {
            context.EstadosPedido.AddRange(
                new EstadoPedido { Codigo = PedidoEstados.Pendiente, Nombre = "Pendiente", EsFinal = false },
                new EstadoPedido { Codigo = PedidoEstados.Confirmado, Nombre = "Confirmado", EsFinal = false },
                new EstadoPedido { Codigo = PedidoEstados.EnPreparacion, Nombre = "En preparación", EsFinal = false },
                new EstadoPedido { Codigo = PedidoEstados.Entregado, Nombre = "Entregado", EsFinal = true },
                new EstadoPedido { Codigo = PedidoEstados.Cancelado, Nombre = "Cancelado", EsFinal = true });
        }
        if (!await context.CanalesVenta.AnyAsync())
            context.CanalesVenta.Add(new CanalVenta { Codigo = "PRESENCIAL", Nombre = "Presencial" });
        if (!await context.TiposProducto.AnyAsync())
            context.TiposProducto.Add(new TipoProducto { Codigo = "GAS", Nombre = "Gas" });
        // LoadCatalogosParaCanjeAsync exige estos códigos aunque el path sea
        // VENTA-only (los valida ANTES de decidir si hay canje). Si faltan,
        // el service tira "Faltan tipos de movimiento ENTREGA_CLIENTE / ..."
        // y enmascara el bug bajo prueba.
        if (!await context.TiposMovimientoGarrafa.AnyAsync())
        {
            context.TiposMovimientoGarrafa.Add(new TipoMovimientoGarrafa
            {
                Codigo = "ENTREGA_CLIENTE", Nombre = "Entrega a cliente"
            });
            context.TiposMovimientoGarrafa.Add(new TipoMovimientoGarrafa
            {
                Codigo = "DEVOLUCION_CLIENTE", Nombre = "Devolución de cliente"
            });
        }
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var fecha = DateOnly.FromDateTime(now);

        var pendienteId = (await context.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Pendiente)).Id;
        var canalVentaId = (await context.CanalesVenta.FirstAsync()).Id;
        var tipoProductoId = (await context.TiposProducto.FirstAsync()).Id;

        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Operador",
            Telefono = "1100000000",
            FechaIngreso = fecha,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Empleados.Add(empleado);

        var cliente = new Cliente
        {
            Nombre = "Pedro",
            Apellido = "Garcia",
            TelefonoPrincipal = "1144556677",
            FechaAlta = fecha,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Clientes.Add(cliente);

        var producto = new Producto
        {
            Codigo = "GAS-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = tipoProductoId,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false, // VENTA-only path: no tracking de garrafas
            Activo = !(incluirProductoInactivo || softDeleteProducto), // el default es activo
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = softDeleteProducto ? now : null,
        };
        context.Productos.Add(producto);

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
        context.Pedidos.Add(pedido);
        await context.SaveChangesAsync();

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
        context.PedidoItems.Add(item);
        await context.SaveChangesAsync();

        return (pedido.Id, producto.Id, cliente.Id);
    }

    // ====================================================================
    // Issue #145 Slice 4: ValidarProductosActivosAsync
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_ProductoDesactivado_ThrowsInvalidOperationException()
    {
        // Tarea 4.3 RED: pedido en draft con un producto que fue desactivado
        // (Activo = false). Al confirmar, el service debe tirar con mensaje
        // claro nombrando el producto, sin transicionar el pedido a
        // CONFIRMADO ni escribir movimientos.
        var (service, context) = NewService(nameof(RegistrarCanjePedidoAsync_ProductoDesactivado_ThrowsInvalidOperationException));
        var (pedidoId, productoId, _) = await SeedPedidoActivoAsync(context, incluirProductoInactivo: true);

        // codigosPorItem vacío fuerza el path VENTA-only (ConfirmarSinCanjeAsync).
        var codigosPorItem = new Dictionary<ulong, List<string>>();

        var act = async () => await service.RegistrarCanjePedidoAsync(pedidoId, codigosPorItem, usuarioId: 1);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // El mensaje debe nombrar el producto (por nombre) para que el
        // operador sepa cuál refrescar.
        ex.WithMessage("*Garrafa 10kg*")
          .WithMessage($"*desactivado*");

        // El pedido NO pasó a CONFIRMADO.
        context.ChangeTracker.Clear();
        var pedido = await context.Pedidos.IgnoreQueryFilters().FirstAsync(p => p.Id == pedidoId);
        pedido.EstadoPedidoId.Should().NotBe(
            (await context.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Confirmado)).Id,
            "la validación debe cortar ANTES del cambio de estado");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_ProductoSoftDeleted_ThrowsInvalidOperationException()
    {
        // Tarea 4.3 RED (rama soft-delete): el QueryFilter global oculta
        // productos soft-deleted, pero al confirmar el pedido queremos
        // detectar el caso via IgnoreQueryFilters. El test verifica que el
        // mismo throw cubre el caso de borrado lógico.
        var (service, context) = NewService(nameof(RegistrarCanjePedidoAsync_ProductoSoftDeleted_ThrowsInvalidOperationException));
        var (pedidoId, productoId, _) = await SeedPedidoActivoAsync(context, softDeleteProducto: true);

        var codigosPorItem = new Dictionary<ulong, List<string>>();

        var act = async () => await service.RegistrarCanjePedidoAsync(pedidoId, codigosPorItem, usuarioId: 1);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*Garrafa 10kg*")
          .WithMessage("*desactivado*");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_TodosProductosActivos_AceptaConfirmacion()
    {
        // Tarea 4.3 — happy path / triangulación: con todos los productos
        // activos, el path VENTA-only (ConfirmarSinCanjeAsync) confirma el
        // pedido sin tirar. El pedido debe quedar en CONFIRMADO.
        var (service, context) = NewService(nameof(RegistrarCanjePedidoAsync_TodosProductosActivos_AceptaConfirmacion));
        var (pedidoId, _, _) = await SeedPedidoActivoAsync(context);

        var codigosPorItem = new Dictionary<ulong, List<string>>();

        var ok = await service.RegistrarCanjePedidoAsync(pedidoId, codigosPorItem, usuarioId: 1);

        ok.Should().BeTrue();
        context.ChangeTracker.Clear();
        var pedido = await context.Pedidos.IgnoreQueryFilters().FirstAsync(p => p.Id == pedidoId);
        var confirmadoId = (await context.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Confirmado)).Id;
        pedido.EstadoPedidoId.Should().Be(confirmadoId);
    }

    /// <summary>
    /// Stub de <see cref="IGarrafaService"/> que lanza si algo lo invoca
    /// durante el path VENTA-only (ConfirmarSinCanjeAsync no usa el service).
    /// Falla ruidosa ante refactor accidental.
    /// </summary>
    private sealed class NotImplementedGarrafaService : IGarrafaService
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
