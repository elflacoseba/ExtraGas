using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresion de los métodos de lectura de <see cref="ClienteService"/>.
/// Issue #112: GetByIdAsync / GetAllAsync / GetByDniAsync / GetActivosAsync no
/// tenían cobertura dedicada. Estos tests blindan:
///   - Devolución de null cuando el cliente no existe (no lanzar excepción).
///   - Respeto del QueryFilter global (soft-deleted oculto en GetBy* y GetActivos).
///   - Orden estable (Apellido, Nombre) en GetAllAsync y GetActivosAsync.
///
/// GetDeletedAsync ya tiene cobertura en <see cref="ClienteServiceTests"/>
/// (Issue #111); no se duplica acá.
///
/// Issue #115: el flag `Activo` se eliminó de la entity. Los tests verifican
/// el estado del cliente vía <c>DeletedAt</c> (única fuente de verdad).
/// </summary>
public class ClienteServiceReadTests
{
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

    private static CreateClienteDto NewCreateDto(string nombre, string apellido, string dni) => new()
    {
        Nombre = nombre,
        Apellido = apellido,
        Dni = dni,
        TelefonoPrincipal = "1144556677",
    };

    // ====================================================================
    // GetByIdAsync
    // ====================================================================

    [Fact]
    public async Task GetByIdAsync_DevuelveDto_CuandoClienteExiste()
    {
        var dbName = nameof(GetByIdAsync_DevuelveDto_CuandoClienteExiste);
        var (service, _) = NewService(dbName);

        var creado = await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111"), createdBy: 1);

        var resultado = await service.GetByIdAsync(creado.Id);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(creado.Id);
        resultado.Nombre.Should().Be("Juan");
        resultado.Apellido.Should().Be("Perez");
    }

    [Fact]
    public async Task GetByIdAsync_DevuelveNull_CuandoClienteNoExiste()
    {
        var dbName = nameof(GetByIdAsync_DevuelveNull_CuandoClienteNoExiste);
        var (service, _) = NewService(dbName);

        var resultado = await service.GetByIdAsync(999999);

        resultado.Should().BeNull("GetByIdAsync es lookup: si no existe, devuelve null, no tira excepción");
    }

    [Fact]
    public async Task GetByIdAsync_DevuelveNull_CuandoClienteSoftDeleted_PorQueryFilter()
    {
        // El QueryFilter global oculta los soft-deleted; GetByIdAsync hereda
        // ese comportamiento. Si alguien refactoriza y mete un IgnoreQueryFilters
        // por error, este test rompe.
        var dbName = nameof(GetByIdAsync_DevuelveNull_CuandoClienteSoftDeleted_PorQueryFilter);
        var (service, _) = NewService(dbName);

        var creado = await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111"), createdBy: 1);
        await service.DeleteAsync(creado.Id, updatedBy: 1);

        var resultado = await service.GetByIdAsync(creado.Id);

        resultado.Should().BeNull("un cliente soft-deleted debe ser invisible para GetByIdAsync");
    }

    // ====================================================================
    // GetAllAsync
    // ====================================================================

