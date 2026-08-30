using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Producto contra DbContext InMemory.
/// Cubren las lineas nuevas del issue #114 + el refactor del DeleteAsync
/// (PR #121) que ahora hace soft-delete completo (DeletedAt + Activo=false)
/// + RestoreAsync de Slice 2 (issue #145).
/// </summary>
public class ProductoServiceTests
{
    private static (ProductoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        // Issue #145 Slice 2: ILogger<ProductoService> requerido para trazabilidad
        // de operaciones de escritura (RestoreAsync). Los tests existentes no
        // asertan sobre el log; usamos NullLogger.
        return (new ProductoService(context, mapper, NullLogger<ProductoService>.Instance), context);
    }

    private static CreateProductoDto NewCreateDto(string codigo = "GAS-10") => new()
    {
        Codigo = codigo,
        Nombre = "Garrafa 10kg",
        TipoProductoId = 1,
        UnidadVenta = "UNIDAD",
        PrecioActual = 15000m,
        ManejaGarrafaIndividual = true,
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    [Fact]
    public async Task UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = new UpdateProductoDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            Nombre = "Garrafa 10kg v2",
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = creado.PrecioActual,
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
            // Activo NO esta en UpdateProductoDto.
        };
        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 2);

        actualizado.Activo.Should().BeTrue(
            "el helper ProductoEditRules debe preservar Activo desde la BD");
        actualizado.Nombre.Should().Be("Garrafa 10kg v2");
    }

    [Fact]
    public async Task DeleteAsync_SeteaDeletedAtYActivoFalse_SoftDeleteCompleto()
    {
        // PR #121: antes DeleteAsync solo seteaba DeletedAt, dejando Activo=true
        // (un zombie). Ahora setea ambos para mantener la invariante.
        var (service, context) = NewService(nameof(DeleteAsync_SeteaDeletedAtYActivoFalse_SoftDeleteCompleto));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var ok = await service.DeleteAsync(creado.Id, ct: default);

        ok.Should().BeTrue();
        var entity = await context.Productos.IgnoreQueryFilters().FirstAsync(p => p.Id == creado.Id);
        entity.DeletedAt.Should().NotBeNull("soft-delete debe setear DeletedAt");
        entity.Activo.Should().BeFalse("soft-delete debe setear Activo=false (PR #121)");
    }

    // ====================================================================
    // Issue #145 Slice 2: RestoreAsync para revertir soft-delete
    // ====================================================================

    [Fact]
    public async Task RestoreAsync_ReactivatesSoftDeletedProducto()
    {
        // Soft-delete deja DeletedAt != null y Activo = false.
        // Restore debe volver ambos a su estado original (invariante
        // Activo=false => DeletedAt != null de #114/#121).
        var (service, context) = NewService(nameof(RestoreAsync_ReactivatesSoftDeletedProducto));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        await service.DeleteAsync(creado.Id, ct: default);

        var ok = await service.RestoreAsync(creado.Id, updatedBy: 99);

        ok.Should().BeTrue();
        var entity = await context.Productos.IgnoreQueryFilters().FirstAsync(p => p.Id == creado.Id);
        entity.DeletedAt.Should().BeNull("Restore debe limpiar DeletedAt");
        entity.Activo.Should().BeTrue("Restore debe reactivar Activo (Producto retiene la columna por #114)");
        entity.UpdatedBy.Should().Be(99, "Restore debe registrar quién lo reactivó");
    }

    [Fact]
    public async Task RestoreAsync_OnAlreadyActive_ReturnsFalse()
    {
        // Tarea 2.1 (tasks.md): producto activo (DeletedAt == null) no debe
        // ser "restaurado" — devolver false para que el Controller mapee
        // TempData[Error]. Patrón tomado de PedidoService.RestoreAsync.
        var (service, _) = NewService(nameof(RestoreAsync_OnAlreadyActive_ReturnsFalse));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var ok = await service.RestoreAsync(creado.Id, updatedBy: 1);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_OnNonExistent_ReturnsFalse()
    {
        var (service, _) = NewService(nameof(RestoreAsync_OnNonExistent_ReturnsFalse));

        var ok = await service.RestoreAsync(999_999UL, updatedBy: 1);

        ok.Should().BeFalse();
    }
}