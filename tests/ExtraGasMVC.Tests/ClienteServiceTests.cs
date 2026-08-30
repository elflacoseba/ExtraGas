using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Exceptions;
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

    /// <summary>
    /// Overload para tests que reciben un <see cref="ClienteDto"/> (lo que
    /// devuelven los metodos del Service) en lugar de la entity.
    /// </summary>
    private static UpdateClienteDto NewUpdateDto(ClienteDto c) => new()
    {
        Id = c.Id,
        Nombre = "Juan Modificado",
        Apellido = c.Apellido,
        Dni = c.Dni,
        TelefonoPrincipal = c.TelefonoPrincipal,
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

    // ====================================================================
    // Issue #108: distinguir cliente inexistente de cliente soft-deleted
    // ====================================================================

    [Fact]
    public async Task UpdateAsync_ClienteNoExiste_LanzaKeyNotFoundException()
    {
        var (service, _) = NewService(nameof(UpdateAsync_ClienteNoExiste_LanzaKeyNotFoundException));

        var updateDto = new UpdateClienteDto
        {
            Id = 999999,
            Nombre = "Fantasma",
            Apellido = "No existe",
            Dni = "00000000",
            TelefonoPrincipal = "0000000000",
        };

        var act = async () => await service.UpdateAsync(updateDto, updatedBy: 1);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999999*");
    }

    [Fact]
    public async Task UpdateAsync_ClienteSoftDeleted_LanzaClienteSoftDeletedException()
    {
        var (service, _) = NewService(nameof(UpdateAsync_ClienteSoftDeleted_LanzaClienteSoftDeletedException));

        // Seed: crear y luego soft-deleted via el metodo del Service (que
        // respeta la BD y setea DeletedAt).
        var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);
        await service.DeleteAsync(creado.Id, updatedBy: 1);

        var updateDto = NewUpdateDto(creado);

        var act = async () => await service.UpdateAsync(updateDto, updatedBy: 1);

        await act.Should().ThrowAsync<ClienteSoftDeletedException>()
            .Where(ex => ex.ClienteId == creado.Id);
    }

    // ====================================================================
    // Issue #111: papelera de clientes soft-deleted
    // ====================================================================

    [Fact]
    public async Task DeleteAsync_Y_RestoreAsync_RoundTrip_DevuelveClienteActivoSinDeletedAt()
    {
        var (service, context) = NewService(nameof(DeleteAsync_Y_RestoreAsync_RoundTrip_DevuelveClienteActivoSinDeletedAt));

        // Arrange: crear un cliente
        var dto = NewCreateDto();
        var creado = await service.CreateAsync(dto, createdBy: 1);

        // Act 1: soft-delete
        var deleted = await service.DeleteAsync(creado.Id, updatedBy: 1);
        deleted.Should().BeTrue();

        // Verificar que el cliente esta soft-deleted en BD
        var entity = await context.Clientes.IgnoreQueryFilters().FirstAsync(c => c.Id == creado.Id);
        entity.DeletedAt.Should().NotBeNull();
        entity.Activo.Should().BeFalse();

        // Act 2: restore
        var restored = await service.RestoreAsync(creado.Id, updatedBy: 1);
        restored.Should().BeTrue();

        // Assert: DeletedAt es null y Activo es true otra vez
        var afterRestore = await context.Clientes.IgnoreQueryFilters().FirstAsync(c => c.Id == creado.Id);
        afterRestore.DeletedAt.Should().BeNull();
        afterRestore.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task GetDeletedAsync_DevuelveSoloSoftDeleted_InclusoSiActivoEsFalse()
    {
        var (service, context) = NewService(nameof(GetDeletedAsync_DevuelveSoloSoftDeleted_InclusoSiActivoEsFalse));

        // Arrange: 2 clientes normales + 1 soft-deleted
        await service.CreateAsync(NewCreateDto("11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("22222222"), createdBy: 1);
        var tercero = await service.CreateAsync(NewCreateDto("33333333"), createdBy: 1);
        await service.DeleteAsync(tercero.Id, updatedBy: 1);

        // Act
        var resultado = await service.GetDeletedAsync(busqueda: null, pagina: 1, tamanio: 25);

        // Assert: solo el tercero aparece, y DeletedAt != null
        resultado.Total.Should().Be(1);
        resultado.Items.Should().HaveCount(1);
        resultado.Items[0].Id.Should().Be(tercero.Id);
    }

    [Fact]
    public async Task GetDeletedAsync_RespetaFiltroDeBusqueda()
    {
        var (service, _) = NewService(nameof(GetDeletedAsync_RespetaFiltroDeBusqueda));

        await service.CreateAsync(NewCreateDto(dni: "11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto(dni: "22222222"), createdBy: 1);
        var tercero = await service.CreateAsync(NewCreateDto(dni: "33333333"), createdBy: 1);
        await service.DeleteAsync(tercero.Id, updatedBy: 1);

        var resultado = await service.GetDeletedAsync(busqueda: "33333333", pagina: 1, tamanio: 25);

        resultado.Total.Should().Be(1);
        resultado.Items[0].Id.Should().Be(tercero.Id);
    }

    // ====================================================================
    // Issue #136 (S6964): UpdateClienteDto.Id es nullable para evitar
    // under-posting silencioso desde forms manipulados. El Controller ya
    // devuelve 400 si id != cliente.Id, pero el Service agrega guard
    // defensivo (ArgumentException) porque también puede invocarse desde
    // tests u otros callers que no pasaron por la validación del
    // Controller. Cubre el branch del null guard.
    // ====================================================================

    [Fact]
    public async Task UpdateAsync_IdNull_LanzaArgumentException()
    {
        var (service, _) = NewService(nameof(UpdateAsync_IdNull_LanzaArgumentException));

        var dto = new UpdateClienteDto
        {
            Id = null,
            Nombre = "Sin Id",
            Apellido = "Invalido",
            Dni = "99999999",
            TelefonoPrincipal = "0000000000",
        };

        var act = async () => await service.UpdateAsync(dto, updatedBy: 1);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Id es obligatorio*");
    }
}