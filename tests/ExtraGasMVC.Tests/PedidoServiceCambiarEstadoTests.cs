using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
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
/// Tests directos del path genérico de cambio de estado
/// (<see cref="PedidoService.CambiarEstadoAsync"/>) — el que se ejecuta en
/// TODA transición de un pedido, EXCEPTO el flujo de canje que delega a
/// <see cref="PedidoService.RegistrarCanjePedidoAsync"/>.
///
/// Issue #163: el método no tenía ningún test directo. El path de canje está
/// cubierto por <see cref="PedidoCanjeIntegrationTests"/>, pero las
/// validaciones genéricas (existencia del pedido, no-op sobre mismo estado,
/// catálogo incompleto, estados finales, motivo de cancelación, etc.) eran
/// código de producción sin red de seguridad.
///
/// Patrón: InMemory con dbName = nameof(method) para aislar cada test
/// (réplica de <see cref="PedidoServiceSearchTests"/>). Este path NO toca
/// triggers MySQL — solo lee y escribe las tablas <c>pedidos</c> y
/// <c>estados_pedido</c>, así que InMemory es suficiente y mucho más rápido
/// que Testcontainers.
/// </summary>
public class PedidoServiceCambiarEstadoTests
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
    /// Sembrado mínimo: 1 pedido en el estado indicado y todas las filas de
    /// <c>estados_pedido</c> que los tests referencian por código (PENDIENTE,
    /// CONFIRMADO, EN_PREPARACION, ENTREGADO, CANCELADO). No sembramos el
    /// catálogo completo porque InMemory no exige FKs y los tests solo
    /// consultan los estados que necesitan.
    /// </summary>
    private static ulong Seed(
        ExtraGasDbContext context,
        string estadoActualCodigo,
        DateTime? updatedAt = null)
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

        var estadoActualId = estados.Single(e => e.Codigo == estadoActualCodigo).Id;

        var pedido = new Pedido
        {
            Numero = "PED-TEST-0001",
            Fecha = now,
            ClienteId = 1,
            EmpleadoId = 1,
            CanalVentaId = 1,
            EstadoPedidoId = estadoActualId,
            Subtotal = 0m,
            Descuento = 0m,
            Total = 0m,
            MontoPagado = 0m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Pedidos.Add(pedido);
        context.SaveChanges();
        return pedido.Id;
    }

    // ====================================================================
    // Existencia del pedido
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_PedidoNoExiste_DevuelveFalse()
    {
        var (service, _) = NewService(nameof(CambiarEstadoAsync_PedidoNoExiste_DevuelveFalse));

        var ok = await service.CambiarEstadoAsync(
            id: 9999,
            nuevoEstadoId: 1,
            motivoCancelacion: null,
            usuarioId: 1);

        ok.Should().BeFalse();
    }

    // ====================================================================
    // No-op sobre mismo estado
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_MismoEstadoActual_NoOpSinCambiosDevuelveTrue()
    {
        var (service, context) = NewService(nameof(CambiarEstadoAsync_MismoEstadoActual_NoOpSinCambiosDevuelveTrue));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pedidoId = Seed(context, PedidoEstados.Pendiente, updatedAt: now);

        var ok = await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: 1, // mismo id que PENDIENTE
            motivoCancelacion: null,
            usuarioId: 99);

        ok.Should().BeTrue();

        // El no-op no debe persistir nada: UpdatedAt y UpdatedBy quedan como
        // estaban. Esto valida que el short-circuit es ANTES del SaveChanges.
        var pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.UpdatedAt.Should().Be(now);
        pedido.UpdatedBy.Should().BeNull("no se persiste el usuario en el no-op");
    }

    // ====================================================================
    // Catálogo incompleto
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_EstadoDestinoInexistenteEnCatalogo_LanzaInvalidOperationException()
    {
        var (service, context) = NewService(nameof(CambiarEstadoAsync_EstadoDestinoInexistenteEnCatalogo_LanzaInvalidOperationException));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var act = async () => await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: 9999, // no existe en estados_pedido
            motivoCancelacion: null,
            usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*estado destino no existe*");
    }

    // ====================================================================
    // Estados finales (no permiten transición saliente)
    // ====================================================================

    [Theory]
    [InlineData(PedidoEstados.Entregado)]
    [InlineData(PedidoEstados.Cancelado)]
    public async Task CambiarEstadoAsync_PedidoEnEstadoFinal_LanzaInvalidOperationException(string estadoFinalCodigo)
    {
        var (service, context) = NewService(
            $"CambiarEstadoAsync_PedidoEnEstadoFinal_LanzaInvalidOperationException_{estadoFinalCodigo}");
        var pedidoId = Seed(context, estadoFinalCodigo);

        // Intentar transicionar a PENDIENTE desde un estado final.
        var pendienteId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Pendiente).Id;

        var act = async () => await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: pendienteId,
            motivoCancelacion: "intento de salida",
            usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No se puede cambiar el estado de un pedido en estado final*");
    }

    // ====================================================================
    // Transición no permitida por matriz
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_TransicionNoPermitida_PendienteAEntregado_LanzaInvalidOperationException()
    {
        // PENDIENTE solo puede ir a CONFIRMADO o CANCELADO. Saltar directo a
        // ENTREGADO debe ser rechazado con mensaje claro.
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_TransicionNoPermitida_PendienteAEntregado_LanzaInvalidOperationException));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var entregadoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Entregado).Id;

        var act = async () => await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: entregadoId,
            motivoCancelacion: null,
            usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transición no permitida*");
    }

    // ====================================================================
    // Transición permitida persiste cambios
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_TransicionPermitida_PendienteAConfirmado_PersisteCambios()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_TransicionPermitida_PendienteAConfirmado_PersisteCambios));
        var originalUpdatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pedidoId = Seed(context, PedidoEstados.Pendiente, updatedAt: originalUpdatedAt);

        var confirmadoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Confirmado).Id;

        var ok = await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: confirmadoId,
            motivoCancelacion: null,
            usuarioId: 42);

        ok.Should().BeTrue();

        var pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(confirmadoId);
        pedido.UpdatedBy.Should().Be(42);
        pedido.UpdatedAt.Should().BeAfter(originalUpdatedAt,
            "el service debe actualizar el timestamp de modificación");
        // No se setea motivo de cancelación en transiciones no-CANCELADO.
        pedido.MotivoCancelacion.Should().BeNull();
    }

    // ====================================================================
    // Cancelado requiere motivo
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_DestinoCanceladoSinMotivo_LanzaInvalidOperationException()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_DestinoCanceladoSinMotivo_LanzaInvalidOperationException));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var canceladoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        var act = async () => await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: canceladoId,
            motivoCancelacion: null,
            usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Debe ingresar un motivo de cancelación.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CambiarEstadoAsync_DestinoCanceladoConMotivoVacioOSoloEspacios_LanzaInvalidOperationException(string motivo)
    {
        var (service, context) = NewService(
            $"CambiarEstadoAsync_DestinoCanceladoConMotivoVacioOSoloEspacios_LanzaInvalidOperationException_{motivo.Length}");
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var canceladoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        var act = async () => await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: canceladoId,
            motivoCancelacion: motivo,
            usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Debe ingresar un motivo de cancelación.");
    }

    [Fact]
    public async Task CambiarEstadoAsync_DestinoCanceladoConMotivo_PersisteMotivoTrimmed()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_DestinoCanceladoConMotivo_PersisteMotivoTrimmed));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var canceladoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        const string motivoConEspacios = "   Cliente canceló por lluvia   ";
        const string motivoEsperado = "Cliente canceló por lluvia";

        var ok = await service.CambiarEstadoAsync(
            id: pedidoId,
            nuevoEstadoId: canceladoId,
            motivoCancelacion: motivoConEspacios,
            usuarioId: 7);

        ok.Should().BeTrue();

        var pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(canceladoId);
        pedido.MotivoCancelacion.Should().Be(motivoEsperado,
            "el service debe persistir el motivo sin espacios al borde");
        pedido.UpdatedBy.Should().Be(7);
    }

    // ====================================================================
    // Flujo completo
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_FlujoCompleto_PendienteConfirmadoPendienteConfirmadoCancelado_ConfirmaCamino()
    {
        // Cubre el flujo: PENDIENTE → CONFIRMADO → PENDIENTE → CONFIRMADO →
        // CANCELADO. Verifica que cada paso persiste correctamente y que un
        // pedido que rebota entre estados termina en CANCELADO con su motivo.
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_FlujoCompleto_PendienteConfirmadoPendienteConfirmadoCancelado_ConfirmaCamino));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var pendienteId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Pendiente).Id;
        var confirmadoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Confirmado).Id;
        var canceladoId = context.EstadosPedido
            .Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        // Paso 1: PENDIENTE → CONFIRMADO.
        (await service.CambiarEstadoAsync(pedidoId, confirmadoId, null, usuarioId: 1))
            .Should().BeTrue();
        var pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(confirmadoId);

        // Paso 2: CONFIRMADO → PENDIENTE (transición válida, rebote).
        (await service.CambiarEstadoAsync(pedidoId, pendienteId, null, usuarioId: 2))
            .Should().BeTrue();
        pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(pendienteId);
        pedido.UpdatedBy.Should().Be(2);

        // Paso 3: PENDIENTE → CONFIRMADO (segunda confirmación).
        (await service.CambiarEstadoAsync(pedidoId, confirmadoId, null, usuarioId: 3))
            .Should().BeTrue();
        pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(confirmadoId);

        // Paso 4: CONFIRMADO → CANCELADO con motivo.
        (await service.CambiarEstadoAsync(pedidoId, canceladoId, "Cliente se arrepintió", usuarioId: 4))
            .Should().BeTrue();
        pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(canceladoId);
        pedido.MotivoCancelacion.Should().Be("Cliente se arrepintió");
        pedido.UpdatedBy.Should().Be(4);
    }

    // ====================================================================
    // Matriz completa de transiciones (parametrizada)
    // ====================================================================

    [Theory]
    [InlineData(PedidoEstados.Pendiente, PedidoEstados.Confirmado)]
    [InlineData(PedidoEstados.Pendiente, PedidoEstados.Cancelado)]
    [InlineData(PedidoEstados.Confirmado, PedidoEstados.Pendiente)]
    [InlineData(PedidoEstados.Confirmado, PedidoEstados.EnPreparacion)]
    [InlineData(PedidoEstados.Confirmado, PedidoEstados.Cancelado)]
    [InlineData(PedidoEstados.EnPreparacion, PedidoEstados.Confirmado)]
    [InlineData(PedidoEstados.EnPreparacion, PedidoEstados.Entregado)]
    [InlineData(PedidoEstados.EnPreparacion, PedidoEstados.Cancelado)]
    public async Task CambiarEstadoAsync_TransicionesValidas_NoLanzaYActualizaEstado(string origen, string destino)
    {
        // Cubre la matriz completa de TransicionesValidasPorCodigo. Para
        // destinos CANCELADO pasamos un motivo no vacío.
        var (service, context) = NewService(
            $"CambiarEstadoAsync_TransicionesValidas_NoLanzaYActualizaEstado_{origen}_{destino}");
        var pedidoId = Seed(context, origen);

        var destinoId = context.EstadosPedido.Single(e => e.Codigo == destino).Id;
        var motivo = destino == PedidoEstados.Cancelado ? "motivo válido" : null;

        var ok = await service.CambiarEstadoAsync(pedidoId, destinoId, motivo, usuarioId: 1);

        ok.Should().BeTrue();
        var pedido = await context.Pedidos.FindAsync((object)pedidoId);
        pedido!.EstadoPedidoId.Should().Be(destinoId);
    }

    // ====================================================================
    // Fake IGarrafaService (PedidoService lo exige en el constructor pero
    // CambiarEstadoAsync no lo usa). Réplica de
    // PedidoServiceSearchTests.NotImplementedGarrafaService.
    // ====================================================================

    // ====================================================================
    // Audit: pedido_estados_historico (issue #165)
    //
    // El helper privado RegistrarCambioEstadoAsync NO se invoca desde afuera
    // (es private). Estos tests verifican su efecto observable: el helper
    // debe crear exactamente una fila de historial por transición efectiva,
    // con estado_anterior_id reflejando el estado previo y motivo solo
    // cuando destino == CANCELADO.
    //
    // Patrón: InMemory provee PedidoEstadosHistorico porque el DbSet se
    // aplica vía ApplyConfigurationsFromAssembly — InMemory no exige FKs.
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_TransicionValida_PersisteFilaEnHistorico()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_TransicionValida_PersisteFilaEnHistorico));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var pendienteId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Pendiente).Id;
        var confirmadoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Confirmado).Id;

        await service.CambiarEstadoAsync(pedidoId, confirmadoId, motivoCancelacion: null, usuarioId: 7);

        var rows = await context.PedidoEstadosHistorico
            .Where(h => h.PedidoId == pedidoId)
            .ToListAsync();

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.EstadoAnteriorId.Should().Be(pendienteId,
            "el helper debe capturar el estado previo ANTES de pisar el entity");
        row.EstadoNuevoId.Should().Be(confirmadoId);
        row.UsuarioId.Should().Be(7);
        row.MotivoCancelacion.Should().BeNull(
            "en transiciones cuyo destino no es CANCELADO, el motivo queda null");
    }

    [Fact]
    public async Task CambiarEstadoAsync_DestinoCanceladoConMotivo_PersisteMotivoEnHistorico()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_DestinoCanceladoConMotivo_PersisteMotivoEnHistorico));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var canceladoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        await service.CambiarEstadoAsync(
            pedidoId, canceladoId, motivoCancelacion: "Cliente canceló por lluvia", usuarioId: 3);

        var row = await context.PedidoEstadosHistorico
            .SingleAsync(h => h.PedidoId == pedidoId);

        row.MotivoCancelacion.Should().Be("Cliente canceló por lluvia",
            "el motivo se persiste igual que en pedidos.motivo_cancelacion");
        row.UsuarioId.Should().Be(3);
    }

    [Fact]
    public async Task CambiarEstadoAsync_MismoEstado_NoCreaFilaEnHistorico()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_MismoEstado_NoCreaFilaEnHistorico));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);
        var pendienteId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Pendiente).Id;

        var ok = await service.CambiarEstadoAsync(
            pedidoId, pendienteId, motivoCancelacion: null, usuarioId: 99);

        ok.Should().BeTrue();
        var rows = await context.PedidoEstadosHistorico
            .Where(h => h.PedidoId == pedidoId)
            .ToListAsync();
        rows.Should().BeEmpty(
            "el no-op (mismo estado actual que destino) no debe persistir fila de historial");
    }

    [Fact]
    public async Task CambiarEstadoAsync_PedidoNoExiste_NoCreaFilaEnHistorico()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_PedidoNoExiste_NoCreaFilaEnHistorico));

        var ok = await service.CambiarEstadoAsync(
            id: 9999, nuevoEstadoId: 1, motivoCancelacion: null, usuarioId: 1);

        ok.Should().BeFalse();
        var rows = await context.PedidoEstadosHistorico.ToListAsync();
        rows.Should().BeEmpty(
            "si el pedido no existe, no debe persistirse nada — ni el pedido ni el historial");
    }

    [Fact]
    public async Task CambiarEstadoAsync_TransicionNoPermitida_NoCreaFilaEnHistorico()
    {
        // Defensa en profundidad: si la validación de la matriz de
        // transiciones lanza, el helper no debe haber sido invocado (ni
        // siquiera en el DbSet tracker, porque SaveChanges rollbackea).
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_TransicionNoPermitida_NoCreaFilaEnHistorico));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);
        var entregadoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Entregado).Id;

        var act = async () => await service.CambiarEstadoAsync(
            pedidoId, entregadoId, motivoCancelacion: null, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transición no permitida*");

        var rows = await context.PedidoEstadosHistorico.ToListAsync();
        rows.Should().BeEmpty("una transición rechazada no debe dejar fila de historial");
    }

    [Fact]
    public async Task CambiarEstadoAsync_FlujoCompleto_CreaUnaFilaPorTransicion()
    {
        // Cubre el contrato central del helper: una fila por transición,
        // no más, no menos. El estado_anterior de cada fila debe ser el
        // estado_nuevo de la anterior — invariante crítico para reconstruir
        // la timeline sin huecos.
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_FlujoCompleto_CreaUnaFilaPorTransicion));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);

        var pendienteId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Pendiente).Id;
        var confirmadoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Confirmado).Id;
        var enPreparacionId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.EnPreparacion).Id;
        var canceladoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        // PENDIENTE → CONFIRMADO
        await service.CambiarEstadoAsync(pedidoId, confirmadoId, null, usuarioId: 1);
        // CONFIRMADO → EN_PREPARACION
        await service.CambiarEstadoAsync(pedidoId, enPreparacionId, null, usuarioId: 2);
        // EN_PREPARACION → CANCELADO con motivo
        await service.CambiarEstadoAsync(pedidoId, canceladoId, "Lluvia", usuarioId: 3);

        var rows = await context.PedidoEstadosHistorico
            .Where(h => h.PedidoId == pedidoId)
            .OrderBy(h => h.Id)
            .ToListAsync();

        rows.Should().HaveCount(3, "tres transiciones efectivas = tres filas");

        rows[0].EstadoAnteriorId.Should().Be(pendienteId);
        rows[0].EstadoNuevoId.Should().Be(confirmadoId);
        rows[0].UsuarioId.Should().Be(1);
        rows[0].MotivoCancelacion.Should().BeNull();

        rows[1].EstadoAnteriorId.Should().Be(confirmadoId);
        rows[1].EstadoNuevoId.Should().Be(enPreparacionId);
        rows[1].UsuarioId.Should().Be(2);
        rows[1].MotivoCancelacion.Should().BeNull();

        rows[2].EstadoAnteriorId.Should().Be(enPreparacionId);
        rows[2].EstadoNuevoId.Should().Be(canceladoId);
        rows[2].UsuarioId.Should().Be(3);
        rows[2].MotivoCancelacion.Should().Be("Lluvia");
    }

    [Fact]
    public async Task CambiarEstadoAsync_DestinoCanceladoSinMotivo_NoCreaFilaEnHistorico()
    {
        // Si la validación de "CANCELADO requiere motivo" lanza, el helper
        // no debe haberse invocado. Misma defensa que transición no
        // permitida pero específica del path de cancelación.
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_DestinoCanceladoSinMotivo_NoCreaFilaEnHistorico));
        var pedidoId = Seed(context, PedidoEstados.Pendiente);
        var canceladoId = context.EstadosPedido.Single(e => e.Codigo == PedidoEstados.Cancelado).Id;

        var act = async () => await service.CambiarEstadoAsync(
            pedidoId, canceladoId, motivoCancelacion: null, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Debe ingresar un motivo de cancelación.");

        var rows = await context.PedidoEstadosHistorico.ToListAsync();
        rows.Should().BeEmpty();
    }

    // ====================================================================
    // Fin sección Audit (issue #165)
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
