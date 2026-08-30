using AutoMapper;
using ExtraGasMVC.Constants;
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
/// Tests de los handlers POST de PedidosController que cambiaron en PR #137:
/// CambiarEstado (TempData[TempDataKeys.Error]), AddItem (Success/Error),
/// RemoveItem (Success/Error), DeleteConfirmed (Success/Error + PedidoNotFoundMessage).
///
/// Sin estos handlers en cobertura, el \`new_coverage\` del PR no refleja el
/// flujo real. Los tests usan el mismo FakePedidoService "configurable por
/// escenario" que ClientesControllerEditNotFoundTests.
/// </summary>
public class PedidosControllerCommandTests
{
    [Fact]
    public async Task CambiarEstado_InvalidOperationException_EscribeTempDataErrorConElMensaje()
    {
        // Service lanza InvalidOperationException (catalogo faltante, codigo
        // duplicado, etc., ver issue #44). El Controller lo mapea a
        // TempData[TempDataKeys.Error] con el mensaje de la excepcion.
        var fake = new ConfigurablePedidoService
        {
            CambiarEstadoScenario = ConfigurablePedidoService.Scenario.ThrowsInvalidOp,
            InvalidOpMessage = "El pedido ya se encuentra en estado CONFIRMADO.",
        };
        var controller = NewController(fake);

        var result = await controller.CambiarEstado(
            id: 1, nuevoEstadoId: 2, motivoCancelacion: null,
            codigosGarrafaJson: null, ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(PedidosController.Edit));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey(TempDataKeys.Error);
        tempData[TempDataKeys.Error].Should().Be("El pedido ya se encuentra en estado CONFIRMADO.");
    }

    [Fact]
    public async Task CambiarEstado_ExcepcionGenerica_EscribeTempDataErrorGenerico()
    {
        // Cualquier otra excepcion cae al catch generico con mensaje templado.
        var fake = new ConfigurablePedidoService
        {
            CambiarEstadoScenario = ConfigurablePedidoService.Scenario.ThrowsGeneric,
        };
        var controller = NewController(fake);

        var result = await controller.CambiarEstado(
            id: 1, nuevoEstadoId: 2, motivoCancelacion: null,
            codigosGarrafaJson: null, ct: default);

        result.Should().BeOfType<RedirectToActionResult>();

        var tempData = SaveAndReadTempData(controller);
        tempData[TempDataKeys.Error].Should().Be(
            "Ocurrió un error al cambiar el estado del pedido. Intente nuevamente.");
    }

    [Fact]
    public async Task DeleteConfirmed_TempDataSuccess_CuandoElServiceDevuelveTrue()
    {
        var fake = new ConfigurablePedidoService
        {
            DeleteScenario = ConfigurablePedidoService.Scenario.ReturnsTrue,
        };
        var controller = NewController(fake);

        var result = await controller.DeleteConfirmed(id: 1, ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(PedidosController.Index));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey(TempDataKeys.Success);
        tempData[TempDataKeys.Success].Should().Be("Pedido eliminado correctamente.");
        tempData.Should().NotContainKey(TempDataKeys.Error);
    }

    [Fact]
    public async Task DeleteConfirmed_TempDataError_UsaMensajeCanónicoCuandoNoEncuentra()
    {
        // PedidoNotFoundMessage del TempDataKeys — PR #137 extrajo este literal
        // a constante. Cubrimos que el Controller lo usa cuando el service
        // devuelve false (no encontro el pedido).
        var fake = new ConfigurablePedidoService
        {
            DeleteScenario = ConfigurablePedidoService.Scenario.ReturnsFalse,
        };
        var controller = NewController(fake);

        var result = await controller.DeleteConfirmed(id: 999999, ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(PedidosController.Index));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey(TempDataKeys.Error);
        tempData[TempDataKeys.Error].Should().Be(TempDataKeys.PedidoNotFoundMessage);
    }

    [Fact]
    public async Task AddItem_Exitoso_TempDataSuccess()
    {
        var fake = new ConfigurablePedidoService();
        var controller = NewController(fake);

        var result = await controller.AddItem(
            new CreatePedidoItemDto { PedidoId = 1, Cantidad = 2 },
            ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(PedidosController.Edit));

        var tempData = SaveAndReadTempData(controller);
        tempData[TempDataKeys.Success].Should().Be("Item agregado correctamente.");
    }

