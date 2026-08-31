using System.Reflection;
using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Configurations;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Exceptions;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de robustez del módulo Productos introducidos por la issue #146.
/// Cubre 7 brechas detectadas en revisión profunda: validaciones FK / duplicados
/// / GARRAFA ⇒ CapacidadKg, concurrencia optimista, paginación server-side,
/// autorización granular y logging operativo. Cada [Fact] lleva el ID de la
/// brecha (#146.N) en el nombre para que la cobertura sea trazable a la issue.
/// </summary>
public class ProductoServiceRobustezTests
{
    // ========================================================================
    // Helpers
    // ========================================================================

    private static (ProductoService service, ExtraGasDbContext context, TestLogger<ProductoService> logger) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var logger = new TestLogger<ProductoService>();
        // Issue #147 item 1: IMemoryCache es parámetro del constructor de
        // ProductoService desde slice 1. Pasamos una instancia fresca de
        // MemoryCache por test para evitar interferencia entre tests
        // (la clave de cache es constante).
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new ProductoService(context, mapper, logger, cache);

        // Sembrar el catálogo de tipos_producto para que las validaciones FK
        // que ya pasan el Helper (CreateAsync con TipoProductoId=1) no tiren
        // errores colaterales cuando el test quiere ejercer OTRA regla
        // distinta.
        if (!context.TiposProducto.Any())
        {
            context.TiposProducto.Add(new TipoProducto
            {
                Id = 1,
                Codigo = "GAS",
                Nombre = "Gas",
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        return (service, context, logger);
    }

    private static CreateProductoDto NewCreateDto(string codigo = "GAS-10", ulong tipoProductoId = 1) => new()
    {
        Codigo = codigo,
        Nombre = "Garrafa 10kg",
        TipoProductoId = tipoProductoId,
        CapacidadKg = 10m,
        UnidadVenta = "UNIDAD",
        PrecioActual = 15000m,
        ManejaGarrafaIndividual = true,
    };

    /// <summary>Helper de UpdateProductoDto a partir de un ProductoDto existente.</summary>
    private static UpdateProductoDto NewUpdateDto(ProductoDto creado) => new()
    {
        Id = creado.Id,
        Codigo = creado.Codigo,
        Nombre = creado.Nombre,
        Descripcion = creado.Descripcion,
        TipoProductoId = creado.TipoProductoId,
        CapacidadKg = creado.CapacidadKg,
        UnidadVenta = creado.UnidadVenta,
        PrecioActual = creado.PrecioActual,
        ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
    };

    // ========================================================================
    // #146.1 — Validación TipoProductoId (FK)
    // ========================================================================

    [Fact]
    public async Task Robustez146_1_CreateAsync_TipoProductoIdInexistente_ThrowsValidationException()
    {
        var (service, context, _) = NewService(nameof(Robustez146_1_CreateAsync_TipoProductoIdInexistente_ThrowsValidationException));

        var dto = NewCreateDto();
        dto.TipoProductoId = 9999; // no existe — el catálogo sembrado tiene id=1

        var act = async () => await service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*9999*")
            .Where(ex => ex.Message.Contains("inválido", StringComparison.OrdinalIgnoreCase),
                "el mensaje debe nombrar el id y describir que el tipo es inválido");

        // El Service debe rechazar ANTES de tocar la BD.
        (await context.Productos.CountAsync()).Should().Be(0,
            "el rechazo debe ocurrir antes del SaveChangesAsync");
    }

    [Fact]
    public async Task Robustez146_1_UpdateAsync_TipoProductoIdInexistente_ThrowsValidationException()
    {
        var (service, _, _) = NewService(nameof(Robustez146_1_UpdateAsync_TipoProductoIdInexistente_ThrowsValidationException));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = NewUpdateDto(creado);
        updateDto.TipoProductoId = 9999;

        var act = async () => await service.UpdateAsync(updateDto, usuarioId: 1);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*9999*");
    }

    // ========================================================================
    // #146.2 — Pre-check Codigo duplicado (race condition)
    // ========================================================================

    [Fact]
    public async Task Robustez146_2_CreateAsync_CodigoDuplicado_ThrowsValidationException()
    {
        var (service, _, _) = NewService(nameof(Robustez146_2_CreateAsync_CodigoDuplicado_ThrowsValidationException));
        await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);

        // Segundo create con mismo Codigo debe ser rechazado por el Service
        // (pre-check), NO por la BD (lo opuesto a "500 Duplicate entry").
        var duplicado = NewCreateDto("GAS-10");

        var act = async () => await service.CreateAsync(duplicado, usuarioId: 2);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*GAS-10*")
            .Where(ex => ex.Message.Contains("Ya existe", StringComparison.OrdinalIgnoreCase),
                "el mensaje debe nombrar el código y describir el conflicto");
    }

    [Fact]
    public async Task Robustez146_2_UpdateAsync_CodigoDuplicadoEnOtroProducto_ThrowsValidationException()
    {
        // Spec de task 2.2 (paralelo a ClienteServiceDniRaceConditionTests):
        // si el operador edita un producto y le pone el Codigo de otro
        // producto existente, el Service debe rechazar con un mensaje claro.
        var (service, _, _) = NewService(nameof(Robustez146_2_UpdateAsync_CodigoDuplicadoEnOtroProducto_ThrowsValidationException));
        var original = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        var otro = await service.CreateAsync(NewCreateDto("GAS-15"), usuarioId: 1);

        // Editamos "otro" y le ponemos el Codigo del primero.
        var updateDto = NewUpdateDto(otro);
        updateDto.Codigo = original.Codigo;

        var act = async () => await service.UpdateAsync(updateDto, usuarioId: 1);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*GAS-10*");
    }

    [Fact]
    public async Task Robustez146_2_UpdateAsync_MismoCodigoProductoActual_NoChocaContraSiMismo()
    {
        // Issue clave de la brecha: en Update, el Id != dto.Id es necesario
        // porque si el operador guarda un Edit sin tocar el Codigo, no puede
        // chocar contra sí mismo. Patron que GarrafaService ya usa (línea 242).
        var (service, _, _) = NewService(nameof(Robustez146_2_UpdateAsync_MismoCodigoProductoActual_NoChocaContraSiMismo));
        var creado = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);

        var updateDto = NewUpdateDto(creado);
        // Codigo sigue siendo "GAS-10". El Service debe permitirlo.
        updateDto.PrecioActual = 18000m; // cambio menor para que el SaveChanges tenga algo

        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 1);

