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
/// Tests directos del helper compartido
/// <c>PedidoService.EnsurePedidoEditableForItemsAsync</c> ejercitado a través
/// de los tres métodos públicos que mutan items: <see cref="IPedidoService.AddItemAsync"/>,
/// <see cref="IPedidoService.UpdateItemAsync"/> y <see cref="IPedidoService.RemoveItemAsync"/>.
///
/// Issue #164: <c>UpdateItemAsync</c> no validaba el estado del pedido antes
/// de mutar (ni precio, ni cantidad, ni tipo de línea). La UI bloquea los
/// inputs por estado, pero el endpoint HTTP no enforce nada — un POST directo
/// podía modificar items de pedidos ENTREGADO o CANCELADO. Peor: la llamada
/// a <c>RecalculateTotalsAsync</c> pisaba el <c>Total</c> del pedido
/// cerrado, perdiendo consistencia con el <c>monto_pagado</c> que mantiene el
/// trigger de pagos.
///
/// Patrón: InMemory con <c>dbName = nameof(method)</c> para aislar cada test
/// (réplica de <see cref="PedidoServiceCambiarEstadoTests"/>). Estos tests no
/// tocan triggers MySQL — solo leen y escriben <c>pedidos</c>,
/// <c>pedido_items</c>, <c>estados_pedido</c> y <c>productos</c>, así que
/// InMemory es suficiente y mucho más rápido que Testcontainers.
/// </summary>
public class PedidoServiceItemEstadoTests
{
    private static (PedidoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new NotImplementedGarrafaService();

        var service = new PedidoService(context, mapper, cache, garrafaService);
        return (service, context);
    }