    [Fact]
    public async Task AddItem_Excepcion_TempDataError()
    {
        var fake = new ConfigurablePedidoService
        {
            AddItemScenario = ConfigurablePedidoService.Scenario.ThrowsGeneric,
        };
        var controller = NewController(fake);

        var result = await controller.AddItem(
            new CreatePedidoItemDto { PedidoId = 1, Cantidad = 2 },
            ct: default);

        var tempData = SaveAndReadTempData(controller);
        tempData[TempDataKeys.Error].Should().Be("Item ya esta en el pedido.");
    }

    [Fact]
    public async Task RemoveItem_TempDataSuccess_CuandoBorra()
    {
        var fake = new ConfigurablePedidoService
        {
            RemoveItemScenario = ConfigurablePedidoService.Scenario.ReturnsTrue,
        };
        var controller = NewController(fake);

        var result = await controller.RemoveItem(itemId: 1, pedidoId: 10, ct: default);

        result.Should().BeOfType<RedirectToActionResult>();

        var tempData = SaveAndReadTempData(controller);
        tempData[TempDataKeys.Success].Should().Be("Item eliminado correctamente.");
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
            new NotImplementedGarrafaService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };
        return controller;
    }

    private static Dictionary<string, object?> SaveAndReadTempData(PedidosController controller)
    {
        var context = controller.HttpContext;
        var factory = context.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
        var postTempData = factory.GetTempData(context);
        postTempData.Save();

        var nextContext = new DefaultHttpContext { RequestServices = context.RequestServices };
        nextContext.User = context.User;
        var nextTempData = factory.GetTempData(nextContext);
        return new Dictionary<string, object?>(nextTempData);
    }

    /// <summary>
    /// Fake configurable por escenario. Solo implementa los métodos que los
    /// tests de PR #137 ejercitan; el resto lanza NotImplementedException.
    /// </summary>
    private sealed class ConfigurablePedidoService : IPedidoService
    {
        public enum Scenario
        {
            Success,
            ReturnsTrue,
            ReturnsFalse,
            ThrowsInvalidOp,
            ThrowsGeneric,
        }

        public Scenario CambiarEstadoScenario { get; set; } = Scenario.Success;
        public Scenario DeleteScenario { get; set; } = Scenario.Success;
        public Scenario AddItemScenario { get; set; } = Scenario.Success;
        public Scenario RemoveItemScenario { get; set; } = Scenario.Success;
        public string? InvalidOpMessage { get; set; }

        public Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default)
        {
            return CambiarEstadoScenario switch
            {
                Scenario.ThrowsInvalidOp => throw new InvalidOperationException(InvalidOpMessage ?? "InvalidOp"),
                Scenario.ThrowsGeneric => throw new Exception("boom"),
                Scenario.ReturnsTrue => Task.FromResult(true),
                Scenario.ReturnsFalse => Task.FromResult(false),
                _ => Task.FromResult(true),
            };
        }

        public Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EstadoPedidoDto>
            {
                new() { Id = 1, Codigo = "PENDIENTE", Nombre = "Pendiente" },
                new() { Id = 2, Codigo = "CONFIRMADO", Nombre = "Confirmado" },
            });

        public Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default)
        {
            return DeleteScenario switch
            {
                Scenario.ReturnsTrue => Task.FromResult(true),
                Scenario.ReturnsFalse => Task.FromResult(false),
                _ => Task.FromResult(true),
            };
        }

        public Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default)
        {
            if (AddItemScenario == Scenario.ThrowsGeneric)
                throw new InvalidOperationException("Item ya esta en el pedido.");
            return Task.FromResult(new PedidoItemDto { Id = 1, PedidoId = item.PedidoId, Cantidad = item.Cantidad });
        }

        public Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default)
        {
            return RemoveItemScenario switch
            {
                Scenario.ReturnsTrue => Task.FromResult(true),
                _ => Task.FromResult(false),
            };
        }

        // Metodos no usados por estos tests — NotImplemented para detectar wiring.
        public Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RegistrarCanjePedidoAsync(ulong pedidoId, Dictionary<ulong, List<string>> codigosPorItem, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
    }

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