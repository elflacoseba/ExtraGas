using System.ComponentModel.DataAnnotations;
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
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Cubre el wiring a nivel Controller de los 3 items del PR #186 que el
/// coverage de SonarQube marco como faltantes:
/// <list type="bullet">
///   <item><b>T12</b> — <c>GarrafasController.Create</c> GET llama al helper
///   <c>GetEstadoIdByCodigoAsync("LLENA_DEPOSITO")</c> y asigna el id al DTO
///   inicial (en lugar del hardcode <c>1</c>).</item>
///   <item><b>T11</b> — <c>GarrafasController.EnClientes</c> lee <c>page</c>
///   y <c>pageSize</c> del query string y los pasa al service, preservando
///   <c>clienteId</c> en ViewBag para los links de paginacion.</item>
///   <item><b>T13</b> — <c>GarrafasController.Create</c> POST rechaza con
///   <c>ModelState.IsValid == false</c> cuando <c>Observaciones</c> excede
///   500 chars, SIN invocar al service.</item>
/// </list>
/// <para>La logica de negocio ya esta cubierta por <see cref="GarrafaServiceTests"/>
/// y los tests del PR #185. Aca solo verificamos que el Controller cablea
/// correctamente las nuevas piezas.</para>
/// </summary>
public class GarrafasControllerPr4CoverageTests
{
    // ====================================================================
    // T12: Create GET — usa el helper por codigo canonico (no hardcode)
    // ====================================================================

