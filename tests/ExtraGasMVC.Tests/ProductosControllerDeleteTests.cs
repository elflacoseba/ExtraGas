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
/// Tests del ProductosController.Delete (issue #147 slice 3 item 2).
/// Cubre el wiring de GET (pasa impacto a la view) + POST (valida
/// confirmCode contra el codigo del producto).
/// </summary>
public class ProductosControllerDeleteTests
{
    [Fact]
    public async Task Delete_GET_PassesImpactToView()
    {
        // Spec scenario "any dependency > 0 → type-to-confirm": GET debe
        // llamar GetByIdAsync + GetDeleteImpactAsync y pasar el DTO de
        // impacto a la view (junto con ViewBag.ExpectedCode que usa el JS).
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = new ProductoDto
            {
                Id = 7,
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                UnidadVenta = "GARRAFA",
                UnidadVentaId = 2,
                PrecioActual = 15000m,
                Activo = true,
            },
            Impacto = new ProductoDeleteImpactDto(
                ProductoId: 7,
                Codigo: "GAS-10",
                PedidoItemsCount: 2,
                RecepcionItemsCount: 0,
                MovimientosGarrafaCount: 1),
        };
        var controller = NewProductosController(fake);

        var result = await controller.Delete(id: 7, ct: default);

        result.Should().BeOfType<ViewResult>();
        var viewResult = (ViewResult)result;
        viewResult.Model.Should().BeOfType<ProductoDeleteImpactDto>()
            .Which.TotalCount.Should().Be(3,
                "el DTO pasado a la view debe tener los 3 contadores no-cero");
        viewResult.Model.Should().BeOfType<ProductoDeleteImpactDto>()
            .Which.HasDependencies.Should().BeTrue();
        ((string)controller.ViewBag.ExpectedCode).Should().Be("GAS-10",
            "ViewBag.ExpectedCode alimenta el JS para validar el type-to-confirm");
        fake.GetByIdLlamadas.Should().Be(1);
        fake.GetDeleteImpactLlamadas.Should().Be(1);
        fake.UltimoImpactoId.Should().Be(7UL);
    }

    [Fact]
    public async Task Delete_GET_NoDependencies_StillRendersViewWithImpact()
    {
        // Spec scenario "0 dependencies → direct confirm": el DTO llega con
        // todos los contadores en 0 → la view renderiza confirm simple.
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = new ProductoDto
            {
                Id = 8,
                Codigo = "NEW-PROD",
                Nombre = "Nuevo",
                TipoProductoId = 1,
                UnidadVenta = "UNIDAD",
                UnidadVentaId = 1,
                PrecioActual = 0m,
                Activo = true,
            },
            Impacto = new ProductoDeleteImpactDto(8, "NEW-PROD", 0, 0, 0),
        };
        var controller = NewProductosController(fake);

        var result = await controller.Delete(id: 8, ct: default);

        result.Should().BeOfType<ViewResult>();
        var dto = ((ViewResult)result).Model.Should().BeOfType<ProductoDeleteImpactDto>().Subject;
        dto.TotalCount.Should().Be(0);
        dto.HasDependencies.Should().BeFalse(
            "el View usa HasDependencies para decidir confirm simple vs type-to-confirm");
    }

    [Fact]
    public async Task Delete_POST_WrongConfirmCode_ReturnsViewWithError()
    {
        // Spec scenario "mismatch blocks Delete": si el operador tipea mal
        // el codigo, el Controller recarga la view de impacto con un error
        // en ModelState (no procede con DeleteAsync).
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = new ProductoDto
            {
                Id = 7,
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                UnidadVenta = "GARRAFA",
                UnidadVentaId = 2,
                PrecioActual = 15000m,
            },
            Impacto = new ProductoDeleteImpactDto(7, "GAS-10", 1, 0, 0),
        };
        var controller = NewProductosController(fake);

        var result = await controller.Delete(id: 7, confirmCode: "GAS-99", ct: default);

        result.Should().BeOfType<ViewResult>(
            "mismatch debe recargar la view de impacto, no redirigir");
        ((string)controller.ViewBag.ConfirmError).Should().Contain("Código incorrecto");
        ((string)controller.ViewBag.ConfirmInput).Should().Be("GAS-99");
        fake.DeleteAsyncLlamadas.Should().Be(0,
            "DeleteAsync NO debe invocarse con un confirmCode incorrecto");
    }

    [Fact]
    public async Task Delete_POST_EmptyConfirmCode_ReturnsViewWithError()
    {
        // Edge case: confirmCode vacío/null (form mal submitteado). Mismo
        // comportamiento que mismatch: recarga la view con error.
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = new ProductoDto
            {
                Id = 7,
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                UnidadVenta = "GARRAFA",
                UnidadVentaId = 2,
                PrecioActual = 15000m,
            },
            Impacto = new ProductoDeleteImpactDto(7, "GAS-10", 0, 0, 0),
        };
        var controller = NewProductosController(fake);

        var result = await controller.Delete(id: 7, confirmCode: "", ct: default);

        result.Should().BeOfType<ViewResult>();
        ((string)controller.ViewBag.ConfirmError).Should().Contain("Código incorrecto");
        fake.DeleteAsyncLlamadas.Should().Be(0);
    }

    [Fact]
    public async Task Delete_POST_CorrectConfirmCode_CallsDeleteAsync_AndRedirectsToIndex()
    {
        // Happy path: confirmCode == producto.Codigo → DeleteAsync se llama
        // una vez con el id y el currentUserId → TempData[Success] +
        // redirect a Index.
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = new ProductoDto
            {
                Id = 7,
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                UnidadVenta = "GARRAFA",
                UnidadVentaId = 2,
                PrecioActual = 15000m,
            },
            Impacto = new ProductoDeleteImpactDto(7, "GAS-10", 0, 0, 0),
            DeleteAsyncReturns = true,
        };
        var controller = NewProductosControllerWithTempData(fake);

        var result = await controller.Delete(id: 7, confirmCode: "GAS-10", ct: default);

        result.Should().BeOfType<RedirectToActionResult>().Subject
            .ActionName.Should().Be(nameof(ProductosController.Index));
        fake.DeleteAsyncLlamadas.Should().Be(1,
            "confirmCode correcto debe proceder con DeleteAsync una sola vez");
        fake.DeleteAsyncUltimoId.Should().Be(7UL);
    }

    [Fact]
    public async Task Delete_GET_UnknownId_ReturnsNotFound()
    {
        // GetByIdAsync devuelve null → Controller retorna 404.
        var fake = new ConfigurableProductoServiceForDelete
        {
            Producto = null,
            Impacto = null,
        };
        var controller = NewProductosController(fake);

        var result = await controller.Delete(id: 99999, ct: default);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ---------- Helpers ----------

    private static ProductosController NewProductosController(IProductoService service)
        => new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") },
                            "TestAuth")),
                },
                RouteData = new RouteData(),
            },
        };

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

    /// <summary>
    /// Fake configurable del ProductoService que cubre los métodos que
    /// ProductosController.Delete usa. Default: tira NotImplementedException
    /// para forzar a los tests a configurar lo que necesitan.
    /// </summary>
    private sealed class ConfigurableProductoServiceForDelete : IProductoService
    {
        public ProductoDto? Producto { get; set; }
        public ProductoDeleteImpactDto? Impacto { get; set; }
        public bool DeleteAsyncReturns { get; set; }

        public int GetByIdLlamadas { get; private set; }
        public int GetDeleteImpactLlamadas { get; private set; }
        public ulong UltimoImpactoId { get; private set; }
        public int DeleteAsyncLlamadas { get; private set; }
        public ulong DeleteAsyncUltimoId { get; private set; }
        public ulong? DeleteAsyncUltimoUsuarioId { get; private set; }

        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
        {
            GetByIdLlamadas++;
            return Task.FromResult(Producto);
        }

        public Task<ProductoDeleteImpactDto> GetDeleteImpactAsync(ulong id, CancellationToken ct = default)
        {
            GetDeleteImpactLlamadas++;
            UltimoImpactoId = id;
            return Task.FromResult(Impacto!);
        }

        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy = null, CancellationToken ct = default)
        {
            DeleteAsyncLlamadas++;
            DeleteAsyncUltimoId = id;
            DeleteAsyncUltimoUsuarioId = updatedBy;
            return Task.FromResult(DeleteAsyncReturns);
        }

        // El resto tira NotImplementedException — los tests de Delete no
        // los invocan.
        public Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<UnidadVentaDto>> GetUnidadesVentaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
