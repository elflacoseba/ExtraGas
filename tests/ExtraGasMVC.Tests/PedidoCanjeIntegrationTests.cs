using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integración del flujo de canje de
/// <see cref="PedidoService.RegistrarCanjePedidoAsync"/> contra un MySQL real
/// dentro de un container Docker (Testcontainers.MySql).
///
/// Issue #138: el PR #137 cerró las 19 issues SonarQube en new code, pero
/// quedó en <c>new_coverage: 44%</c> (threshold 80%) porque este método y sus
/// helpers privados (LoadCatalogosParaCanjeAsync, LoadItemsParaCanjeAsync,
/// AplicarCanjeYConfirmarAsync, etc.) no tenían ningún test. La cobertura de
/// InMemory no alcanza porque el canje ejecuta escrituras en transacciones
/// reales contra triggers MySQL (<c>trg_mov_garrafa_ai</c>) que actualizan
/// <c>garrafas.estado_garrafa_id</c> en respuesta al INSERT del movimiento.
///
/// Patrón: IClassFixture comparte el container entre los tests (los containers
/// son caros de arrancar). Cada test crea su propia base y aplica el schema
/// mínimo para tener aislamiento. Réplica del patrón de
/// <see cref="ClienteMySqlFixture"/>.
/// </summary>
public class PedidoCanjeIntegrationTests : IClassFixture<PedidoCanjeMySqlFixture>
{
    private readonly PedidoCanjeMySqlFixture _fixture;

    public PedidoCanjeIntegrationTests(PedidoCanjeMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    // ====================================================================
    // Happy path
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_HappyPath_EntregaYDevolucion_CreaMovimientosYConfirmaPedido()
    {
        // Cubre: LoadPedidoParaCanjeAsync, AsegurarNoCanjeadoAsync,
        // LoadCatalogosParaCanjeAsync, LoadItemsParaCanjeAsync,
        // ValidarItemsSonGarrafaCanjeable, NormalizarYValidarCodigos,
        // ValidarCodigosContraInventarioAsync, AplicarCanjeYConfirmarAsync,
        // AplicarCanjeDeItemAsync y la cadena hasta el COMMIT de la transacción.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_HappyPath_EntregaYDevolucion_CreaMovimientosYConfirmaPedido));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: true);
        // El seed crea: 2 garrafas LLENA_DEPOSITO (entrega) + 1 garrafa EN_CLIENTE del cliente (devolución).
        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
            [seed.ItemDevolucionId] = new() { seed.CodigoDevolucion },
        };

        var ok = await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        ok.Should().BeTrue();

        // 1) Pedido pasó a CONFIRMADO.
        var pedidoFinal = await ctx.Pedidos.IgnoreQueryFilters()
            .FirstAsync(p => p.Id == seed.PedidoId);
        var confirmado = await ctx.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Confirmado);
        pedidoFinal.EstadoPedidoId.Should().Be(confirmado.Id);
        pedidoFinal.UpdatedBy.Should().Be(1);

        // 2) Se generaron 3 movimientos de garrafa (2 entrega + 1 devolución),
        //    todos apuntando al pedido.
        var movimientos = await ctx.MovimientosGarrafa
            .Where(m => m.PedidoId == seed.PedidoId)
            .OrderBy(m => m.Id)
            .ToListAsync();
        movimientos.Should().HaveCount(3);
        movimientos.Should().OnlyContain(m => m.PedidoId == seed.PedidoId);

        // 3) Estado origen/destino y tipo de movimiento de cada uno.
        var entregaCliente = await ctx.TiposMovimientoGarrafa
            .FirstAsync(t => t.Codigo == "ENTREGA_CLIENTE");
        var devolucionCliente = await ctx.TiposMovimientoGarrafa
            .FirstAsync(t => t.Codigo == "DEVOLUCION_CLIENTE");
        var llenaDeposito = await ctx.EstadosGarrafa.FirstAsync(e => e.Codigo == GarrafaEstados.LlenaDeposito);
        var enCliente = await ctx.EstadosGarrafa.FirstAsync(e => e.Codigo == GarrafaEstados.EnCliente);

        movimientos.Count(m => m.TipoMovimientoId == entregaCliente.Id
                            && m.EstadoDestinoId == enCliente.Id
                            && m.ClienteId == seed.ClienteId).Should().Be(2,
            "2 entregas a cliente");

        movimientos.Count(m => m.TipoMovimientoId == devolucionCliente.Id
                            && m.EstadoDestinoId == llenaDeposito.Id
                            && m.ClienteId == null).Should().Be(1,
            "1 devolución sin cliente (vuelve al depósito)");