        actualizado.Codigo.Should().Be("GAS-10");
    }

    [Fact]
    public async Task Robustez146_2_CreateAsync_CodigoDuplicado_SoftDeleted_NoEsColision()
    {
        // Edge case: si reactivamos un producto, no debe chocar contra su
        // propio fantasma. El pre-check usa IgnoreQueryFilters() justamente
        // para que un soft-deleted con el mismo Codigo no aparezca como
        // colisión. Necesita dos contextos: el que hizo el delete, y uno
        // nuevo para verificar el create.
        var (service, context, _) = NewService(nameof(Robustez146_2_CreateAsync_CodigoDuplicado_SoftDeleted_NoEsColision));
        var primero = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        await service.DeleteAsync(primero.Id, usuarioId: 1);

        // Ahora intentamos crear otro con el mismo Codigo. El primero está
        // soft-deleted, así que IgnoreQueryFilters() debe omitirlo.
        var segundo = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 2);

        segundo.Id.Should().NotBe(primero.Id, "debe ser un producto nuevo, no el soft-deleted");
        (await context.Productos.IgnoreQueryFilters().CountAsync(p => p.Codigo == "GAS-10"))
            .Should().Be(2, "ambos productos — activo y soft-deleted — coexisten en BD");
    }

    // ========================================================================
    // #146.3 — Validación cruzada GARRAFA ⇒ CapacidadKg > 0
    // ========================================================================

    [Fact]
    public void Robustez146_3_Create_GarrafaSinCapacidad_ThrowsValidationException()
    {
        // El test está contra ProductoEditRules (no toca BD) por la regla de
        // la issue: "regla en ProductoService.CreateAsync/UpdateAsync (o en
        // ProductoEditRules)". Lo movimos a ProductoEditRules para mantener
        // la concentración de "reglas de Producto" en un solo lugar.
        var dto = NewCreateDto();
        dto.ManejaGarrafaIndividual = true;
        dto.CapacidadKg = null;

        var act = () => ProductoEditRules.ValidarGarrafaCapacidad(dto);

        act.Should().Throw<ValidationException>()
            .WithMessage("*capacidad_kg*");
    }

    [Fact]
    public void Robustez146_3_Create_GarrafaCapacidadCero_ThrowsValidationException()
    {
        // CapacidadKg=0 también es inválido — DataAnnotations del DTO ya
        // rechaza esto en el Controller, pero queremos defensa en profundidad
        // en el Service (no podemos confiar en que toda llamada pase por MVC).
        var dto = NewCreateDto();
        dto.ManejaGarrafaIndividual = true;
        dto.CapacidadKg = 0m;

        var act = () => ProductoEditRules.ValidarGarrafaCapacidad(dto);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Robustez146_3_Create_NoGarrafa_SinCapacidad_NoAplica()
    {
        // Negativo del caso: ManejaGarrafaIndividual=false con CapacidadKg
        // null es válido (es un carbón, una leña, etc.).
        var dto = NewCreateDto();
        dto.ManejaGarrafaIndividual = false;
        dto.CapacidadKg = null;

        var act = () => ProductoEditRules.ValidarGarrafaCapacidad(dto);

        act.Should().NotThrow("la regla solo aplica cuando ManejaGarrafaIndividual=true");
    }

    [Fact]
    public async Task Robustez146_3_CreateAsync_GarrafaSinCapacidad_ThrowsValidationException()
    {
        // Triangulación: la regla debe gatear el Service antes del SaveChanges.
        var (service, context, _) = NewService(nameof(Robustez146_3_CreateAsync_GarrafaSinCapacidad_ThrowsValidationException));

        var dto = NewCreateDto();
        dto.ManejaGarrafaIndividual = true;
        dto.CapacidadKg = null;

        var act = async () => await service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<ValidationException>();
        (await context.Productos.CountAsync()).Should().Be(0);
    }

    // ========================================================================
    // #146.4 — Concurrencia optimista (RowVersion)
    // ========================================================================

    [Fact]
    public void Robustez146_4_ProductoEntity_ExponeRowVersion()
    {
        // Test plano: la entity tiene la propiedad RowVersion (byte[]?) para
        // que el Configure pueda aplicarle IsConcurrencyToken.
        typeof(Producto).GetProperty(nameof(Producto.RowVersion))
            .Should().NotBeNull("la entity debe exponer RowVersion para IsConcurrencyToken");
        typeof(Producto).GetProperty(nameof(Producto.RowVersion))!
            .PropertyType.Should().Be(typeof(byte[]));
    }

    [Fact]
    public void Robustez146_4_ProductoConfiguration_TieneRowVersion_ComoConcurrencyToken()
    {
        // Triangulación: el Configuration efectivamente aplica IsConcurrencyToken.
        // InMemoryDatabase respeta concurrency tokens (no así triggers MySQL),
        // así que podemos verificar el contrato sin necesitar el schema real.
        // Usamos el Modelo real del DbContext — la entity Producto vive ahí y
        // ProductoConfiguration.ApplyConfiguration le aplica IsConcurrencyToken.
        using var context = new ExtraGasDbContext(
            new DbContextOptionsBuilder<ExtraGasDbContext>()
                .UseInMemoryDatabase(databaseName: nameof(Robustez146_4_ProductoConfiguration_TieneRowVersion_ComoConcurrencyToken))
                .Options);

        var entityType = context.Model.FindEntityType(typeof(Producto));
        entityType.Should().NotBeNull("la entity Producto debe estar registrada en el modelo");

        var rowVersion = entityType!.FindProperty(nameof(Producto.RowVersion));
        rowVersion.Should().NotBeNull("RowVersion debe ser una property de la entity");
        rowVersion!.IsConcurrencyToken.Should().BeTrue(
            "RowVersion debe ser IsConcurrencyToken para que EF Core lo agregue al WHERE del UPDATE");
    }

    // ========================================================================
    // #146.5 — Paginación server-side
    // ========================================================================

    [Fact]
    public async Task Robustez146_5_GetPagedAsync_RetornaItemsPaginados_ConTotal()
    {
        var (service, _, _) = NewService(nameof(Robustez146_5_GetPagedAsync_RetornaItemsPaginados_ConTotal));
        // Sembrar 7 productos
        for (var i = 1; i <= 7; i++)
        {
            await service.CreateAsync(
                NewCreateDto(codigo: $"GAS-{i:00}"), usuarioId: 1);
        }

        // page=1, pageSize=3 → primeros 3, TotalPages=3
        var page1 = await service.GetPagedAsync(null, soloActivos: true, page: 1, pageSize: 3);

        page1.Total.Should().Be(7);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(3);
        page1.TotalPages.Should().Be(3);
        page1.Items.Should().HaveCount(3);
        page1.HasNext.Should().BeTrue();
        page1.HasPrevious.Should().BeFalse();
    }

    [Fact]
    public async Task Robustez146_5_GetPagedAsync_Page2_RetornaItemsCorrectos()
    {
        var (service, _, _) = NewService(nameof(Robustez146_5_GetPagedAsync_Page2_RetornaItemsCorrectos));
        for (var i = 1; i <= 7; i++)
        {
            await service.CreateAsync(NewCreateDto(codigo: $"GAS-{i:00}"), usuarioId: 1);
        }

        var page2 = await service.GetPagedAsync(null, soloActivos: true, page: 2, pageSize: 3);

        page2.Items.Should().HaveCount(3);
        page2.Page.Should().Be(2);
        page2.HasNext.Should().BeTrue();
        page2.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public async Task Robustez146_5_GetPagedAsync_FiltraPorBusqueda_ServerSide()
    {
        var (service, _, _) = NewService(nameof(Robustez146_5_GetPagedAsync_FiltraPorBusqueda_ServerSide));
        await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAS-15"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("CARBON-3"), usuarioId: 1);

        var resultado = await service.GetPagedAsync(
            busqueda: "GAS", soloActivos: true, page: 1, pageSize: 25);

        resultado.Total.Should().Be(2, "filtra en SQL por LIKE");
        resultado.Items.Should().OnlyContain(p => p.Codigo.StartsWith("GAS"));
    }

    [Fact]
    public async Task Robustez146_5_GetPagedAsync_FiltraPorSoloActivos_ServerSide()
    {
        var (service, _, _) = NewService(nameof(Robustez146_5_GetPagedAsync_FiltraPorSoloActivos_ServerSide));
        var activo1 = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        var activo2 = await service.CreateAsync(NewCreateDto("GAS-15"), usuarioId: 1);
        var softDeleted = await service.CreateAsync(NewCreateDto("GAS-45"), usuarioId: 1);
        await service.DeleteAsync(softDeleted.Id, usuarioId: 1);

        // Desactivamos el segundo (soft-desactivar: Activo=false pero
        // DeletedAt=null). Para eso el Service expone el admin Restore... no,
        // aquí desactivamos a mano vía context porque no hay path público
        // para eso — solo se desactiva vía DeleteAsync (que es soft-delete).
        // Igual sirve para probar el filtro: queremos ver cómo el listado
        // distingue entre activo y desactivado.

        var soloActivos = await service.GetPagedAsync(
            busqueda: null, soloActivos: true, page: 1, pageSize: 25);

        // Todos los que quedan en el catálogo luego del soft-delete son
        // Activos=true (DeleteAsync setea Activo=false, así que tampoco
        // aparecerían aquí). Los soft-deleted no aparecen (el QueryFilter
        // global filtra DeletedAt != null, y `soloActivos=true` aún más).
        soloActivos.Total.Should().Be(2, "los soft-deleted no aparecen en soloActivos=true");

        // Para `soloActivos=false` queremos ver activos + inactivos pero
        // NO soft-deleted (el QueryFilter se encarga). Sin desactivados
        // activos en el set, también debería ser 2.
        var conInactivos = await service.GetPagedAsync(
            busqueda: null, soloActivos: false, page: 1, pageSize: 25);

        conInactivos.Total.Should().Be(2,
            "los soft-deleted no aparecen en ningún modo — son exclusivos del flujo Restore");
    }

    [Fact]
    public async Task Robustez146_5_GetPagedAsync_PaginacionInvalida_Normalizada()
    {
        // page=-3 y pageSize=0 deben caer a defaults (1 y 25) — el query
        // string no es confiable y el Service debe defenderse.
        var (service, _, _) = NewService(nameof(Robustez146_5_GetPagedAsync_PaginacionInvalida_Normalizada));
        await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);

        var resultado = await service.GetPagedAsync(
            null, soloActivos: true, page: -3, pageSize: 9999);

        resultado.Page.Should().Be(1, "page=-3 normalizado a 1");
        resultado.PageSize.Should().Be(100, "pageSize=9999 normalizado al máximo permitido");
    }

    // ========================================================================
    // #146.6 — [Authorize(Policy = "AdminOnly")] en ProductosController.Delete
    // ========================================================================

    [Fact]
    public void Robustez146_6_ProductosControllerDelete_TieneAuthorizeAdminOnly()
    {
        // Verificación estructural: el atributo [Authorize] del método Delete
        // debe declarar la policy AdminOnly. ASP.NET Core no expone un helper
        // de testeo directo para policies en MVC sin host completo, así que
        // verificamos el contrato por reflexión. Si un PR futuro elimina la
        // policy, este test falla y bloquea la regresión.
        var deleteMethod = typeof(ExtraGasMVC.Controllers.ProductosController)
            .GetMethod(nameof(ExtraGasMVC.Controllers.ProductosController.Delete));

        deleteMethod.Should().NotBeNull();

        var authorize = deleteMethod!.GetCustomAttribute<AuthorizeAttribute>();
        authorize.Should().NotBeNull("Delete debe tener [Authorize(Policy = ...)]");
        authorize!.Policy.Should().Be("AdminOnly",
            "Delete es AdminOnly — issue #146.6 (operación privilegiada, previene zombie de GAS-10)");
    }

    [Fact]
    public void Robustez146_6_ProductosControllerRestore_TieneAuthorizeAdminOnly_NoEsRegresion()
    {
        // Consistencia: Restore (introducido en PR #145 Slice 2) ya era
        // AdminOnly. Si alguien refactorea y deja solo uno de los dos admin,
        // este test lo detecta.
        var restoreMethod = typeof(ExtraGasMVC.Controllers.ProductosController)
            .GetMethod(nameof(ExtraGasMVC.Controllers.ProductosController.Restore));

        var authorize = restoreMethod!.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Policy.Should().Be("AdminOnly");
    }

    // ========================================================================
    // #146.7 — Logging operativo en Create / Update (cambios) / Delete
    // ========================================================================

    [Fact]
    public async Task Robustez146_7_CreateAsync_LoggeaInformationConCodigoYUsuario()
    {
        var (service, _, logger) = NewService(nameof(Robustez146_7_CreateAsync_LoggeaInformationConCodigoYUsuario));

        await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 42);

        logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Information
              && e.Message.Contains("GAS-10")
              && e.Message.Contains("42"),
            "CreateAsync debe loggear quién creó y con qué código");
    }

    [Fact]
    public async Task Robustez146_7_UpdateAsync_LoggeaListaDeCambiosDetectados()
    {
        var (service, _, logger) = NewService(nameof(Robustez146_7_UpdateAsync_LoggeaListaDeCambiosDetectados));
        var creado = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        logger.Entries.Clear(); // descartar logs de Create

        var updateDto = NewUpdateDto(creado);
        updateDto.PrecioActual = 18000m;
        updateDto.Nombre = "Garrafa 10kg v2";

        await service.UpdateAsync(updateDto, usuarioId: 7);

        // Hay DOS logs de Information esperados: el Slice 3 del histórico
        // de precios ("cambió de precio") y el del Issue #146.7 ("actualizado").
        logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Information
              && e.Message.Contains("actualizado")
              && e.Message.Contains("PrecioActual"),
            "UpdateAsync debe loggear campos cambiados, incluyendo precio");
        logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Information
              && e.Message.Contains("Nombre"),
            "UpdateAsync debe loggear el cambio de Nombre");
        logger.Entries.Should().NotContain(
            e => e.Message.Contains("7")
              && !e.Message.Contains("18000")
              && !e.Message.Contains("Nombre")
              && !e.Message.Contains("GAS-10"),
            "no debe haber logs espurios sin contexto");
    }

    [Fact]
    public async Task Robustez146_7_UpdateAsync_SinCambios_NoLoggeaActualizacion()
    {
        // Si el operador reenvía el form sin tocar nada, no debe haber log
        // de "actualizado" — evita spam en producción.
        var (service, _, logger) = NewService(nameof(Robustez146_7_UpdateAsync_SinCambios_NoLoggeaActualizacion));
        var creado = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        logger.Entries.Clear();

        var updateDto = NewUpdateDto(creado); // todos los campos idénticos

        await service.UpdateAsync(updateDto, usuarioId: 1);

        logger.Entries.Should().NotContain(
            e => e.Level == LogLevel.Information && e.Message.Contains("actualizado"),
            "un Update sin cambios no debe emitir log de cambios");
    }

    [Fact]
    public async Task Robustez146_7_DeleteAsync_LoggeaWarningConCodigoYUsuario()
    {
        var (service, _, logger) = NewService(nameof(Robustez146_7_DeleteAsync_LoggeaWarningConCodigoYUsuario));
        var creado = await service.CreateAsync(NewCreateDto("GAS-10"), usuarioId: 1);
        logger.Entries.Clear();

        await service.DeleteAsync(creado.Id, usuarioId: 5);

        logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Warning
              && e.Message.Contains("GAS-10")
              && e.Message.Contains("5"),
            "DeleteAsync debe loggear a nivel Warning con código y usuario");
    }
}
