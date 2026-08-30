using AutoMapper;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Exceptions;
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
/// Tests de regresion del flujo POST Edit del ClientesController.
///
/// Issue #108: cuando el cliente no existe o esta soft-deleted, el Service
/// lanza una excepcion especifica (KeyNotFoundException /
/// ClienteSoftDeletedException). Antes, esos casos caian en el catch
/// generico de Exception y devolvian 500. Ahora el Controller los mapea
/// a TempData["Error"] + redirect a Index.
///
/// Patrón: DefaultHttpContext + ClaimsPrincipal (igual que
/// ChangePasswordTempDataFlowTests) + FakeClienteService que se configura
/// por test para devolver success o tirar la excepcion esperada.
/// </summary>
public class ClientesControllerEditNotFoundTests
{
    private const string TestUserId = "1";

    // ---------- Rama 1: Update exitoso -> redirect con Success ----------

    [Fact]
    public async Task Edit_POST_UpdateExitoso_RedirigeAIndexConSuccess()
    {
        var fake = new FakeClienteService
        {
            UpdateScenario = FakeClienteService.Scenario.Success,
        };
        var (controller, _, _, _) = NewControllerAndEnv(fake);

        var result = await controller.Edit(
            id: 10,
            cliente: NewUpdateDto(10),
            ct: default);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(ClientesController.Index));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey("Success");
        tempData["Success"].Should().BeOfType<string>()
            .Which.Should().Contain("actualizado");
        tempData.Should().NotContainKey("Error",
            "en el happy path no debe haber mensaje de error");
    }

    // ---------- Rama 2: Cliente no existe -> KeyNotFoundException ----------

    [Fact]
    public async Task Edit_POST_ClienteNoExiste_RedirigeAIndexConError_KeyNotFound()
    {
        var fake = new FakeClienteService
        {
            UpdateScenario = FakeClienteService.Scenario.KeyNotFound,
        };
        var (controller, _, _, _) = NewControllerAndEnv(fake);

        var result = await controller.Edit(
            id: 999999,
            cliente: NewUpdateDto(999999),
            ct: default);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(ClientesController.Index));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey("Error");
        tempData["Error"].Should().BeOfType<string>()
            .Which.Should().Be("Cliente con Id 999999 no encontrado.");
    }

    // ---------- Rama 3: Cliente soft-deleted -> ClienteSoftDeletedException ----------

    [Fact]
    public async Task Edit_POST_ClienteSoftDeleted_RedirigeAIndexConMensajeRestaurar()
    {
        var fake = new FakeClienteService
        {
            UpdateScenario = FakeClienteService.Scenario.SoftDeleted,
        };
        var (controller, _, _, _) = NewControllerAndEnv(fake);

        var result = await controller.Edit(
            id: 42,
            cliente: NewUpdateDto(42),
            ct: default);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(ClientesController.Index));

        var tempData = SaveAndReadTempData(controller);
        tempData.Should().ContainKey("Error");
        tempData["Error"].Should().BeOfType<string>()
            .Which.Should().Be(
                "No se puede editar un cliente eliminado; debe restaurarlo primero.");
    }

    // ---------- Rama 4: DNI duplicado -> InvalidOperationException -> re-render ----------

    [Fact]
    public async Task Edit_POST_DniDuplicado_ReRenderDelFormConModelStateError()
    {
        var fake = new FakeClienteService
        {
            UpdateScenario = FakeClienteService.Scenario.InvalidOperation,
        };
        var (controller, _, _, _) = NewControllerAndEnv(fake);

        var dto = NewUpdateDto(7);
        var result = await controller.Edit(id: 7, cliente: dto, ct: default);

        // DNI duplicado: NO redirige, re-renderiza la vista con error en el
        // campo Dni para que el operador pueda corregir.
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(dto);

        controller.ModelState.Should().ContainKey("Dni");
        controller.ModelState["Dni"]!.Errors
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("El DNI ingresado ya está registrado.");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static UpdateClienteDto NewUpdateDto(ulong id) => new()
    {
        Id = id,
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = "12345678",
        TelefonoPrincipal = "1144556677",
    };

    /// <summary>
    /// Arma el HttpContext con TempDataProvider in-memory + ITempDataDictionaryFactory +
    /// IUrlHelperFactory registrados, igual que ChangePasswordTempDataFlowTests.
    /// Devuelve el controller, el HttpContext original (para SaveTempData), el
    /// factory y el provider.
    /// </summary>
    private static (ClientesController controller,
                    DefaultHttpContext postContext,
                    ITempDataDictionaryFactory factory,
                    InMemoryTempDataProvider provider)
        NewControllerAndEnv(FakeClienteService fake)
    {
        var provider = new InMemoryTempDataProvider();
        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(provider)
            .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier, TestUserId),
                },
                "TestAuth"));

        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();

        var controller = new ClientesController(fake, mapper)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };

        var factory = services.GetRequiredService<ITempDataDictionaryFactory>();
        return (controller, httpContext, factory, provider);
    }

    /// <summary>
    /// Simula lo que hace el filtro SaveTempDataFilter al final del POST:
    /// obtiene el ITempDataDictionary del HttpContext actual, llama a Save()
    /// y luego arma un nuevo HttpContext (un GET hipotetico) que comparte el
    /// provider. Asi se valida que TempData["Error"] sobrevive al redirect.
    /// </summary>
    private static IDictionary<string, object?> SaveAndReadTempData(ClientesController controller)
    {
        var context = controller.HttpContext;
        var factory = context.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
        var postTempData = factory.GetTempData(context);
        postTempData.Save();

        // Simular un nuevo HttpContext (GET siguiente) que comparta provider
        // y factory. Asi verificamos que el TempData["Error"] que escribio el
        // Controller en el POST sigue siendo legible en el GET.
        var nextContext = new DefaultHttpContext
        {
            RequestServices = context.RequestServices,
        };
        nextContext.User = context.User;
        var nextTempData = factory.GetTempData(nextContext);
        // ITempDataDictionary implementa IDictionary<string, object?>
        return nextTempData;
    }

    // ====================================================================
    // Fakes
    // ====================================================================

    /// <summary>
    /// Fake de <see cref="IClienteService"/> que se configura por test segun
    /// el escenario. Solo implementa los metodos ejercitados por estos tests;
    /// el resto lanza <see cref="NotImplementedException"/> (mismo patron que
    /// los fakes de Usuarios) para que un cambio en el wiring del Controller
    /// rompa un test en lugar de fallar silenciosamente.
    /// </summary>
    private sealed class FakeClienteService : IClienteService
    {
        public enum Scenario
        {
            Success,
            KeyNotFound,
            SoftDeleted,
            InvalidOperation,
        }

        public Scenario UpdateScenario { get; set; } = Scenario.Success;

        public Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default)
        {
            return UpdateScenario switch
            {
                Scenario.Success => Task.FromResult(new ClienteDto
                {
                    Id = cliente.Id,
                    Nombre = cliente.Nombre,
                    Apellido = cliente.Apellido,
                    Dni = cliente.Dni,
                    TelefonoPrincipal = cliente.TelefonoPrincipal,
                }),
                Scenario.KeyNotFound =>
                    throw new KeyNotFoundException($"Cliente con Id {cliente.Id} no encontrado."),
                Scenario.SoftDeleted =>
                    throw new ClienteSoftDeletedException(cliente.Id),
                Scenario.InvalidOperation =>
                    throw new InvalidOperationException("El DNI ingresado ya está registrado."),
                _ => throw new InvalidOperationException($"Scenario no soportado: {UpdateScenario}"),
            };
        }

        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default)
            => Task.FromResult(new List<ProvinciaDto>());
        public Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<VSaldoClienteDto>>(new List<VSaldoClienteDto>());

        // Metodos no usados por estos tests:
        public Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto c, ulong? b, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// ITempDataProvider in-memory compartido entre el POST y el GET
    /// hipotetico. Mismo patron que ChangePasswordTempDataFlowTests.
    /// </summary>
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
