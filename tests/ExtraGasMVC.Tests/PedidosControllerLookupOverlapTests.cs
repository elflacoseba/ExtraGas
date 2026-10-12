using ExtraGasMVC.Constants;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresion del fan-out paralelo que vivia en
/// <c>BuildCreateViewModelAsync</c> y <c>BuildEditViewModelAsync</c>.
///
/// Esos metodos arrancaban 4 (Create) y 7 (Edit) lookups con un
/// <c>Task.WhenAll</c>. Todos esos services son <c>Scoped</c> y comparten la
/// MISMA instancia de <c>ExtraGasDbContext</c>, asi que EF Core tiraba:
/// "A second operation was started on this context instance before a previous
/// operation completed" y el GET /Pedidos/Create moria con un 500.
///
/// Los fakes de abajo miden el maximo de llamadas EN VOL simultaneas: el
/// Controller correcto (secuencial) nunca supera 1; el patron con
/// Task.WhenAll las solapa todas y supera 1 de forma determinista.
/// No hace falta MySQL ni Docker para reproducir el defecto.
/// </summary>
public class PedidosControllerLookupOverlapTests
{
    /// <summary>
    /// Espera real dentro de cada lookup simulado. Sin esto los fakes
    /// completarian de forma sincrona y nunca se solaparian, y el test
    /// pasaria aunque el Controller volviera al Task.WhenAll.
    /// </summary>
    private const int LookupDelayMs = 5;

    [Fact]
    public async Task Create_NoSolapaLosLookups_QueCompartenElDbContextScoped()
    {
        var probe = new ConcurrencyProbe();
        var controller = NewController(probe);

        var result = await controller.Create(ct: default);

        probe.MaxInFlight.Should().Be(1,
            "los services Scoped comparten una sola instancia de DbContext: " +
            "EF Core no permite dos operaciones concurrentes sobre ella");

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var vm = view.Model.Should().BeOfType<PedidoCreateViewModel>().Subject;
        vm.Clientes.Should().HaveCount(1);
        vm.Empleados.Should().HaveCount(1);
        vm.Canales.Should().HaveCount(1);
        vm.MediosContacto.Should().HaveCount(1);
    }

    [Fact]
    public async Task Edit_NoSolapaLosLookups_QueCompartenElDbContextScoped()
    {
        var probe = new ConcurrencyProbe();
        var controller = NewController(probe);

        var result = await controller.Edit(42, ct: default);

        probe.MaxInFlight.Should().Be(1,
            "Edit compartia el mismo antipatron que Create: 7 lookups en paralelo " +
            "contra un unico DbContext scoped");

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var vm = view.Model.Should().BeOfType<PedidoEditViewModel>().Subject;
        vm.Pedido.Id.Should().Be(42);
        vm.Transiciones.Should().HaveCount(1);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static PedidosController NewController(ConcurrencyProbe probe)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(new InMemoryTempDataProvider())
            .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();

        return new PedidosController(
            new ProbedPedidoService(probe),
            new ProbedClienteService(probe),
            new ProbedProductoService(probe),
            new NotImplementedGarrafaServiceForPedidos())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
                RouteData = new RouteData(),
            },
        };
    }

    /// <summary>
    /// Cuenta cuantas llamadas de lookup estan en vuelo a la vez. Si dos
    /// llamadas coexisten, el Controller las solapo — que es exactamente lo
    /// que el ConcurrencyDetector de EF Core no tolera sobre un DbContext.
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        private int _inFlight;

        public int MaxInFlight { get; private set; }

        public async Task<T> TrackAsync<T>(T value, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _inFlight);
            if (current > MaxInFlight)
            {
                MaxInFlight = current;
            }

            try
            {
                await Task.Delay(LookupDelayMs, ct);
                return value;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class ProbedPedidoService : IPedidoService
    {
        private readonly ConcurrencyProbe _probe;

        public ProbedPedidoService(ConcurrencyProbe probe) => _probe = probe;

        public Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => _probe.TrackAsync<PedidoDto?>(new PedidoDto
            {
                Id = id,
                Fecha = new DateTime(2026, 10, 1),
                EstadoCodigo = PedidoEstados.Pendiente,
                EstadoNombre = "Pendiente",
                Subtotal = 1000m,
                Total = 1000m,
            }, ct);

        public Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default)
            => _probe.TrackAsync<IEnumerable<EmpleadoDto>>(new List<EmpleadoDto> { new() { Id = 1 } }, ct);

        public Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default)
            => _probe.TrackAsync(new List<CanalVentaDto> { new() { Id = 1 } }, ct);

        public Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default)
            => _probe.TrackAsync(new List<MedioContactoPedidoDto> { new() { Id = 1 } }, ct);

        public Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default)
            => _probe.TrackAsync(new List<EstadoPedidoDto> { new() { Id = 2 } }, ct);

        public Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetPendientesAsync(int pagina = 1, int tamanio = 25, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoEstadoHistoricoDto>> GetHistorialEstadosAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RegistrarCanjePedidoAsync(ulong pedidoId, Dictionary<ulong, List<string>> codigosPorItem, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class ProbedClienteService : IClienteService
    {
        private readonly ConcurrencyProbe _probe;

        public ProbedClienteService(ConcurrencyProbe probe) => _probe = probe;

        public Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default)
            => _probe.TrackAsync<IEnumerable<ClienteDto>>(new List<ClienteDto> { new() { Id = 7 } }, ct);

        public Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? restoredBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> GetDeletedAsync(string? busqueda, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class ProbedProductoService : IProductoService
    {
        private readonly ConcurrencyProbe _probe;

        public ProbedProductoService(ConcurrencyProbe probe) => _probe = probe;

        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default)
            => _probe.TrackAsync<IEnumerable<ProductoDto>>(new List<ProductoDto> { new() { Id = 3 } }, ct);

        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? restoredBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NotImplementedGarrafaServiceForPedidos : IGarrafaService
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
        public Task<bool> CambiarEstadoAsync(ulong id, ulong estadoOrigenEsperadoId, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RegistrarMovimientoPorCanjeAsync(ulong garrafaId, ulong estadoDestinoId, ulong? clienteId, ulong pedidoId, string tipoMovimientoCodigo, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<VGarrafaEnCliente>> GetEnClientesAsync(ulong? clienteId = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ulong> GetEstadoIdByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public Dictionary<string, object?> Store { get; } = new();
        public IDictionary<string, object?> LoadTempData(HttpContext context) => Store;
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            Store.Clear();
            foreach (var kv in values) Store[kv.Key] = kv.Value;
        }
    }
}