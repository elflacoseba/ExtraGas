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
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de los Controllers para verificar que ViewBag.Activo (y
/// ViewBag.FechaAlta en ClientesController) se popula con los valores del
/// DTO despues del refactor del issue #114.
/// La logica de negocio la cubren los tests del Service y del helper;
/// aca solo se valida el wiring del Controller.
/// </summary>
public class ControllersActivoViewBagTests
{
    [Fact]
    public async Task ClientesController_EditGet_PopulaViewBagActivoYFechaAlta()
    {
        var controller = NewClientesController(cliente: new ClienteDto
        {
            Id = 1, Nombre = "Juan", Apellido = "Perez", TelefonoPrincipal = "1",
            FechaAlta = new DateOnly(2024, 1, 15), Activo = true,
        });

        await controller.Edit(1);

        ((bool)controller.ViewBag.Activo).Should().Be(true,
            "Edit GET debe pasar el Activo del DTO por ViewBag para mostrarlo read-only");
        ((DateOnly)controller.ViewBag.FechaAlta).Should().Be(new DateOnly(2024, 1, 15),
            "Edit GET debe pasar la FechaAlta del DTO por ViewBag");
    }

    [Fact]
    public async Task ClientesController_EditGet_ConClienteInactivo_PopulaViewBagActivoFalse()
    {
        var controller = NewClientesController(cliente: new ClienteDto
        {
            Id = 1, Nombre = "Juan", Apellido = "Perez", TelefonoPrincipal = "1",
            FechaAlta = new DateOnly(2024, 1, 15), Activo = false,
        });

        await controller.Edit(1);

        ((bool)controller.ViewBag.Activo).Should().Be(false);
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
    {
        var service = new FakeProductoService(producto);
        var controller = new ProductosController(service)
        {
            ControllerContext = NewControllerContext(),
        };
        return controller;
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
        public Task<SearchResultDto<ClienteDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> CreateAsync(CreateClienteDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClienteDto> UpdateAsync(UpdateClienteDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => Task.FromResult(new List<ProvinciaDto>());
    }

    private sealed class FakeEmpleadoService : IEmpleadoService
    {
        private readonly EmpleadoDto? _empleado;
        public FakeEmpleadoService(EmpleadoDto? empleado) { _empleado = empleado; }
        public Task<EmpleadoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_empleado);
        public Task<SearchResultDto<EmpleadoDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default) => Task.FromResult(new List<ProvinciaDto>());
    }

    private sealed class FakeProductoService : IProductoService
    {
        private readonly ProductoDto? _producto;
        public FakeProductoService(ProductoDto? producto) { _producto = producto; }
        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_producto);
        public Task<ProductoDto?> GetByCodigoAsync(string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<TipoProductoDto>>(new List<TipoProductoDto>());
        public Task<ProductoDto> CreateAsync(CreateProductoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task<SearchResultDto<ProveedorDto>> SearchAsync(string? b, bool s, int p, int t, CancellationToken ct = default) => Task.FromResult(new SearchResultDto<ProveedorDto> { Items = new List<ProveedorDto>(), Total = 0, Pagina = p, Tamanio = t });
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