using AutoMapper;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services;
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
/// Tests de los Controllers para verificar que ViewBag.DeletedAt / ViewBag.Activo
/// (y ViewBag.FechaAlta en ClientesController) se popula con los valores del
/// DTO despues del refactor del issue #115.
///
/// <para>Issue #115: en <see cref="ClientesController"/>, la vista Edit GET
/// recibe <c>ViewBag.DeletedAt</c> (no <c>ViewBag.Activo</c>); el badge
/// "Activo/Inactivo" se calcula desde <c>DeletedAt == null</c>. En
/// Empleados/Productos/Garrafas (otras tablas con <c>activo</c> propio) el
/// ViewBag sigue siendo <c>ViewBag.Activo</c>.</para>
///
/// La logica de negocio la cubren los tests del Service y del helper;
/// aca solo se valida el wiring del Controller.
/// </summary>
public class ControllersActivoViewBagTests
{
    [Fact]
    public async Task ClientesController_EditGet_PopulaViewBagDeletedAtYFechaAlta()
    {
        // Issue #115: ClienteDto.Activo es getter-only derivado de
        // DeletedAt. Solo se setea DeletedAt en el DTO; el controller
        // propaga ViewBag.DeletedAt y ViewBag.FechaAlta.
        var controller = NewClientesController(cliente: new ClienteDto
        {
            Id = 1, Nombre = "Juan", Apellido = "Perez", TelefonoPrincipal = "1144556677",
            FechaAlta = new DateOnly(2024, 1, 15),
            // DeletedAt null por defecto → cliente activo.
        });

        await controller.Edit(1);

        // ViewBag.DeletedAt es dynamic (object). Hay que castear a DateTime?
        // para que FluentAssertions infiera el tipo y aplique BeNull().
        ((DateTime?)controller.ViewBag.DeletedAt).Should().BeNull(
            "Edit GET debe pasar DeletedAt (no Activo) por ViewBag para que la vista derive el badge");
        ((DateOnly)controller.ViewBag.FechaAlta).Should().Be(new DateOnly(2024, 1, 15),
            "Edit GET debe pasar la FechaAlta del DTO por ViewBag");
    }

    [Fact]
    public async Task ClientesController_EditGet_ConClienteSoftDeleted_PopulaViewBagDeletedAt()
    {
        var fechaBaja = new DateTime(2024, 6, 1, 10, 0, 0);
        var controller = NewClientesController(cliente: new ClienteDto
        {
            Id = 1, Nombre = "Juan", Apellido = "Perez", TelefonoPrincipal = "1144556677",
            FechaAlta = new DateOnly(2024, 1, 15),
            DeletedAt = fechaBaja,
        });

        await controller.Edit(1);

        // ViewBag.DeletedAt es dynamic (object). Hay que castear para que
        // FluentAssertions pueda aplicar `Should().Be()` con inferencia
        // fuerte de tipo.
        ((DateTime?)controller.ViewBag.DeletedAt).Should().Be(fechaBaja,
            "Edit GET debe pasar la fecha de baja para que la vista muestre 'Inactivo'");
    }

    [Fact]
    public async Task EmpleadosController_EditGet_PopulaViewBagActivo()
    {
        var controller = NewEmpleadosController(empleado: new EmpleadoDto
        {
            Id = 1, Nombre = "Juan", Apellido = "Perez", Activo = true,
        });

        await controller.Edit(1);

        ((bool)controller.ViewBag.Activo).Should().Be(true);
    }

    [Fact]
    public async Task ProductosController_EditGet_PopulaViewBagActivo()
    {
        var controller = NewProductosController(producto: new ProductoDto
        {
            Id = 1, Codigo = "GAS-10", Nombre = "Garrafa 10kg",
            TipoProductoId = 1, UnidadVenta = "UNIDAD", PrecioActual = 15000m,
            Activo = true,
        });

        await controller.Edit(1);

        ((bool)controller.ViewBag.Activo).Should().Be(true);
    }

    [Fact]
    public async Task GarrafasController_EditGet_PopulaViewBagActivo()
    {
        var controller = NewGarrafasController(garrafa: new GarrafaDto
        {
            Id = 1, Codigo = "GAR-001", CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = 1, Activo = true,
        });

        await controller.Edit(1);

        ((bool)controller.ViewBag.Activo).Should().Be(true);
    }

    // ====================================================================
    // Issue #145 Slice 2: ProductosController.Restore (AdminOnly).
    // Cubre el wiring del Controller — happy path + 404. La enforcement de
    // [Authorize(Policy="AdminOnly")] la cubre el middleware de ASP.NET Core;
    // no la testeamos a nivel unitario (no hay WebApplicationFactory en repo).
    // ====================================================================

    [Fact]
    public async Task ProductosController_Restore_RedirectsToIndex_OnServiceReturnsTrue()
    {
        // Producto soft-deleted existe y RestoreAsync devuelve true
        // (operación exitosa). Controller: TempData[Success] + redirect a Index.
        var fake = new FakeProductoService(producto: null)
        {
            RestoreReturns = true,
        };
        var controller = NewProductosControllerWithTempData(fake);

        var result = await controller.Restore(id: 1, ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(ProductosController.Index));
        fake.RestoreLlamadas.Should().Be(1, "RestoreAsync debe invocarse exactamente una vez");
        fake.RestoreUltimoId.Should().Be(1, "el id del POST debe propagarse al Service");
        fake.RestoreUltimoUpdatedBy.Should().NotBeNull(
            "GetCurrentUserId() lee la claim del ControllerContext — si es null, el test harness esta roto");
    }

