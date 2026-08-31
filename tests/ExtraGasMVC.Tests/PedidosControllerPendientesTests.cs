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
/// Tests de regresión del <see cref="PedidosController.Pendientes"/> después
/// del refactor de paginación de issue #166. Cubren:
/// <list type="bullet">
///   <item>Propagación de <c>pagina</c>/<c>tamanio</c> del query string al service.</item>
///   <item>ViewBag mínima (sin filtros como el Index — Pendientes no filtra).</item>
///   <item>Compatibilidad con la firma anterior: callers que llamaban sin
///         args siguen funcionando vía defaults.</item>
/// </list>
/// La normalización defensiva (<c>pagina &lt; 1 → 1</c>, <c>tamanio &lt; 1 → 25</c>,
/// <c>tamanio &gt; 100 → 100</c>) vive en el <c>PedidoService</c>, no en el
/// Controller — los tests de normalización los cubre el propio Service
/// (no son foco de estos tests de Controller).
/// </summary>
public class PedidosControllerPendientesTests
{
    [Fact]
    public async Task Pendientes_Defaults_Pagina1Tamanio25()
    {
        // Sin query string → el Controller debe llamar con los defaults
        // (pagina=1, tamanio=25). Mismo criterio que el Index action.
        var fake = new CapturingPendientesPedidoService();
        var controller = NewController(fake);

        await controller.Pendientes(ct: default);

        fake.LastPagina.Should().Be(1);
        fake.LastTamanio.Should().Be(25);
    }

    [Fact]
    public async Task Pendientes_PasaLosQueryParamsAlService()
    {
        // El operador pide explícitamente la página 3 de a 50 — el Controller
        // propaga sin transformación.
        var fake = new CapturingPendientesPedidoService();
        var controller = NewController(fake);

        await controller.Pendientes(pagina: 3, tamanio: 50, ct: default);

        fake.LastPagina.Should().Be(3);
        fake.LastTamanio.Should().Be(50);
    }

    [Fact]
    public async Task Pendientes_DevuelveViewResultConElPagedResultDelService()
    {
        var fake = new CapturingPendientesPedidoService
        {
            ResultadoDevuelto = new PagedResult<PedidoDto>
            {
                Items = new List<PedidoDto>
                {
                    new() { Id = 1, Numero = "PED-1", Saldo = 100m },
                    new() { Id = 2, Numero = "PED-2", Saldo = 250m }
                },
                Total = 2,
                Page = 1,
                PageSize = 25
            }
        };
        var controller = NewController(fake);

        var result = await controller.Pendientes(pagina: 1, tamanio: 25, ct: default);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(fake.ResultadoDevuelto);
    }

    [Fact]
    public async Task Pendientes_NoSeteaViewBag_FiltrosDelIndexNoAplican()
    {
        // Pendientes no expone filtros (el Index sí: numero/estadoId/desde/hasta).
        // Aseguramos que no se cuele ViewBag accidental — la vista de Pendientes
        // no los lee. Casteamos explícito como en PedidosControllerIndexTests
        // porque el dynamic binder explota si la key no existe.
        var fake = new CapturingPendientesPedidoService();
        var controller = NewController(fake);

        await controller.Pendientes(pagina: 2, tamanio: 25, ct: default);

        ((string?)controller.ViewBag.Numero).Should().BeNull();
        ((ulong?)controller.ViewBag.EstadoId).Should().BeNull();
        ((DateTime?)controller.ViewBag.Desde).Should().BeNull();
        ((DateTime?)controller.ViewBag.Hasta).Should().BeNull();
    }

    [Fact]
    public async Task Pendientes_NoLlamadaAsincronaColgada_PropagaCancellationToken()
    {
        // El Controller reenvía el CT al service — un cambio a
        // (pagina, tamanio, ct) accidental sin CT rompería la cancelación.
        var fake = new CapturingPendientesPedidoService();
        var controller = NewController(fake);

        using var cts = new CancellationTokenSource();
        await controller.Pendientes(pagina: 1, tamanio: 25, ct: cts.Token);

        fake.LastCt.Should().Be(cts.Token);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static PedidosController NewController(IPedidoService service)
    {
        var provider = new InMemoryTempDataProvider();
        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(provider)
            .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        var controller = new PedidosController(
            service,
            new NotImplementedClienteService(),
            new NotImplementedProductoService(),
            new NotImplementedGarrafaServiceForPendientes())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };
        return controller;
    }

    /// <summary>
    /// Fake que captura <c>pagina</c>/<c>tamanio</c>/<c>ct</c> que recibe
    /// <see cref="IPedidoService.GetPendientesAsync"/> y devuelve un
    /// <see cref="PagedResult{PedidoDto}"/> configurable por test.
    /// </summary>
    private sealed class CapturingPendientesPedidoService : IPedidoService
    {
        public int? LastPagina { get; private set; }
        public int? LastTamanio { get; private set; }
        public CancellationToken LastCt { get; private set; }
        public PagedResult<PedidoDto> ResultadoDevuelto { get; set; } =
            new PagedResult<PedidoDto> { Items = new List<PedidoDto>(), Total = 0, Page = 1, PageSize = 25 };

        public Task<PagedResult<PedidoDto>> GetPendientesAsync(int pagina = 1, int tamanio = 25, CancellationToken ct = default)
        {
            LastPagina = pagina;
            LastTamanio = tamanio;
            LastCt = ct;
            return Task.FromResult(ResultadoDevuelto);
        }

        // Metodos no usados por Pendientes — NotImplementedException para
        // que un wiring accidental haga ruido en lugar de fallar silenciosamente.
        public Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoEstadoHistoricoDto>> GetHistorialEstadosAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RegistrarCanjePedidoAsync(ulong pedidoId, Dictionary<ulong, List<string>> codigosPorItem, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// Fakes para los otros services que el Controller pide en el constructor
    /// pero Pendientes no usa. Mismo patrón que PedidosControllerIndexTests.
    /// </summary>
    private sealed class NotImplementedClienteService : IClienteService
    {
        public Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> GetDeletedAsync(string? busqueda, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NotImplementedProductoService : IProductoService
    {
        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NotImplementedGarrafaServiceForPendientes : IGarrafaService
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
