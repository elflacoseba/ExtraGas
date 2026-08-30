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
/// Tests de integracion del Service de Cliente contra DbContext InMemory.
/// Cubren las lineas nuevas introducidas por el issue #114:
/// - CreateAsync setea Activo=true y FechaAlta=hoy (no del DTO)
/// - UpdateAsync preserva Activo y FechaAlta desde la BD (defense-in-depth)
///
/// Los helpers estaticos (ClienteEditRules) tienen tests dedicados en
/// <see cref="ClienteEditRulesTests"/>; aca se valida la integracion
/// end-to-end: que el Service realmente llame al helper y persista el
/// resultado correcto.
/// </summary>
public class ClienteServiceTests
{
    /// <summary>
    /// Crea un DbContext fresco sobre InMemoryDatabase (unico por test) y
    /// siembra un Cliente base. InMemory no soporta transacciones ni triggers
    /// pero alcanza para ejercitar las lineas nuevas del Service.
    /// </summary>
    private static (ClienteService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new ClienteService(context, mapper, cache);
        return (service, context);
    }

    private static CreateClienteDto NewCreateDto(string dni = "12345678") => new()
    {
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = dni,
        TelefonoPrincipal = "1144556677",
    };

    private static UpdateClienteDto NewUpdateDto(Cliente c) => new()
    {
        Id = c.Id,
        Nombre = "Juan Modificado",
        Apellido = c.Apellido,
        Dni = c.Dni,
        TelefonoPrincipal = c.TelefonoPrincipal,
        // Sin Activo ni FechaAlta: el DTO ya no los expone.
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrueYFechaAltaHoy_AunqueDtoNoLosTenga()
    {
        var (service, context) = NewService(nameof(CreateAsync_SeteaActivoTrueYFechaAltaHoy_AunqueDtoNoLosTenga));
        var dto = NewCreateDto();

        var antes = DateOnly.FromDateTime(DateTime.UtcNow);
var creado = await service.CreateAsync(dto, createdBy: 1);
        var despues = DateOnly.FromDateTime(DateTime.UtcNow);

        creado.Activo.Should().BeTrue();
        creado.FechaAlta.Should().BeOnOrAfter(antes).And.BeOnOrBefore(despues);
    }

    [Fact]
    public async Task CreateAsync_NoRespetaActivoFalseNiFechaAltaPasada_DelDto()
    {
        // El operador podria mandar Activo=false y FechaAlta retroactiva si el
        // DTO los expusiera. Verifica que el Service los ignora y setea los
        // valores correctos. (Ya no se puede mandar por el DTO, pero este
        // test documenta la garantia a nivel Service.)
        var (service, _) = NewService(nameof(CreateAsync_NoRespetaActivoFalseNiFechaAltaPasada_DelDto));
        var dto = new CreateClienteDto
        {
            Nombre = "Juan", Apellido = "Perez", Dni = "11111111",
            TelefonoPrincipal = "1144556677",
            // Activo y FechaAlta no existen en el DTO: el compilador no
            // permite setearlos. Lo que verificamos es que el Service pone
            // sus defaults independientemente del resto del DTO.
        };

var creado = await service.CreateAsync(dto, createdBy: 1);

        creado.Activo.Should().BeTrue();
        creado.FechaAlta.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(0),
            "FechaAlta debe ser hoy, no un valor retroactivo que el DTO pueda cargar");
    }

    [Fact]
    public async Task UpdateAsync_PreservaActivoYFechaAlta_DesdeLaBD_AunqueDtoNoLosTenga()
    {
        // Cliente creado con Activo=true, FechaAlta=hoy. El operador intenta
        // "desactivarlo" mandando DTO con Activo=false — el DTO ya no lo
        // expone, pero defense-in-depth: el Service preserva desde la BD.
        var (service, context) = NewService(nameof(UpdateAsync_PreservaActivoYFechaAlta_DesdeLaBD_AunqueDtoNoLosTenga));
var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);
        var fechaAltaOriginal = creado.FechaAlta;

        var updateDto = new UpdateClienteDto
        {
            Id = creado.Id,
            Nombre = "Juan Modificado",
            Apellido = creado.Apellido,
            Dni = creado.Dni,
            TelefonoPrincipal = creado.TelefonoPrincipal,
        };
        // Activo y FechaAlta NO estan en UpdateClienteDto.

        var actualizado = await service.UpdateAsync(updateDto, updatedBy: 2);

        actualizado.Activo.Should().BeTrue("Activo debe preservarse desde la BD aunque el DTO no lo traiga");
        actualizado.FechaAlta.Should().Be(fechaAltaOriginal,
            "FechaAlta debe preservarse desde la BD aunque el DTO no la traiga");
actualizado.Nombre.Should().Be("Juan Modificado", "el resto de los campos si se actualizan");
    }
}