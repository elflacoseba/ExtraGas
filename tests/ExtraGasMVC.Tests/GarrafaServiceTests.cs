using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Garrafa contra DbContext InMemory.
/// Foco: CreateAsync setea Activo=true y UpdateAsync preserva Activo via
/// <see cref="GarrafaEditRules"/>. Garrafa tiene Activo (soft-delete) y
/// estado_garrafa_id (operacional) ortogonales.
/// </summary>
public class GarrafaServiceTests
{
    private static GarrafaService NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        return new GarrafaService(context, mapper, NullLogger<GarrafaService>.Instance);
    }

    private static CreateGarrafaDto NewCreateDto(string codigo = "GAR-001") => new()
    {
        Codigo = codigo,
        CapacidadKg = 10,
        FechaCompra = new DateOnly(2024, 1, 15),
        EstadoGarrafaId = 1,
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var service = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    [Fact]
    public async Task UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga()
    {
        var service = NewService(nameof(UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = new UpdateGarrafaDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            CapacidadKg = creado.CapacidadKg,
            FechaCompra = creado.FechaCompra,
            EstadoGarrafaId = creado.EstadoGarrafaId,
            // Activo NO esta en UpdateGarrafaDto.
        };
        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 2);

        actualizado.Activo.Should().BeTrue(
            "el helper GarrafaEditRules debe preservar Activo desde la BD");
    }
}