        // 4) Trigger trg_mov_garrafa_ai actualizó garrafa.estado_garrafa_id.
        // Usamos AsNoTracking para no leer del tracker de EF (que cachea el
        // estado anterior al canje).
        var garrafas = await ctx.Garrafas.IgnoreQueryFilters().AsNoTracking()
            .Where(g => seed.CodigosEntrega.Concat(new[] { seed.CodigoDevolucion }).Contains(g.Codigo))
            .ToListAsync();
        garrafas.Where(g => seed.CodigosEntrega.Contains(g.Codigo))
            .Should().OnlyContain(g => g.EstadoGarrafaId == enCliente.Id
                                    && g.ClienteId == seed.ClienteId);
        garrafas.Single(g => g.Codigo == seed.CodigoDevolucion)
            .EstadoGarrafaId.Should().Be(llenaDeposito.Id);
        garrafas.Single(g => g.Codigo == seed.CodigoDevolucion)
            .ClienteId.Should().BeNull("la devolución limpia el cliente_id");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_SoloEntrega_SinDevolucion_TambienConfirma()
    {
        // Cubre la rama donde solo hay items ENTREGA. La tabla
        // codigosPorItem incluye solo el item de entrega; no hay itemDevolucion.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_SoloEntrega_SinDevolucion_TambienConfirma));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);
        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
        };

