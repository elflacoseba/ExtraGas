using AutoMapper;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión del módulo ProveedoresController.
/// Verifican que las pantallas Create (GET/POST) y Details cargan
/// <c>ViewBag.Provincias</c> en TODAS las ramas de ejecución. Si alguien
/// borra un <c>LoadViewBagsAsync(ct)</c> en alguna rama de error del POST,
/// el dropdown de Provincia en el re-render queda vacío y el operador
/// no puede editar.
/// </summary>
public class ProveedoresControllerCreateViewBagTests
{
    private const string TestUserId = "1";

    private static readonly List<ProvinciaDto> ProvinciasEsperadas = new()
    {
        new() { Id = 1, Nombre = "Buenos Aires" },
        new() { Id = 2, Nombre = "Córdoba" },
        new() { Id = 3, Nombre = "Santa Fe" },
    };

    // ---------- Rama 1: Create (GET) siempre carga provincias ----------

    [Fact]
    public async Task Create_Get_CargaViewBagProvincias()
    {
        var controller = NewController();

        var result = await controller.Create(CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertProvinciasViewBag(controller);
    }

    // ---------- Rama 2: Create (POST) con ModelState inválido ----------

    [Fact]
    public async Task Create_PostConModelStateInvalido_RepopulaViewBagProvincias()
    {
        var controller = NewController();

        // Forzamos un error de validación (finge ser el binder: por ejemplo, un
        // atributo [Required] que no se cumplió). Sin ModelBinding real esta es
        // la forma de simular el POST fallido.
        controller.ModelState.AddModelError(
            nameof(CreateProveedorDto.Cuit),
            "El CUIT es obligatorio.");

        var dto = new CreateProveedorDto
        {
            RazonSocial = "Proveedor Test",
            // Cuit queda null → RequiredAttribute rechazaría, pero el ModelState
            // ya está marcado inválido por la línea de arriba.
        };

        var result = await controller.Create(dto, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertProvinciasViewBag(controller);
    }

    // ---------- Rama 3: Details siempre recarga provincias ----------

    [Fact]
    public async Task Details_ConProveedorExistente_RepopulaViewBagProvincias()
    {
        var controller = NewController(proveedor: new ProveedorDto
        {
            Id = 10,
            RazonSocial = "Shell",
            Cuit = "30123456780",
        });

        var result = await controller.Details(10, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertProvinciasViewBag(controller);
    }

    [Fact]
    public async Task Details_ConProveedorInexistente_RetornaNotFound()
    {
        var controller = NewController(); // sin proveedor configurado

        var result = await controller.Details(999, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static void AssertProvinciasViewBag(ProveedoresController controller)
    {
        var enViewBag = controller.ViewBag.Provincias as List<ProvinciaDto>;

        enViewBag.Should().NotBeNull(
            "ViewBag.Provincias no puede ser null despues de un error de validacion; " +
            "de lo contrario el <select> de Provincia en el re-render queda solo " +
            "con el placeholder '-- Seleccione provincia --' y el operador no puede " +
            "elegir (regresion del patron ViewBag ya documentado en Usuarios).");

        enViewBag.Should().BeEquivalentTo(ProvinciasEsperadas,
            options => options.WithStrictOrdering(),
            "las provincias deben coincidir con las que devuelve el service");
    }

    private static ProveedoresController NewController(ProveedorDto? proveedor = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.NameIdentifier, TestUserId),
                    },
                    "TestAuth")),
        };

        // Mapper real con el MappingProfile de la app. Es lo más fiel al
        // wiring de producción sin necesidad de InMemory DbContext.
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();

        var service = new FakeProveedorService(ProvinciasEsperadas, proveedor);

        return new ProveedoresController(service, mapper)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };
    }

    /// <summary>
    /// Fake de <see cref="IProveedorService"/> que solo implementa los métodos
    /// ejercitados por estos tests. El resto lanza <see cref="NotImplementedException"/>
    /// (mismo patrón que los fakes de Usuarios) para que un cambio en el wiring
    /// del controller rompa un test en lugar de fallar silenciosamente.
    /// </summary>
    private sealed class FakeProveedorService : IProveedorService
    {
        private readonly List<ProvinciaDto> _provincias;
        private readonly ProveedorDto? _proveedor;

        public FakeProveedorService(List<ProvinciaDto> provincias, ProveedorDto? proveedor)
        {
            _provincias = provincias;
            _proveedor = proveedor;
        }

        public Task<IEnumerable<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ProvinciaDto>>(_provincias);

        public Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
            => Task.FromResult(_proveedor?.Id == id ? _proveedor : null);

        // Métodos no usados por estos tests:
        public Task<ProveedorDto?> GetByCuitAsync(string cuit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedor, ulong? createdBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto proveedor, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SearchResultDto<ProveedorDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