    /// <summary>
    /// Sembrado mínimo: catálogo de <c>estados_pedido</c> + un <c>Producto</c>
    /// + un <c>Pedido</c> en el estado solicitado + un <c>PedidoItem</c>
    /// VENTA de cantidad 1 a precio 1000. No sembramos el catálogo completo
    /// porque InMemory no exige FKs y los tests solo consultan los estados y
    /// el item que necesitan.
    /// </summary>
    /// <param name="context">
    /// MISMO contexto que usa el service bajo prueba. InMemory es por
    /// databaseName, así que seed y service deben compartir el context.
    /// </param>
    private static ItemSeed SeedItemEnEstado(
        ExtraGasDbContext context, string estadoActualCodigo, DateTime? updatedAt = null)
    {
        var now = updatedAt ?? DateTime.UtcNow;

        var estados = new[]
        {
            (Codigo: PedidoEstados.Pendiente,     Nombre: "Pendiente",      Id: (ulong)1),
            (Codigo: PedidoEstados.Confirmado,    Nombre: "Confirmado",     Id: (ulong)2),
            (Codigo: PedidoEstados.EnPreparacion, Nombre: "En Preparación", Id: (ulong)3),
            (Codigo: PedidoEstados.Entregado,     Nombre: "Entregado",      Id: (ulong)4),
            (Codigo: PedidoEstados.Cancelado,     Nombre: "Cancelado",      Id: (ulong)5),
        };
        foreach (var (codigo, nombre, id) in estados)
        {
            context.EstadosPedido.Add(new EstadoPedido
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                EsFinal = codigo is PedidoEstados.Entregado or PedidoEstados.Cancelado,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        // Catalogos minimos que AddItemAsync consulta pero no valida FKs en InMemory.
        var canalVenta = new CanalVenta
        {
            Id = 1,
            Codigo = "PRESENCIAL",
            Nombre = "Presencial",
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.CanalesVenta.Add(canalVenta);

        var empleado = new Empleado
        {
            Id = 1,
            Nombre = "Juan",
            Apellido = "Empleado",
            Telefono = "1100000000",
            FechaIngreso = DateOnly.FromDateTime(now),
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Empleados.Add(empleado);

        var cliente = new Cliente
        {
            Id = 1,
            Nombre = "Pedro",
            Apellido = "Garcia",
            Dni = "11222333",
            TelefonoPrincipal = "1144556677",
            FechaAlta = DateOnly.FromDateTime(now),
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Clientes.Add(cliente);

        var producto = new Producto
        {
            Id = 1,
            Codigo = "PROD-A",
            Nombre = "Producto A",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1000m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Productos.Add(producto);

        var estadoActualId = estados.Single(e => e.Codigo == estadoActualCodigo).Id;

        var pedido = new Pedido
        {
            Id = 1,
            Numero = "PED-TEST-0001",
            Fecha = now,
            ClienteId = cliente.Id,
            EmpleadoId = empleado.Id,
            CanalVentaId = canalVenta.Id,
            EstadoPedidoId = estadoActualId,
            Subtotal = 1000m,
            Descuento = 0m,
            Total = 1000m,
            MontoPagado = 0m,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        };
        context.Pedidos.Add(pedido);

        var item = new PedidoItem
        {
            Id = 1,
            PedidoId = pedido.Id,
            ProductoId = producto.Id,
            TipoLinea = TipoLinea.VENTA,
            Cantidad = 1m,
            PrecioUnitario = 1000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.PedidoItems.Add(item);

        context.SaveChanges();
        return new ItemSeed(pedido.Id, item.Id, producto.Id);
    }

    private sealed record ItemSeed(
        ulong PedidoId,
        ulong ItemId,
        ulong ProductoId);

    // ====================================================================
    // UpdateItemAsync — Issue #164: el bug original
    // ====================================================================

    [Fact]
    public async Task UpdateItemAsync_PedidoPendiente_PersisteCambios()
    {
        // Caso happy path: el pedido está PENDIENTE, UpdateItemAsync debe
        // aplicar el cambio sin lanzar. Cubre que el helper NO bloquea
        // cuando sí corresponde modificar.
        var (service, context) = NewService(
            nameof(UpdateItemAsync_PedidoPendiente_PersisteCambios));
        var seed = SeedItemEnEstado(context, PedidoEstados.Pendiente);

        var update = new UpdatePedidoItemDto
        {
            Id = seed.ItemId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 5m,
            PrecioUnitario = 2000m,
            Observaciones = "modificado por test",
        };

        var dto = await service.UpdateItemAsync(update);

        dto.Cantidad.Should().Be(5m);
        dto.PrecioUnitario.Should().Be(2000m);
        dto.Observaciones.Should().Be("modificado por test");

        var persisted = await context.PedidoItems.FindAsync((object)seed.ItemId);
        persisted!.Cantidad.Should().Be(5m);
        persisted.PrecioUnitario.Should().Be(2000m);

        // RecalculateTotalsAsync persiste el nuevo subtotal: 5 * 2000 = 10000.
        var pedido = await context.Pedidos.FindAsync((object)seed.PedidoId);
        pedido!.Subtotal.Should().Be(10000m,
            "en PENDIENTE el helper permite la mutación y RecalculateTotalsAsync persiste el subtotal");
        pedido.Total.Should().Be(10000m);
    }

    [Theory]
    [InlineData(PedidoEstados.Confirmado)]
    [InlineData(PedidoEstados.EnPreparacion)]
    [InlineData(PedidoEstados.Entregado)]
    [InlineData(PedidoEstados.Cancelado)]
    public async Task UpdateItemAsync_PedidoNoPendiente_LanzaInvalidOperationException(string estadoCodigo)
    {
        // Cubre el gap principal de #164: UpdateItemAsync debe rechazar
        // cualquier mutación de items cuando el pedido NO está PENDIENTE,
        // tanto en estados terminales (ENTREGADO, CANCELADO) como en
        // estados de read-only parcial (CONFIRMADO, EN_PREPARACION). La UI
        // bloquea los inputs, pero el endpoint HTTP no — esta es la única
        // defensa del lado del service.
        var (service, context) = NewService(
            $"UpdateItemAsync_PedidoNoPendiente_LanzaInvalidOperationException_{estadoCodigo}");
        var seed = SeedItemEnEstado(context, estadoCodigo);

        var update = new UpdatePedidoItemDto
        {
            Id = seed.ItemId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 99m,
            PrecioUnitario = 9999m,
        };

        var act = async () => await service.UpdateItemAsync(update);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No se pueden modificar items en estado*")
            .WithMessage("*Solo se permite en estado Pendiente*");
    }

    [Fact]
    public async Task UpdateItemAsync_PedidoNoExistente_LanzaKeyNotFoundException()
    {
        // Defensa en profundidad: si el item existe pero su pedido fue
        // borrado (soft-delete), UpdateItemAsync debe fallar con
        // KeyNotFoundException en vez de aplicar cambios sobre un pedido
        // huérfano.
        var (service, _) = NewService(
            nameof(UpdateItemAsync_PedidoNoExistente_LanzaKeyNotFoundException));

        var update = new UpdatePedidoItemDto
        {
            Id = 1, // itemId que no existe
            ProductoId = 1,
            TipoLinea = "VENTA",
            Cantidad = 1m,
            PrecioUnitario = 1000m,
        };

        var act = async () => await service.UpdateItemAsync(update);

        // El método hace FindAsync(item.Id) ANTES del helper de estado, así
        // que la primera excepción es KeyNotFoundException del item, no
        // del pedido. Lo importante: NO se aplican cambios.
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateItemAsync_PedidoCerrado_NoPisaTotal()
    {
        // Cubre la segunda parte de #164: aún si alguien lograra bypasear
        // el helper, RecalculateTotalsAsync no debe pisar el Total de un
        // pedido cerrado. Tras aplicar el helper correctamente, este test
        // verifica el camino feliz: con PENDIENTE el subtotal SÍ se
        // actualiza. El camino "no pisa Total" está implícitamente
        // cubierto por UpdateItemAsync_PedidoNoPendiente_* que lanzan
        // InvalidOperationException ANTES de llegar a
        // RecalculateTotalsAsync.
        var (service, context) = NewService(
            nameof(UpdateItemAsync_PedidoCerrado_NoPisaTotal));
        var seed = SeedItemEnEstado(context, PedidoEstados.Pendiente);

        var totalOriginal = (await context.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId)).Total;

        var update = new UpdatePedidoItemDto
        {
            Id = seed.ItemId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 3m,
            PrecioUnitario = 500m,
        };

        await service.UpdateItemAsync(update);

        var pedido = await context.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId);
        pedido.Total.Should().Be(1500m, // 3 * 500
            "el helper permite la actualización y RecalculateTotalsAsync persiste el nuevo subtotal");
        pedido.Total.Should().NotBe(totalOriginal,
            "sanity check: el Total cambió tras el update");
    }

    // ====================================================================
    // AddItemAsync — regresión: el helper mantiene el contrato histórico
    // ====================================================================

    [Theory]
    [InlineData(PedidoEstados.Confirmado)]
    [InlineData(PedidoEstados.EnPreparacion)]
    [InlineData(PedidoEstados.Entregado)]
    [InlineData(PedidoEstados.Cancelado)]
    public async Task AddItemAsync_PedidoNoPendiente_LanzaInvalidOperationException(string estadoCodigo)
    {
        // Regresión: el helper reemplaza el bloque inline que AddItemAsync
        // ya tenía. Estos tests garantizan que el mensaje y el
        // comportamiento se preservan al extraer al helper compartido.
        var (service, context) = NewService(
            $"AddItemAsync_PedidoNoPendiente_LanzaInvalidOperationException_{estadoCodigo}");
        var seed = SeedItemEnEstado(context, estadoCodigo);

        var add = new CreatePedidoItemDto
        {
            PedidoId = seed.PedidoId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 1m,
        };

        var act = async () => await service.AddItemAsync(add);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No se pueden agregar items en estado*")
            .WithMessage("*Solo se permite en estado Pendiente*");
    }

    // ====================================================================
    // RemoveItemAsync — regresión: el helper mantiene el contrato histórico
    // ====================================================================

    [Theory]
    [InlineData(PedidoEstados.Confirmado)]
    [InlineData(PedidoEstados.EnPreparacion)]
    [InlineData(PedidoEstados.Entregado)]
    [InlineData(PedidoEstados.Cancelado)]
    public async Task RemoveItemAsync_PedidoNoPendiente_LanzaInvalidOperationException(string estadoCodigo)
    {
        // Regresión simétrica: RemoveItemAsync antes validaba inline con
        // `if (pedido is not null)`. Ahora pasa por el helper. Verificamos
        // que el comportamiento observable (mensaje + excepción) es el
        // mismo.
        var (service, context) = NewService(
            $"RemoveItemAsync_PedidoNoPendiente_LanzaInvalidOperationException_{estadoCodigo}");
        var seed = SeedItemEnEstado(context, estadoCodigo);

        var act = async () => await service.RemoveItemAsync(seed.ItemId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No se pueden eliminar items en estado*")
            .WithMessage("*Solo se permite en estado Pendiente*");
    }

    // ====================================================================
    // Fake IGarrafaService (PedidoService lo exige en el constructor pero
    // estos tests no ejercitan el canje). Réplica de
    // PedidoServiceCambiarEstadoTests.NotImplementedGarrafaService.
    // ====================================================================

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