    [Fact]
    public async Task ProductosController_Restore_RedirectsToIndex_OnServiceReturnsFalse()
    {
        // Producto no existe o ya esta activo: RestoreAsync devuelve false.
        // Controller: TempData[Error] + redirect a Index (no NotFound — patron
        // tomado de ClientesController.Restore).
        var fake = new FakeProductoService(producto: null)
        {
            RestoreReturns = false,
        };
        var controller = NewProductosControllerWithTempData(fake);

        var result = await controller.Restore(id: 999999, ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(ProductosController.Index));
        fake.RestoreLlamadas.Should().Be(1);
    }

    // ---------- Helpers ----------

    private static ClientesController NewClientesController(ClienteDto cliente)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<Mappings.MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var service = new FakeClienteService(cliente);
        var controller = new ClientesController(service, mapper)
        {
            ControllerContext = NewControllerContext(),
        };
        return controller;
    }

    private static EmpleadosController NewEmpleadosController(EmpleadoDto empleado)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<Mappings.MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var service = new FakeEmpleadoService(empleado);
        var controller = new EmpleadosController(service, mapper)
        {
            ControllerContext = NewControllerContext(),
        };
        return controller;
    }

    private static ProductosController NewProductosController(ProductoDto producto)
        => NewProductosController(new FakeProductoService(producto));

    /// <summary>Issue #145 Slice 2: overload que acepta el fake configurable para Restore.</summary>
    private static ProductosController NewProductosController(IProductoService service)
    {
        var controller = new ProductosController(service)
        {
            ControllerContext = NewControllerContext(),
        };
        return controller;
    }

    /// <summary>
    /// Issue #145 Slice 2: variante con TempDataProvider in-memory para tests
    /// que escriben TempData (Restore escribe TempData[Success]/TempData[Error]).
    /// El overload default no setea TempDataProvider porque los otros tests
    /// solo leen Controller.ViewBag y ViewBag funciona sin TempData.
    /// </summary>
    private static ProductosController NewProductosControllerWithTempData(IProductoService service)
    {
        var provider = new InMemoryTempDataProvider();
        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(provider)
            .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var controller = new ProductosController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };
        controller.ControllerContext.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") },
                "TestAuth"));
        return controller;
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

    private static GarrafasController NewGarrafasController(GarrafaDto garrafa)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GarrafasController>.Instance;
        var garrafaService = new FakeGarrafaService(garrafa);
        var clienteService = new FakeClienteService(null);
        var proveedorService = new FakeProveedorService();
        var recepcionService = new FakeRecepcionService();
        var controller = new GarrafasController(logger, garrafaService, clienteService, proveedorService, recepcionService)
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

    // ---------- Fakes ----------

    private sealed class FakeClienteService : IClienteService
    {
        private readonly ClienteDto? _cliente;
        public FakeClienteService(ClienteDto? cliente) { _cliente = cliente; }
        public Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_cliente);
        public Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ClienteDto>>(new[] { _cliente! });
        public Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<ClienteDto>>(new List<ClienteDto>());
        public Task<PagedResult<ClienteDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ClienteDto>> GetDeletedAsync(string? b, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> UpdateAsync(UpdateClienteDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => Task.FromResult(new List<ProvinciaDto>());
        public Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<VSaldoClienteDto>>(new List<VSaldoClienteDto>());
    }

    private sealed class FakeEmpleadoService : IEmpleadoService
    {
        private readonly EmpleadoDto? _empleado;
        public FakeEmpleadoService(EmpleadoDto? empleado) { _empleado = empleado; }
        public Task<EmpleadoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_empleado);
        public Task<PagedResult<EmpleadoDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => Task.FromResult(new List<ProvinciaDto>());
    }

    private sealed class FakeProductoService : IProductoService
    {
        private readonly ProductoDto? _producto;
        public FakeProductoService(ProductoDto? producto) { _producto = producto; }

        /// <summary>Issue #145 Slice 2: configurable para tests de Restore.</summary>
        public bool RestoreReturns { get; set; }
        public int RestoreLlamadas { get; private set; }
        public ulong RestoreUltimoId { get; private set; }
        public ulong? RestoreUltimoUpdatedBy { get; private set; }

        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_producto);
        public Task<ProductoDto?> GetByCodigoAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<TipoProductoDto>>(new List<TipoProductoDto>());
        public Task<ProductoDto> CreateAsync(CreateProductoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
        {
            RestoreLlamadas++;
            RestoreUltimoId = id;
            RestoreUltimoUpdatedBy = updatedBy;
            return Task.FromResult(RestoreReturns);
        }
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeGarrafaService : IGarrafaService
    {
        private readonly GarrafaDto? _garrafa;
        public FakeGarrafaService(GarrafaDto? garrafa) { _garrafa = garrafa; }
        public Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_garrafa);
        public Task<GarrafaDto?> GetByCodigoAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong e, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<GarrafaDto>> GetPagedAsync(string? codigo, byte? capacidad, int page = 1, int pageSize = 20, string sortBy = "codigo", string sortDir = "asc", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<EstadoGarrafaDto>>(new List<EstadoGarrafaDto>());
        public Task<GarrafaDto> CreateAsync(CreateGarrafaDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong g, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong g, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RegistrarMovimientoPorCanjeAsync(ulong g, ulong ed, ulong? c, ulong p, string t, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VGarrafaEnCliente>> GetEnClientesAsync(ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeProveedorService : IProveedorService
    {
        public Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProveedorDto?> GetByCuitAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProveedorDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => Task.FromResult(new PagedResult<ProveedorDto> { Items = new List<ProveedorDto>(), Total = 0, Page = p, PageSize = t });
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
        public Task<IEnumerable<RecepcionDto>> GetRecientesAsync(int c, CancellationToken ct = default) => Task.FromResult<IEnumerable<RecepcionDto>>(new List<RecepcionDto>());
    }
}