    [Fact]
    public async Task GetAllAsync_DevuelveTodosOrdenadosPorApellidoYNombre()
    {
        var dbName = nameof(GetAllAsync_DevuelveTodosOrdenadosPorApellidoYNombre);
        var (service, _) = NewService(dbName);

        // Siembra desordenada a propósito: B / A / C.
        await service.CreateAsync(NewCreateDto("Beatriz", "Zapata", "11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Alvarez", "22222222"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Carlos", "Brown", "33333333"), createdBy: 1);

        var todos = await service.GetAllAsync();

        todos.Select(c => c.Apellido).Should().Equal(new[] { "Alvarez", "Brown", "Zapata" },
            "GetAllAsync debe ordenar por Apellido ascendente para que la lista sea estable");
    }

    [Fact]
    public async Task GetAllAsync_NoIncluyeSoftDeleted_PorQueryFilter()
    {
        var dbName = nameof(GetAllAsync_NoIncluyeSoftDeleted_PorQueryFilter);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Ana", "Alvarez", "11111111"), createdBy: 1);
        var soft = await service.CreateAsync(NewCreateDto("Luis", "Lopez", "22222222"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Carlos", "Brown", "33333333"), createdBy: 1);
        await service.DeleteAsync(soft.Id, updatedBy: 1);

        var todos = await service.GetAllAsync();

        todos.Should().HaveCount(2);
        todos.Select(c => c.Id).Should().NotContain(soft.Id);
    }

    // ====================================================================
    // GetByDniAsync
    // ====================================================================

    [Fact]
    public async Task GetByDniAsync_DevuelveDto_CuandoDniCoincide()
    {
        var dbName = nameof(GetByDniAsync_DevuelveDto_CuandoDniCoincide);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "12345678"), createdBy: 1);

        var resultado = await service.GetByDniAsync("12345678");

        resultado.Should().NotBeNull();
        resultado!.Nombre.Should().Be("Juan");
    }

    [Fact]
    public async Task GetByDniAsync_DevuelveNull_CuandoDniNoCoincide()
    {
        var dbName = nameof(GetByDniAsync_DevuelveNull_CuandoDniNoCoincide);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "12345678"), createdBy: 1);

        var resultado = await service.GetByDniAsync("99999999");

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task GetByDniAsync_DevuelveNull_CuandoClienteSoftDeleted_PorQueryFilter()
    {
        // La búsqueda por DNI debe respetar el QueryFilter: si el único
        // cliente con ese DNI está soft-deleted, el operador no lo encuentra
        // (consistente con el resto de las APIs de lectura).
        var dbName = nameof(GetByDniAsync_DevuelveNull_CuandoClienteSoftDeleted_PorQueryFilter);
        var (service, _) = NewService(dbName);

        var creado = await service.CreateAsync(NewCreateDto("Juan", "Perez", "12345678"), createdBy: 1);
        await service.DeleteAsync(creado.Id, updatedBy: 1);

        var resultado = await service.GetByDniAsync("12345678");

        resultado.Should().BeNull();
    }

    // ====================================================================
    // GetActivosAsync
    // ====================================================================

    [Fact]
    public async Task GetActivosAsync_DevuelveSoloClientesSinDeletedAt()
    {
        // Issue #115: el flag `Activo` desapareció. El método devuelve los
        // clientes que el QueryFilter global considera "visibles"
        // (DeletedAt IS NULL). Verificamos que un cliente soft-deleted vía
        // DeleteAsync queda fuera del listado. El escenario "zombie"
        // (Activo=false con DeletedAt=null) ya no es posible a nivel código
        // ni a nivel BD: la columna activo se eliminó.
        var dbName = nameof(GetActivosAsync_DevuelveSoloClientesSinDeletedAt);
        var (service, context) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Gomez", "22222222"), createdBy: 1);

        // Cliente soft-deleted vía el método del Service (que respeta la BD
        // y setea DeletedAt). GetActivosAsync NO debe traerlo.
        var softDeleted = await service.CreateAsync(NewCreateDto("Zombie", "ZombieAp", "99999999"), createdBy: 1);
        await service.DeleteAsync(softDeleted.Id, updatedBy: 1);

        var activos = await service.GetActivosAsync();

        activos.Should().HaveCount(2);
        activos.Select(c => c.Id).Should().NotContain(softDeleted.Id);
    }

    [Fact]
    public async Task GetActivosAsync_OrdenadosPorApellidoYNombre()
    {
        var dbName = nameof(GetActivosAsync_OrdenadosPorApellidoYNombre);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Beatriz", "Zapata", "11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Alvarez", "22222222"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Carlos", "Brown", "33333333"), createdBy: 1);

        var activos = await service.GetActivosAsync();

        activos.Select(c => c.Apellido).Should().Equal("Alvarez", "Brown", "Zapata");
    }
}