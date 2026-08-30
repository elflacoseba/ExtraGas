using AutoMapper;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
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
/// Tests de regresion del <see cref="PedidosController.Index"/> con el nuevo
/// <see cref="PedidoSearchFilter"/> introducido en PR #137. Cubre el cuerpo
/// de la construccion del filter (lineas 42-52) y la propagacion de los
/// argumentos del query string al service.
///
/// Antes de #137 el Controller llamaba SearchAsync con 8 argumentos
/// posicionales; ahora construye un record inmutable. Estos tests verifican
/// que la firma sigue funcionando end-to-end (Controller → IPedidoService).
/// </summary>
public class PedidosControllerIndexTests
{
    [Fact]
    public async Task Index_PasaLosParametrosDelQueryStringAlService()
    {
        var fake = new CapturingPedidoService();
        var controller = NewController(fake);

        await controller.Index(
            numero: "PED-2026",
            estadoId: 3,
            desde: new DateTime(2026, 1, 1),
            hasta: new DateTime(2026, 12, 31),
            pagina: 2,
            tamanio: 50,
            ct: default);

        // El filter construido en el Controller debe llegar al service con
        // los mismos valores (estadoId > 0 → se mantiene, no se convierte a null).
        fake.LastFilter.Should().NotBeNull();
        var captured = fake.LastFilter!;
        captured.Numero.Should().Be("PED-2026");
        captured.EstadoId.Should().Be(3);
        captured.ClienteId.Should().BeNull(
            "el Index no filtra por cliente — eso vive en CuentasCorrientes");
        captured.Desde.Should().Be(new DateTime(2026, 1, 1));
        captured.Hasta.Should().Be(new DateTime(2026, 12, 31));
        captured.Pagina.Should().Be(2);
        captured.Tamanio.Should().Be(50);
    }

    [Fact]
    public async Task Index_EstadoIdCero_LoConvierteANullEnElFilter()
    {
        // El dropdown de la vista envia 0 cuando el usuario no filtro nada;
        // el Controller lo limpia a null para que el Service no filtre por
        // un estado inexistente.
        var fake = new CapturingPedidoService();
        var controller = NewController(fake);

        await controller.Index(
            numero: null,
            estadoId: 0,
            desde: null,
            hasta: null,
            pagina: 1,
            tamanio: 25,
            ct: default);

        fake.LastFilter!.EstadoId.Should().BeNull();
        fake.LastFilter!.Numero.Should().BeNull();
        fake.LastFilter!.Pagina.Should().Be(1);
        fake.LastFilter!.Tamanio.Should().Be(25);
    }

    [Fact]
    public async Task Index_SeteaViewBagConLosValoresOriginales()
    {
        var fake = new CapturingPedidoService();
        var controller = NewController(fake);

        await controller.Index(
            numero: "PED-X",
            estadoId: 1,
            desde: new DateTime(2026, 6, 1),
            hasta: new DateTime(2026, 6, 30),
            pagina: 3,
            tamanio: 10,
            ct: default);

        // ViewBag es dynamic — casteamos para evitar RuntimeBinderException.
        ((string?)controller.ViewBag.Numero).Should().Be("PED-X");
        ((ulong?)controller.ViewBag.EstadoId).Should().Be(1);
        ((DateTime?)controller.ViewBag.Desde).Should().Be(new DateTime(2026, 6, 1));
        ((DateTime?)controller.ViewBag.Hasta).Should().Be(new DateTime(2026, 6, 30));
        ((List<EstadoPedidoDto>?)controller.ViewBag.Estados).Should().BeSameAs(fake.EstadosDevueltos);
    }

    [Fact]
    public async Task Index_DevuelveViewResultConElPagedResultDelService()
    {
        var fake = new CapturingPedidoService();
        var controller = NewController(fake);

        var result = await controller.Index(
            numero: null, estadoId: null, desde: null, hasta: null,
            pagina: 1, tamanio: 25, ct: default);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(fake.SearchResultDevuelto);
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
            new NotImplementedGarrafaServiceForPedidos())
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
    /// Fake que captura el <see cref="PedidoSearchFilter"/> que recibe el
    /// service para verificar la traduccion Controller → record.
    /// </summary>
    private sealed class CapturingPedidoService : IPedidoService
    {
        public PedidoSearchFilter? LastFilter { get; private set; }
        public PagedResult<PedidoDto> SearchResultDevuelto { get; } =
            new PagedResult<PedidoDto> { Items = new List<PedidoDto>(), Total = 0, Page = 1, PageSize = 25 };
        public List<EstadoPedidoDto> EstadosDevueltos { get; } = new();

        public Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default)
        {
            LastFilter = filter;
            return Task.FromResult(SearchResultDevuelto);
        }

        public Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default)
            => Task.FromResult(EstadosDevueltos);

        // Metodos no usados por Index — NotImplementedException para que
        // un wiring accidental haga ruido en lugar de fallar silenciosamente.
        public Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RegistrarCanjePedidoAsync(ulong pedidoId, Dictionary<ulong, List<string>> codigosPorItem, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// Fakes para los otros services que el Controller pide en el constructor
    /// pero Index no usa. Mismo patron que PedidoServiceSearchTests.
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
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
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
