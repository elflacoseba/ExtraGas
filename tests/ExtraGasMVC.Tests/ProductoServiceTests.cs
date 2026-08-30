using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Producto contra DbContext InMemory.
/// Cubren las lineas nuevas del issue #114 + el refactor del DeleteAsync
/// (PR #121) que ahora hace soft-delete completo (DeletedAt + Activo=false).
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
        return (new ProductoService(context, mapper), context);
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
}