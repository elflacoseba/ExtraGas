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
/// Tests de integracion del Service de Empleado contra DbContext InMemory.
/// Cubren las lineas nuevas del issue #114: CreateAsync setea Activo=true,
/// UpdateAsync preserva Activo desde la BD via <see cref="EmpleadoEditRules"/>.
/// Los tests del helper estatico viven en <see cref="EmpleadoEditRulesTests"/>.
/// </summary>
public class EmpleadoServiceTests
{
    private static (EmpleadoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        return (new EmpleadoService(context, mapper), context);
    }

    private static CreateEmpleadoDto NewCreateDto(string dni = "12345678") => new()
    {
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = dni,
        FechaIngreso = new DateOnly(2024, 6, 1),
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    [Fact]
    public async Task UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga));
        var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);

        var updateDto = new UpdateEmpleadoDto
        {
            Id = creado.Id,
            Nombre = "Juan Modificado",
            Apellido = creado.Apellido,
            Dni = creado.Dni,
            Telefono = creado.Telefono,
            // Activo NO esta en UpdateEmpleadoDto.
        };
        var actualizado = await service.UpdateAsync(updateDto, updatedBy: 2);

        actualizado.Activo.Should().BeTrue(
            "el helper EmpleadoEditRules debe preservar Activo desde la BD");
        actualizado.Nombre.Should().Be("Juan Modificado");
    }
}