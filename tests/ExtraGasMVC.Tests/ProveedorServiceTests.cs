using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Proveedor contra DbContext InMemory.
/// Foco: CreateAsync setea Activo=true (PR #123 cierra la inconsistencia
/// del DTO; el Service ya preservaba Activo via ProveedorEditRules).
/// </summary>
public class ProveedorServiceTests
{
    private static ProveedorService NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ProveedorService(context, mapper, cache);
    }

    private static CreateProveedorDto NewCreateDto(string cuit = "20123456789") => new()
    {
        RazonSocial = "Proveedor Test",
        Cuit = cuit,
        TelefonoPrincipal = "1144556677",
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var service = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);

        creado.Activo.Should().BeTrue(
            "Activo no viene del DTO desde PR #123; el Service lo setea en true");
    }
}