    [Fact]
    public async Task Create_GET_LlamaHelperConLlenaDeposito_YAsignaIdAlDtoInicial()
    {
        // Arrange: el helper devuelve 7 (simula un seed que reordeno IDs).
        var fakeGarrafa = new FakeGarrafaService
        {
            GetEstadoIdByCodigoResult = 7UL,
        };
        var controller = NewController(fakeGarrafa);

        // Act
        var result = await controller.Create();

        // Assert: el controller llamo al helper con el codigo canonico
        // (no con "1" hardcodeado).
        fakeGarrafa.GetEstadoIdByCodigoCodigo.Should().Be(
            GarrafaEstados.LlenaDeposito,
            "el estado inicial del alta debe resolverse por codigo canonico");
        fakeGarrafa.GetEstadoIdByCodigoCalls.Should().Be(1);

        // Resultado: ViewResult con DTO sembrado con el id del helper.
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var dto = viewResult.Model.Should().BeOfType<CreateGarrafaDto>().Subject;
        dto.EstadoGarrafaId.Should().Be(7UL,
            "el id inicial debe venir del helper, no del hardcode 1");
        dto.FechaCompra.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow),
            "el alta se siembra con la fecha del dia");
    }

    // ====================================================================
    // T11: EnClientes — pasa page/pageSize al service y preserva clienteId
    // ====================================================================

    [Fact]
    public async Task EnClientes_PasaPageYPageSizeAlService_YPreservaClienteIdEnViewBag()
    {
        // Arrange: el service devuelve un PagedResult con 1 item; el cliente
        // existe para que ViewBag.Cliente se popule.
        var fakeGarrafa = new FakeGarrafaService
        {
            PagedResultEnClientes = new PagedResult<VGarrafaEnCliente>
            {
                Items = new List<VGarrafaEnCliente>(),
                Total = 0,
                Page = 3,
                PageSize = 50,
            },
        };
        var fakeCliente = new FakeClienteService
        {
            GetByIdResult = new ClienteDto { Id = 5, Nombre = "Juan", Apellido = "Perez" },
        };
        var controller = NewController(fakeGarrafa, fakeCliente);

        // Act
        var result = await controller.EnClientes(clienteId: 5, page: 3, pageSize: 50);

        // Assert: el controller paso los params al service SIN normalizar
        // (la normalizacion la hace el service).
        fakeGarrafa.GetEnClientesArgs.Should().Be(((ulong?)5UL, 3, 50));

        // ViewBag expone clienteId para que los links de paginacion lo preserven.
        ((ulong?)controller.ViewBag.ClienteId).Should().Be(5UL);
        var clienteEnBag = (ClienteDto?)controller.ViewBag.Cliente;
        clienteEnBag.Should().NotBeNull();
        clienteEnBag!.Id.Should().Be(5UL);

        result.Should().BeOfType<ViewResult>().Subject
            .ViewName.Should().Be("EnClientes",
                "el controller reusa la vista EnClientes con el PagedResult");
    }

    [Fact]
    public async Task EnClientes_DefaultPageYPageSize_CuandoQueryStringVacio()
    {
        // Arrange
        var fakeGarrafa = new FakeGarrafaService
        {
            PagedResultEnClientes = new PagedResult<VGarrafaEnCliente>
            {
                Items = new List<VGarrafaEnCliente>(),
                Total = 0,
                Page = 1,
                PageSize = 20,
            },
        };
        var controller = NewController(fakeGarrafa);

        // Act: defaults del action method (page=1, pageSize=20)
        var result = await controller.EnClientes(clienteId: null, page: 1, pageSize: 20);

        // Assert: sin clienteId, no se llama GetByIdAsync del cliente y
        // ViewBag.Cliente queda null (solo ViewBag.ClienteId queda null tambien).
        fakeGarrafa.GetEnClientesArgs.Should().Be(((ulong?)null, 1, 20));
        ((ulong?)controller.ViewBag.ClienteId).Should().BeNull();
        ((ClienteDto?)controller.ViewBag.Cliente).Should().BeNull();

        result.Should().BeOfType<ViewResult>();
    }

    // ====================================================================
    // T13: Create POST — rechaza Observaciones > 500 chars sin invocar Service
    // ====================================================================

    [Fact]
    public async Task Create_POST_RechazaObservacionesDe501Chars_NoLlamaService()
    {
        // Arrange: DTO con Observaciones de 501 chars (excede el [StringLength(500)]).
        var fakeGarrafa = new FakeGarrafaService(); // CreateAsync throws si se llama
        var controller = NewController(fakeGarrafa);

        var dto = new CreateGarrafaDto
        {
            Codigo = "GAR-2026-00001",
            CapacidadKg = 10,
            EstadoGarrafaId = 1,
            FechaCompra = new DateOnly(2026, 1, 1),
            ProveedorId = 1,
            Observaciones = new string('a', 501), // 501 chars -> viola StringLength
        };

        // Simulamos el binder del framework: corre la validacion DataAnnotations
        // y popula ModelState (MVC no auto-valida sin [ApiController]).
        var validationContext = new ValidationContext(dto);
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(dto, validationContext, validationResults, validateAllProperties: true);
        foreach (var vr in validationResults)
            foreach (var member in vr.MemberNames)
                controller.ModelState.AddModelError(member, vr.ErrorMessage ?? "");

        // Act
        var result = await controller.Create(dto, CancellationToken.None);

        // Assert: ModelState invalido, el service NO se invoco, y el
        // resultado es View (re-render del form con errores).
        controller.ModelState.IsValid.Should().BeFalse(
            "Observaciones de 501 chars debe violar [StringLength(500)]");
        controller.ModelState.Should().ContainKey(nameof(CreateGarrafaDto.Observaciones));
        fakeGarrafa.CreateAsyncCalls.Should().Be(0,
            "con ModelState invalido el controller debe cortar antes del service");

        result.Should().BeOfType<ViewResult>().Subject
            .Model.Should().BeSameAs(dto,
                "el controller re-renderiza el form con el DTO recibido");
    }

    // ====================================================================
    // Helpers de construccion
    // ====================================================================

    private static GarrafasController NewController(
        FakeGarrafaService garrafaService,
        FakeClienteService? clienteService = null)
    {
        var controller = new GarrafasController(
            NullLogger<GarrafasController>.Instance,
            garrafaService,
            clienteService ?? new FakeClienteService(),
            new FakeProveedorService(),
            new FakeRecepcionService())
        {
            ControllerContext = NewControllerContext(),
        };
        return controller;
    }

    private static ControllerContext NewControllerContext() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") },
                    "TestAuth")),
        },
        RouteData = new RouteData(),
    };

    // ====================================================================
    // Fakes configurables (solo lo necesario para los 3 paths testeados)
    // ====================================================================

    private sealed class FakeGarrafaService : IGarrafaService
    {
        // T12 tracking
        public int GetEstadoIdByCodigoCalls { get; private set; }
        public string? GetEstadoIdByCodigoCodigo { get; private set; }
        public ulong GetEstadoIdByCodigoResult { get; set; }

        // T11 tracking
        public (ulong? clienteId, int page, int pageSize)? GetEnClientesArgs { get; private set; }
        public PagedResult<VGarrafaEnCliente>? PagedResultEnClientes { get; set; }

        // T13 tracking
        public int CreateAsyncCalls { get; private set; }

        public Task<ulong> GetEstadoIdByCodigoAsync(string codigo, CancellationToken ct = default)
        {
            GetEstadoIdByCodigoCalls++;
            GetEstadoIdByCodigoCodigo = codigo;
            return Task.FromResult(GetEstadoIdByCodigoResult);
        }

        public Task<PagedResult<VGarrafaEnCliente>> GetEnClientesAsync(
            ulong? clienteId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            GetEnClientesArgs = (clienteId, page, pageSize);
            return Task.FromResult(PagedResultEnClientes ?? new PagedResult<VGarrafaEnCliente>
            {
                Items = new List<VGarrafaEnCliente>(),
                Total = 0,
                Page = page,
                PageSize = pageSize,
            });
        }

        public Task<GarrafaDto> CreateAsync(CreateGarrafaDto d, ulong? u, CancellationToken ct = default)
        {
            CreateAsyncCalls++;
            // El test verifica que NO se llame; si llega aca, el modelo estaba
            // mal seteado y queremos un error explicito.
            throw new InvalidOperationException(
                "CreateAsync NO debio invocarse: ModelState era invalido o el controller bypaseo la validacion.");
        }

        // Miembros no usados en estos tests — NIE explicito.
        public Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto?> GetByCodigoAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong e, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<GarrafaDto>> GetPagedAsync(string? codigo, byte? capacidad, int page = 1, int pageSize = 20, string sortBy = "codigo", string sortDir = "asc", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<EstadoGarrafaDto>>(new List<EstadoGarrafaDto>());
        public Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, ulong estadoOrigenEsperadoId, CambiarEstadoGarrafaDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong g, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong g, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RegistrarMovimientoPorCanjeAsync(ulong g, ulong ed, ulong? c, ulong p, string t, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeClienteService : IClienteService
    {
        public ClienteDto? GetByIdResult { get; set; }
        public FakeClienteService() { }
        public FakeClienteService(ClienteDto? result) { GetByIdResult = result; }

        public Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(GetByIdResult);
        public Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ClienteDto>>(new List<ClienteDto>());
        public Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ClienteDto>>(new List<ClienteDto>());
        public Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> GetDeletedAsync(string? b, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> UpdateAsync(UpdateClienteDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => Task.FromResult(new List<ProvinciaDto>());
        public Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<VSaldoClienteDto>>(new List<VSaldoClienteDto>());
    }

    private sealed class FakeProveedorService : IProveedorService
    {
        public Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProveedorDto?> GetByCuitAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProveedorDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ProveedorDto>
            {
                Items = new List<ProveedorDto>(),
                Total = 0,
                Page = p,
                PageSize = t,
            });
        public Task<ProveedorDto> CreateAsync(CreateProveedorDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeRecepcionService : IRecepcionService
    {
        public Task<RecepcionDto> CreateAsync(CrearRecepcionDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ReversarAsync(ulong r, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetProductosActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<RecepcionDto>> GetRecientesAsync(int c, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<RecepcionDto>>(new List<RecepcionDto>());
    }
}