        var ok = await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        ok.Should().BeTrue();
        var movimientos = await ctx.MovimientosGarrafa
            .Where(m => m.PedidoId == seed.PedidoId).ToListAsync();
        movimientos.Should().HaveCount(2);
    }

    // ====================================================================
    // Idempotencia
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_DosVecesSobreElMismoPedido_RechazaPorEstadoConfirmado()
    {
        // Cubre LoadPedidoParaCanjeAsync: la segunda invocación rechaza el
        // pedido que ya quedó en CONFIRMADO tras el primer canje exitoso.
        // (AsegurarNoCanjeadoAsync es la red de seguridad para el caso más
        // raro en que el pedido se revirtió a PENDIENTE sin deshacer los
        // movimientos — no cubierto por este test del happy-path).
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_DosVecesSobreElMismoPedido_RechazaPorEstadoConfirmado));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);
        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
        };

        await service.RegistrarCanjePedidoAsync(seed.PedidoId, codigosPorItem, usuarioId: 1);

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ya se encuentra en estado CONFIRMADO*");

        // La cantidad de movimientos no se duplicó: sigue habiendo 2.
        var total = await ctx.MovimientosGarrafa.AsNoTracking()
            .CountAsync(m => m.PedidoId == seed.PedidoId);
        total.Should().Be(2);
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_PedidoConMovimientosPrevios_RechazaPorIdempotencia()
    {
        // Cubre AsegurarNoCanjeadoAsync: si ya hay movimientos registrados
        // para el pedido (sin importar el estado), el canje rechaza. Para
        // forzar este camino sin pasar por la validación de estado, dejamos
        // el pedido en PENDIENTE pero le insertamos un movimiento "huérfano"
        // por raw SQL — el EF tracker del pedido en PENDIENTE engaña al
        // LoadPedidoParaCanjeAsync, así que llegamos a AsegurarNoCanjeadoAsync.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_PedidoConMovimientosPrevios_RechazaPorIdempotencia));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);

        // Forzar el escenario: el pedido sigue en PENDIENTE (tracker), pero
        // la BD tiene un movimiento previo para el mismo pedido.
        var conn = (MySqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        var entregaTipoId = (await ctx.TiposMovimientoGarrafa.AsNoTracking()
            .FirstAsync(t => t.Codigo == "ENTREGA_CLIENTE")).Id;
        var enClienteId = (await ctx.EstadosGarrafa.AsNoTracking()
            .FirstAsync(e => e.Codigo == GarrafaEstados.EnCliente)).Id;
        var garrafaId = (await ctx.Garrafas.AsNoTracking()
            .FirstAsync(g => g.Codigo == seed.CodigosEntrega[0])).Id;

        await using (var cmd = conn.CreateCommand())
        {
            // Movimiento huérfano: disparará el trigger que pasa la garrafa a
            // EN_CLIENTE (no afecta al test, solo queremos un mov previo).
            cmd.CommandText = @"INSERT INTO movimientos_garrafa
                (garrafa_id, fecha, tipo_movimiento_id, pedido_id, estado_origen_id, estado_destino_id)
                VALUES (@gar, NOW(), @tip, @ped, @est_origen, @est_destino);";
            cmd.Parameters.Add(new MySqlParameter("@gar", garrafaId));
            cmd.Parameters.Add(new MySqlParameter("@tip", entregaTipoId));
            cmd.Parameters.Add(new MySqlParameter("@ped", seed.PedidoId));
            cmd.Parameters.Add(new MySqlParameter("@est_origen", enClienteId));
            cmd.Parameters.Add(new MySqlParameter("@est_destino", enClienteId));
            await cmd.ExecuteNonQueryAsync();
        }

        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ya tiene movimientos de canje*");
    }

    // ====================================================================
    // Catálogo incompleto
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_TipoMovimientoFaltante_LanzaInvalidOperationException()
    {
        // Cubre LoadCatalogosParaCanjeAsync cuando falta el tipo de
        // movimiento DEVOLUCION_CLIENTE en la tabla tipos_movimiento_garrafa.
        // El test elimina la fila antes de invocar el service.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_TipoMovimientoFaltante_LanzaInvalidOperationException));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: true);
        await ctx.TiposMovimientoGarrafa
            .Where(t => t.Codigo == "DEVOLUCION_CLIENTE")
            .ExecuteDeleteAsync();

        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
            [seed.ItemDevolucionId] = new() { seed.CodigoDevolucion },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ENTREGA_CLIENTE*DEVOLUCION_CLIENTE*");

        // Ningún movimiento se persistió.
        var total = await ctx.MovimientosGarrafa.CountAsync(m => m.PedidoId == seed.PedidoId);
        total.Should().Be(0);

        // El pedido sigue en PENDIENTE (la transacción hizo rollback).
        var pedido = await ctx.Pedidos.IgnoreQueryFilters().FirstAsync(p => p.Id == seed.PedidoId);
        var pendiente = await ctx.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Pendiente);
        pedido.EstadoPedidoId.Should().Be(pendiente.Id);
    }

    // ====================================================================
    // Validaciones de items y códigos
    // ====================================================================

    [Fact]
    public async Task RegistrarCanjePedidoAsync_ItemNoGarrafaCanjeable_LanzaInvalidOperationException()
    {
        // Cubre ValidarItemsSonGarrafaCanjeable: el service rechaza el canje
        // si el item tiene tipo VENTA (carbón, leña) o si el producto no
        // maneja garrafas individuales.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_ItemNoGarrafaCanjeable_LanzaInvalidOperationException));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);

        // Cambiar el tipo del item existente a VENTA (carbón/leña). El item
        // debe seguir siendo GARRAFA-capaz (ManejaGarrafaIndividual=true),
        // pero con tipo_linea=VENTA — el service debe rechazar.
        var item = await ctx.PedidoItems.FirstAsync(i => i.Id == seed.ItemEntregaId);
        item.TipoLinea = TipoLinea.VENTA;
        await ctx.SaveChangesAsync();

        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*solo ENTREGA o DEVOLUCION*");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_CantidadCodigosNoCoincideConItem_LanzaInvalidOperationException()
    {
        // Cubre NormalizarYValidarCodigos: si el operador carga menos (o
        // más) códigos que la cantidad del item, el service rechaza.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_CantidadCodigosNoCoincideConItem_LanzaInvalidOperationException));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);
        // El item pide 2 garrafas pero mandamos solo 1.
        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0] },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*esperaba 2*código*recibió 1*");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_GarrafaNoPerteneceAlCliente_LanzaInvalidOperationException()
    {
        // Cubre ValidarCodigosContraInventarioAsync: una DEVOLUCION exige que
        // la garrafa esté en estado EN_CLIENTE Y sea del cliente del pedido.
        // Cargamos una garrafa EN_CLIENTE de OTRO cliente y verificamos que
        // el service rechaza.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_GarrafaNoPerteneceAlCliente_LanzaInvalidOperationException));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: true);

        // Crear un cliente extra + una garrafa EN_CLIENTE de ese cliente.
        var now = DateTime.UtcNow;
        var fechaCompra = DateOnly.FromDateTime(now);

        var otroCliente = new Cliente
        {
            Nombre = "Otro",
            Apellido = "Cliente",
            TelefonoPrincipal = "1144449999",
            FechaAlta = fechaCompra,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Clientes.Add(otroCliente);
        await ctx.SaveChangesAsync();

        var enClienteEstadoId = (await ctx.EstadosGarrafa.FirstAsync(e => e.Codigo == GarrafaEstados.EnCliente)).Id;
        var garrafaAjena = new Garrafa
        {
            Codigo = "G-DEVOL-AJENA",
            CapacidadKg = 10,
            FechaCompra = fechaCompra,
            EstadoGarrafaId = enClienteEstadoId,
            ClienteId = otroCliente.Id, // <- del OTRO cliente, no del pedido
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Garrafas.Add(garrafaAjena);
        await ctx.SaveChangesAsync();

        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], seed.CodigosEntrega[1] },
            [seed.ItemDevolucionId] = new() { garrafaAjena.Codigo },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pertenece al cliente*");
    }

    [Fact]
    public async Task RegistrarCanjePedidoAsync_CodigoInexistente_LanzaInvalidOperationException()
    {
        // Cubre ValidarCodigosContraInventarioAsync cuando el código físico
        // no existe en la tabla garrafas.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RegistrarCanjePedidoAsync_CodigoInexistente_LanzaInvalidOperationException));
        var service = NewService(ctx);

        var seed = await SeedPedidoCompletoAsync(ctx, incluirDevolucion: false);
        var codigosPorItem = new Dictionary<ulong, List<string>>
        {
            [seed.ItemEntregaId] = new() { seed.CodigosEntrega[0], "G-NO-EXISTE" },
        };

        var act = async () => await service.RegistrarCanjePedidoAsync(
            seed.PedidoId, codigosPorItem, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*G-NO-EXISTE*no existe en el inventario*");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static PedidoService NewService(ExtraGasDbContext context)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new GarrafaService(context, mapper, NullLogger<GarrafaService>.Instance);
        return new PedidoService(context, mapper, cache, garrafaService);
    }

    /// <summary>
    /// Sembrado mínimo del escenario de canje. Devuelve los IDs y códigos
    /// físicos necesarios para que cada test arme el diccionario de
    /// codigosPorItem a su manera.
    ///
    /// Estructura del pedido:
    ///   - estado PENDIENTE
    ///   - 1 item ENTREGA de GARRAFA 10kg, cantidad 2
    ///   - (opcional) 1 item DEVOLUCION de GARRAFA 10kg, cantidad 1
    ///   - 2 garrafas LLENA_DEPOSITO (serán entregadas al cliente)
    ///   - (opcional) 1 garrafa EN_CLIENTE del cliente del pedido (será devuelta)
    /// </summary>
    private async Task<CanjeSeed> SeedPedidoCompletoAsync(
        ExtraGasDbContext ctx, bool incluirDevolucion)
    {
        // Catálogos (idempotente gracias a INSERT IGNORE + ON DUPLICATE KEY).
        var pendienteId = (await ctx.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Pendiente)).Id;
        var confirmadaId = (await ctx.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Confirmado)).Id;

        var llenaDepositoId = (await ctx.EstadosGarrafa.FirstAsync(e => e.Codigo == GarrafaEstados.LlenaDeposito)).Id;
        var enClienteId = (await ctx.EstadosGarrafa.FirstAsync(e => e.Codigo == GarrafaEstados.EnCliente)).Id;

        await ctx.TiposMovimientoGarrafa.FirstAsync(t => t.Codigo == "ENTREGA_CLIENTE");
        await ctx.TiposMovimientoGarrafa.FirstAsync(t => t.Codigo == "DEVOLUCION_CLIENTE");

        var canalVentaId = (await ctx.CanalesVenta.FirstAsync()).Id;
        var tipoProductoId = (await ctx.TiposProducto.FirstAsync()).Id;

        var now = DateTime.UtcNow;
        var fechaCompra = DateOnly.FromDateTime(now);

        // Empleado.
        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Empleado",
            Telefono = "1100000000",
            FechaIngreso = fechaCompra,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Empleados.Add(empleado);
        await ctx.SaveChangesAsync();

        // Cliente.
        var cliente = new Cliente
        {
            Nombre = "Pedro",
            Apellido = "Garcia",
            Dni = "11222333",
            TelefonoPrincipal = "1144556677",
            FechaAlta = fechaCompra,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        // Producto GARRAFA-capaz (ManejaGarrafaIndividual = true).
        var producto = new Producto
        {
            Codigo = "GAR-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = tipoProductoId,
            CapacidadKg = 10m,
            UnidadVenta = "UNIDAD",
            PrecioActual = 15000m,
            ManejaGarrafaIndividual = true,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        // Pedido PENDIENTE.
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

        // Item ENTREGA cantidad 2.
        var itemEntrega = new PedidoItem
        {
            PedidoId = pedido.Id,
            ProductoId = producto.Id,
            TipoLinea = TipoLinea.ENTREGA,
            Cantidad = 2m,
            PrecioUnitario = 15000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.PedidoItems.Add(itemEntrega);
        await ctx.SaveChangesAsync();

        ulong itemDevolucionId = 0;
        if (incluirDevolucion)
        {
            var itemDevolucion = new PedidoItem
            {
                PedidoId = pedido.Id,
                ProductoId = producto.Id,
                TipoLinea = TipoLinea.DEVOLUCION,
                Cantidad = 1m,
                PrecioUnitario = 15000m,
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.PedidoItems.Add(itemDevolucion);
            await ctx.SaveChangesAsync();
            itemDevolucionId = itemDevolucion.Id;
        }

        // Garrafas: 2 LLENAS (entrega) + 1 EN_CLIENTE (devolución, si aplica).
        var codigosEntrega = new List<string> { "G-LLENA-001", "G-LLENA-002" };
        foreach (var codigo in codigosEntrega)
        {
            ctx.Garrafas.Add(new Garrafa
            {
                Codigo = codigo,
                CapacidadKg = 10,
                FechaCompra = fechaCompra,
                EstadoGarrafaId = llenaDepositoId,
                ClienteId = null,
                Activo = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        string codigoDevolucion = "";
        if (incluirDevolucion)
        {
            codigoDevolucion = "G-DEVUELTA-001";
            ctx.Garrafas.Add(new Garrafa
            {
                Codigo = codigoDevolucion,
                CapacidadKg = 10,
                FechaCompra = fechaCompra,
                EstadoGarrafaId = enClienteId,
                ClienteId = cliente.Id,
                Activo = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await ctx.SaveChangesAsync();

        return new CanjeSeed(
            PedidoId: pedido.Id,
            ClienteId: cliente.Id,
            ItemEntregaId: itemEntrega.Id,
            ItemDevolucionId: itemDevolucionId,
            CodigosEntrega: codigosEntrega,
            CodigoDevolucion: codigoDevolucion);
    }

    private sealed record CanjeSeed(
        ulong PedidoId,
        ulong ClienteId,
        ulong ItemEntregaId,
        ulong ItemDevolucionId,
        List<string> CodigosEntrega,
        string CodigoDevolucion);
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers y provee
/// un método para crear bases frescas con el schema mínimo del módulo
/// Pedidos + Garrafas (lo necesario para
/// <see cref="PedidoService.RegistrarCanjePedidoAsync"/>).
///
/// Réplica del patrón de <see cref="ClienteMySqlFixture"/>. Se comparte entre
/// los tests de <see cref="PedidoCanjeIntegrationTests"/> via IClassFixture
/// — los containers tardan segundos en arrancar, no vale la pena pagar ese
/// costo por test.
///
/// Requiere Docker daemon accesible. Si el CI no tiene Docker, los tests se
/// pueden saltar con un filtro de xUnit a nivel de pipeline
/// (ej. <c>dotnet test --filter "FullyQualifiedName!~IntegrationTests"</c>).
/// </summary>
public class PedidoCanjeMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    // MySQL limita identificadores a 64 chars. Prefix corto + Guid.NewGuid().ToString("N")
    // (32 chars hex) = 2 + 32 = 34 chars, lejos del límite.
    private const string DatabasePrefix = "pc_";

    private MySqlContainer? _container;
    private string? _rootConnectionString;

    public async Task InitializeAsync()
    {
        _container = new MySqlBuilder()
            .WithImage(MysqlImage)
            .WithUsername(RootUsername)
            .WithPassword(RootPassword)
            .WithDatabase("placeholder_db")
            .Build();
        await _container.StartAsync();
        _rootConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// Crea una base nueva con nombre único (prefix + GUID) y aplica el
    /// schema mínimo necesario para que
    /// <see cref="PedidoService.RegistrarCanjePedidoAsync"/> funcione contra
    /// MySQL real: clientes + empleados + lookup tables + pedidos con
    /// transacción + trigger <c>trg_mov_garrafa_ai</c> que mantiene
    /// <c>garrafas.estado_garrafa_id</c> sincronizado con los INSERTs en
    /// <c>movimientos_garrafa</c>.
    /// </summary>
    public async Task<ExtraGasDbContext> NewDbContextAsync(string testName)
    {
        _ = testName; // reservado para logging futuro; el nombre es GUID.
        var dbName = DatabasePrefix + Guid.NewGuid().ToString("N");

        // 1) Crear la base vía root connection.
        await using (var conn = new MySqlConnection(_rootConnectionString))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{dbName}` " +
                                 "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await create.ExecuteNonQueryAsync();
        }

        // 2) Conectar a la nueva base y aplicar el schema mínimo.
        var connString = _rootConnectionString!
            .Replace("database=placeholder_db", $"database={dbName}", StringComparison.OrdinalIgnoreCase);

        await using (var conn = new MySqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var schema = conn.CreateCommand();
            schema.CommandText = PedidoCanjeSchemaMinimal;
            await schema.ExecuteNonQueryAsync();
        }

        // 3) Armar DbContext con Pomelo contra esa base.
        var serverVersion = ServerVersion.Create(new Version(8, 0, 0), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseMySql(connString, serverVersion)
            .Options;
        return new ExtraGasDbContext(options);
    }

    /// <summary>
    /// Cierra un DbContext abierto por <see cref="NewDbContextAsync"/> y
    /// elimina su base efímera. Helper para el patrón using en tests que
    /// necesitan cleanup explícito (vs. el default de
    /// <see cref="PedidoCanjeIntegrationTests"/> que confía en el container
    /// dispose al final de la clase).
    ///
    /// Issue #145 Slice 4: agregado para los tests de race condition que
    /// crean múltiples bases por test class y necesitan cleanup por test
    /// para no acumular basura entre runs.
    /// </summary>
    public async Task DropDatabaseAsyncForDbContext(ExtraGasDbContext context)
    {
        var dbName = context.Database.GetDbConnection().Database;
        await context.DisposeAsync();
        await DropDatabaseAsync(dbName);
    }

    /// <summary>
    /// DROP DATABASE IF EXISTS para la base efímera. Réplica del helper en
    /// <see cref="ProductoPrecioHistoricoMySqlFixture"/>. Usado por
    /// <see cref="DropDatabaseAsyncForDbContext"/>.
    /// </summary>
    public async Task DropDatabaseAsync(string dbName)
    {
        await using var conn = new MySqlConnection(_rootConnectionString);
        await conn.OpenAsync();
        await using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS `{dbName}`;";
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Schema mínimo para que <see cref="PedidoService.RegistrarCanjePedidoAsync"/>
    /// funcione contra MySQL real. Incluye solo las tablas + FKs + el trigger
    /// <c>trg_mov_garrafa_ai</c> que el flujo ejercita (PR #137). Reproduce
    /// los DDL relevantes de las migraciones 20260102_000001 / 20260102_000004 /
    /// 20260102_000006 / 20260102_000007.
    /// </summary>
    private const string PedidoCanjeSchemaMinimal = """
        -- Lookups
        CREATE TABLE tipos_producto (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_tipos_producto_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE estados_pedido (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            es_final BOOLEAN NOT NULL DEFAULT FALSE,
            color VARCHAR(7) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_estados_pedido_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE canales_venta (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_canales_venta_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE estados_garrafa (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            es_disponible_para_venta BOOLEAN NOT NULL DEFAULT FALSE,
            requiere_cliente BOOLEAN NOT NULL DEFAULT FALSE,
            color VARCHAR(7) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_estados_garrafa_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE tipos_movimiento_garrafa (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_tipos_movimiento_garrafa_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Personas
        CREATE TABLE empleados (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            nombre VARCHAR(100) NOT NULL,
            apellido VARCHAR(100) NOT NULL,
            dni VARCHAR(15) NULL,
            cuil VARCHAR(15) NULL,
            telefono VARCHAR(25) NULL,
            email VARCHAR(150) NULL,
            calle VARCHAR(150) NULL,
            numero VARCHAR(10) NULL,
            piso VARCHAR(10) NULL,
            depto VARCHAR(10) NULL,
            ciudad VARCHAR(100) NULL,
            codigo_postal VARCHAR(10) NULL,
            provincia_id BIGINT UNSIGNED NULL,
            fecha_ingreso DATE NULL,
            fecha_egreso DATE NULL,
            usuario_id BIGINT UNSIGNED NULL,
            activo BOOLEAN NOT NULL DEFAULT TRUE,
            observaciones TEXT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            KEY idx_empleados_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE clientes (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(20) NULL,
            nombre VARCHAR(100) NOT NULL,
            apellido VARCHAR(100) NOT NULL,
            dni VARCHAR(15) NULL,
            cuit_cuil VARCHAR(15) NULL,
            telefono_principal VARCHAR(25) NOT NULL,
            telefono_secundario VARCHAR(25) NULL,
            email VARCHAR(150) NULL,
            calle VARCHAR(150) NULL,
            numero VARCHAR(10) NULL,
            piso VARCHAR(10) NULL,
            depto VARCHAR(10) NULL,
            ciudad VARCHAR(100) NULL,
            codigo_postal VARCHAR(10) NULL,
            provincia_id BIGINT UNSIGNED NULL,
            referencias TEXT NULL,
            observaciones TEXT NULL,
            fecha_alta DATE NOT NULL,
            activo BOOLEAN NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            dni_unique VARCHAR(15) GENERATED ALWAYS AS (
                CASE WHEN deleted_at IS NULL THEN dni ELSE NULL END
            ) VIRTUAL,
            UNIQUE KEY idx_clientes_dni_unique (dni_unique),
            KEY idx_clientes_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Issue #145 Slice 4: usuarios + proveedores. proveedores debe ir
        -- ANTES de recepciones_proveedor (FK proveedor_id) y el orden de
        -- creación de tablas en este schema es relevante. usuarios va acá
        -- para tener un id=1 sembrado que satisfaga FKs empleados.usuario_id
        -- si algún test los activa.
        CREATE TABLE IF NOT EXISTS usuarios (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            username VARCHAR(50) NOT NULL,
            password_hash VARCHAR(255) NOT NULL,
            email VARCHAR(150) NULL,
            rol_id BIGINT UNSIGNED NOT NULL,
            bloqueado_hasta DATETIME NULL,
            intentos_fallidos INT NOT NULL DEFAULT 0,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_usuarios_username (username)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS proveedores (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(20) NULL,
            razon_social VARCHAR(150) NOT NULL,
            nombre_fantasia VARCHAR(150) NULL,
            cuit VARCHAR(15) NULL,
            telefono_principal VARCHAR(25) NULL,
            telefono_secundario VARCHAR(25) NULL,
            email VARCHAR(150) NULL,
            calle VARCHAR(150) NULL,
            numero VARCHAR(10) NULL,
            piso VARCHAR(10) NULL,
            depto VARCHAR(10) NULL,
            ciudad VARCHAR(100) NULL,
            codigo_postal VARCHAR(10) NULL,
            provincia_id BIGINT UNSIGNED NULL,
            referencias TEXT NULL,
            observaciones TEXT NULL,
            contacto_nombre VARCHAR(150) NULL,
            contacto_telefono VARCHAR(25) NULL,
            contacto_email VARCHAR(150) NULL,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            KEY idx_proveedores_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Productos
        CREATE TABLE productos (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(150) NOT NULL,
            descripcion VARCHAR(255) NULL,
            tipo_producto_id BIGINT UNSIGNED NOT NULL,
            capacidad_kg DECIMAL(8,2) NULL,
            unidad_venta VARCHAR(20) NOT NULL DEFAULT 'UNIDAD',
            precio_actual DECIMAL(12,2) NOT NULL DEFAULT 0,
            maneja_garrafa_individual TINYINT(1) NOT NULL DEFAULT 0,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
            UNIQUE KEY uq_productos_codigo (codigo),
            KEY idx_productos_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Pedidos
        CREATE TABLE pedidos (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            numero VARCHAR(20) NULL,
            fecha DATETIME NOT NULL,
            fecha_entrega DATETIME NULL,
            entregado BOOLEAN NOT NULL DEFAULT FALSE,
            cliente_id BIGINT UNSIGNED NOT NULL,
            empleado_id BIGINT UNSIGNED NOT NULL,
            estado_pedido_id BIGINT UNSIGNED NOT NULL,
            canal_venta_id BIGINT UNSIGNED NOT NULL,
            medio_contacto_id BIGINT UNSIGNED NULL,
            subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
            descuento DECIMAL(12,2) NOT NULL DEFAULT 0,
            total DECIMAL(12,2) NOT NULL DEFAULT 0,
            monto_pagado DECIMAL(12,2) NOT NULL DEFAULT 0,
            saldo DECIMAL(12,2) GENERATED ALWAYS AS (total - monto_pagado) STORED,
            observaciones TEXT NULL,
            motivo_cancelacion VARCHAR(500) NULL,
            direccion_entrega VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            CONSTRAINT fk_pedidos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
            CONSTRAINT fk_pedidos_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
            CONSTRAINT fk_pedidos_estado FOREIGN KEY (estado_pedido_id) REFERENCES estados_pedido(id),
            CONSTRAINT fk_pedidos_canal FOREIGN KEY (canal_venta_id) REFERENCES canales_venta(id),
            CONSTRAINT chk_pedidos_total CHECK (total >= 0),
            CONSTRAINT chk_pedidos_monto_pagado CHECK (monto_pagado >= 0),
            UNIQUE KEY idx_pedidos_numero (numero),
            KEY idx_pedidos_cliente (cliente_id, fecha),
            KEY idx_pedidos_estado (estado_pedido_id),
            KEY idx_pedidos_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE pedido_items (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            pedido_id BIGINT UNSIGNED NOT NULL,
            producto_id BIGINT UNSIGNED NOT NULL,
            tipo_linea ENUM('ENTREGA','DEVOLUCION','VENTA') NOT NULL DEFAULT 'VENTA',
            cantidad DECIMAL(10,2) NOT NULL,
            precio_unitario DECIMAL(12,2) NOT NULL,
            subtotal DECIMAL(12,2) GENERATED ALWAYS AS (cantidad * precio_unitario) STORED,
            observaciones VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_pedido_items_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id) ON DELETE CASCADE,
            CONSTRAINT fk_pedido_items_producto FOREIGN KEY (producto_id) REFERENCES productos(id),
            CONSTRAINT chk_pedido_items_cantidad CHECK (cantidad > 0),
            CONSTRAINT chk_pedido_items_precio CHECK (precio_unitario >= 0),
            KEY idx_pedido_items_pedido (pedido_id),
            KEY idx_pedido_items_producto (producto_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Garrafas y movimientos
        CREATE TABLE garrafas (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(50) NOT NULL,
            capacidad_kg TINYINT UNSIGNED NOT NULL,
            proveedor_id BIGINT UNSIGNED NULL,
            recepcion_id BIGINT UNSIGNED NULL,
            fecha_compra DATE NOT NULL,
            estado_garrafa_id BIGINT UNSIGNED NOT NULL,
            cliente_id BIGINT UNSIGNED NULL,
            activo BOOLEAN NOT NULL DEFAULT TRUE,
            fecha_ultimo_movimiento DATETIME NULL,
            observaciones TEXT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            CONSTRAINT uq_garrafas_codigo UNIQUE (codigo),
            CONSTRAINT chk_garrafas_capacidad CHECK (capacidad_kg IN (10, 15, 45)),
            CONSTRAINT fk_garrafas_estado FOREIGN KEY (estado_garrafa_id) REFERENCES estados_garrafa(id),
            CONSTRAINT fk_garrafas_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
            KEY idx_garrafas_estado (estado_garrafa_id),
            KEY idx_garrafas_cliente (cliente_id),
            KEY idx_garrafas_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        -- (created_by/updated_by ya incluidos arriba)

        CREATE TABLE movimientos_garrafa (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            garrafa_id BIGINT UNSIGNED NOT NULL,
            fecha DATETIME NOT NULL,
            tipo_movimiento_id BIGINT UNSIGNED NOT NULL,
            pedido_id BIGINT UNSIGNED NULL,
            recepcion_id BIGINT UNSIGNED NULL,
            cliente_id BIGINT UNSIGNED NULL,
            estado_origen_id BIGINT UNSIGNED NULL,
            estado_destino_id BIGINT UNSIGNED NOT NULL,
            empleado_id BIGINT UNSIGNED NULL,
            observaciones VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            CONSTRAINT fk_mov_garrafa_garrafa FOREIGN KEY (garrafa_id) REFERENCES garrafas(id),
            CONSTRAINT fk_mov_garrafa_tipo FOREIGN KEY (tipo_movimiento_id) REFERENCES tipos_movimiento_garrafa(id),
            CONSTRAINT fk_mov_garrafa_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
            CONSTRAINT fk_mov_garrafa_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
            CONSTRAINT fk_mov_garrafa_estado_origen FOREIGN KEY (estado_origen_id) REFERENCES estados_garrafa(id),
            CONSTRAINT fk_mov_garrafa_estado_destino FOREIGN KEY (estado_destino_id) REFERENCES estados_garrafa(id),
            KEY idx_mov_garrafa_pedido (pedido_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Issue #145 Slice 4: recepciones_proveedor + recepcion_items.
        -- Necesarios para que RecepcionService.CreateAsync complete su
        -- transacción en el integration test. Réplica mínima de la
        -- migración 20260102_000005 (las columnas generadas y los FKs que
        -- el service toca).
        CREATE TABLE recepciones_proveedor (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            numero VARCHAR(20) NULL,
            fecha DATETIME NOT NULL,
            proveedor_id BIGINT UNSIGNED NOT NULL,
            empleado_id BIGINT UNSIGNED NOT NULL,
            numero_factura_proveedor VARCHAR(50) NULL,
            subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
            descuento DECIMAL(12,2) NOT NULL DEFAULT 0,
            total DECIMAL(12,2) NOT NULL DEFAULT 0,
            monto_pagado DECIMAL(12,2) NOT NULL DEFAULT 0,
            observaciones TEXT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            CONSTRAINT fk_recepciones_proveedor FOREIGN KEY (proveedor_id) REFERENCES proveedores(id),
            CONSTRAINT fk_recepciones_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
            KEY idx_recepciones_proveedor (proveedor_id, fecha)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE recepcion_items (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            recepcion_id BIGINT UNSIGNED NOT NULL,
            producto_id BIGINT UNSIGNED NOT NULL,
            cantidad DECIMAL(10,2) NOT NULL,
            precio_unitario DECIMAL(12,2) NOT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_recepcion_items_recepcion FOREIGN KEY (recepcion_id) REFERENCES recepciones_proveedor(id) ON DELETE CASCADE,
            CONSTRAINT fk_recepcion_items_producto FOREIGN KEY (producto_id) REFERENCES productos(id),
            KEY idx_recepcion_items_recepcion (recepcion_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Catálogo sembrado (idempotente gracias a INSERT IGNORE).
        INSERT IGNORE INTO tipos_producto (codigo, nombre) VALUES ('GAS', 'Gas');
        INSERT IGNORE INTO estados_pedido (codigo, nombre, es_final) VALUES
            ('PENDIENTE', 'Pendiente', FALSE),
            ('CONFIRMADO', 'Confirmado', FALSE),
            ('EN_PREPARACION', 'En preparación', FALSE),
            ('ENTREGADO', 'Entregado', TRUE),
            ('CANCELADO', 'Cancelado', TRUE);
        INSERT IGNORE INTO canales_venta (codigo, nombre) VALUES ('PRESENCIAL', 'Presencial');
        INSERT IGNORE INTO estados_garrafa (codigo, nombre, es_disponible_para_venta, requiere_cliente) VALUES
            ('LLENA_DEPOSITO', 'Llena en depósito', TRUE, FALSE),
            ('VACIA_DEPOSITO', 'Vacía en depósito', FALSE, FALSE),
            ('EN_CLIENTE', 'En cliente', FALSE, TRUE),
            ('EN_TRANSITO', 'En tránsito', FALSE, FALSE),
            ('DAÑADA', 'Dañada', FALSE, FALSE),
            ('FUERA_SERVICIO', 'Fuera de servicio', FALSE, FALSE);
        INSERT IGNORE INTO tipos_movimiento_garrafa (codigo, nombre) VALUES
            ('ENTREGA_CLIENTE', 'Entrega a cliente'),
            ('DEVOLUCION_CLIENTE', 'Devolución de cliente'),
            -- Issue #145 Slice 4: COMPRA es el tipo de movimiento que usa
            -- RecepcionService.LoadCatalogosCompraAsync al registrar una
            -- recepción de proveedor.
            ('COMPRA', 'Compra');

        -- Usuario mínimo para que FKs empleados.usuario_id / productos.created_by
        -- tengan destino si un test los usa. Réplica mínima.
        INSERT IGNORE INTO usuarios (id, username, password_hash, rol_id) VALUES (1, 'system', 'noop', 1);

        -- Trigger crítico: actualiza garrafas.estado_garrafa_id cuando se
        -- inserta un movimiento. Sin este trigger GarrafaService no podría
        -- mover garrafas por canje (solo lo hace vía INSERT en
        -- movimientos_garrafa). Réplica del trigger trg_mov_garrafa_ai de
        -- la migración 20260102_000007.
        DROP TRIGGER IF EXISTS trg_mov_garrafa_ai;
        CREATE TRIGGER trg_mov_garrafa_ai
        AFTER INSERT ON movimientos_garrafa
        FOR EACH ROW
        UPDATE garrafas
        SET fecha_ultimo_movimiento = NEW.fecha,
            estado_garrafa_id = NEW.estado_destino_id
        WHERE id = NEW.garrafa_id;
        """;
}
