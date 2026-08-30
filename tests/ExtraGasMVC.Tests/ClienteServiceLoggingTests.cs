using System.Reflection;
using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del fix para la issue #116: trazabilidad de <see cref="ClienteService"/>
/// via <see cref="ILogger{T}"/>. Cubre las cuatro operaciones de escritura y los
/// dos paths de error (DNI duplicado por pre-check, race condition con errno 1062
/// y DbUpdateException no esperada).
///
/// Se usa un spy <see cref="TestLogger{T}"/> para capturar las entradas; no hay
/// Moq en el proyecto y <see cref="NullLogger{T}"/> no nos sirve porque queremos
/// asertar que se loggea.
/// </summary>
public class ClienteServiceLoggingTests
{
    // ====================================================================
    // Tests: Information en operaciones exitosas
    // ====================================================================

    [Fact]
    public async Task CreateAsync_Exitoso_LoggeaInformationConClienteId()
    {
        var (service, _, logger) = NewService(nameof(CreateAsync_Exitoso_LoggeaInformationConClienteId));

        var creado = await service.CreateAsync(NewCreateDto("12345678"), createdBy: 42);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("creado")
            && e.Message.Contains(creado.Id.ToString()));
    }

    [Fact]
    public async Task UpdateAsync_Exitoso_LoggeaInformationConClienteId()
    {
        var (service, _, logger) = NewService(nameof(UpdateAsync_Exitoso_LoggeaInformationConClienteId));
        var creado = await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        await service.UpdateAsync(NewUpdateDto(creado.Id, "12345678"), updatedBy: 7);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("actualizado")
            && e.Message.Contains(creado.Id.ToString()));
    }

    [Fact]
    public async Task DeleteAsync_Exitoso_LoggeaInformationConClienteId()
    {
        var (service, _, logger) = NewService(nameof(DeleteAsync_Exitoso_LoggeaInformationConClienteId));
        var creado = await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        await service.DeleteAsync(creado.Id, updatedBy: 3);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("soft-deleted")
            && e.Message.Contains(creado.Id.ToString()));
    }

    [Fact]
    public async Task DeleteAsync_NoEncontrado_NoLoggea()
    {
        // No loggeamos el "no encontrado" porque es flujo esperado (404 de la
        // papelera cuando el operador hace doble click), no requiere investigación.
        var (service, _, logger) = NewService(nameof(DeleteAsync_NoEncontrado_NoLoggea));

        var ok = await service.DeleteAsync(999999, updatedBy: 1);

        ok.Should().BeFalse();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreAsync_Exitoso_LoggeaInformationConClienteId()
    {
        var (service, _, logger) = NewService(nameof(RestoreAsync_Exitoso_LoggeaInformationConClienteId));
        var creado = await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);
        await service.DeleteAsync(creado.Id, updatedBy: 1);

        logger.Entries.Clear();

        await service.RestoreAsync(creado.Id, updatedBy: 5);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("reactivado")
            && e.Message.Contains(creado.Id.ToString()));
    }

    [Fact]
    public async Task RestoreAsync_NoEncontrado_NoLoggea()
    {
        var (service, _, logger) = NewService(nameof(RestoreAsync_NoEncontrado_NoLoggea));

        var ok = await service.RestoreAsync(999999, updatedBy: 1);

        ok.Should().BeFalse();
        logger.Entries.Should().BeEmpty();
    }

    // ====================================================================
    // Tests: Warning en DNI duplicado (pre-check)
    // ====================================================================

    [Fact]
    public async Task CreateAsync_DniDuplicado_PreCheck_LoggeaWarningAntesDeThrow()
    {
        var (service, _, logger) = NewService(nameof(CreateAsync_DniDuplicado_PreCheck_LoggeaWarningAntesDeThrow));
        await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        var act = async () => await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("DNI duplicado")
            && e.Message.Contains("12345678"));
    }

    [Fact]
    public async Task UpdateAsync_DniDuplicado_PreCheck_LoggeaWarningAntesDeThrow()
    {
        var (service, _, logger) = NewService(nameof(UpdateAsync_DniDuplicado_PreCheck_LoggeaWarningAntesDeThrow));
        var primero = await service.CreateAsync(NewCreateDto("11111111"), createdBy: 1);
        var segundo = await service.CreateAsync(NewCreateDto("22222222"), createdBy: 1);

        // Intento pisar el segundo con el DNI del primero.
        var act = async () => await service.UpdateAsync(
            NewUpdateDto(segundo.Id, "11111111"), updatedBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("DNI duplicado")
            && e.Message.Contains("11111111")
            && e.Message.Contains(segundo.Id.ToString())
            && e.Message.Contains("UpdateAsync"));
    }

    // ====================================================================
    // Tests: Warning en race condition (errno 1062) y Error en DbUpdateException
    // no esperada. Necesitamos un DbContext que tire al SaveChangesAsync;
    // InMemory no enforce UNIQUE constraints.
    // ====================================================================

    [Fact]
    public async Task CreateAsync_RaceConditionDniDuplicado_LoggeaWarningAntesDeThrow()
    {
        var dbName = nameof(CreateAsync_RaceConditionDniDuplicado_LoggeaWarningAntesDeThrow);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new FakeDbContext(options)
        {
            ToThrowOnSave = BuildDuplicateDniDbUpdateException()
        };
        var logger = new TestLogger<ClienteService>();
        var service = NewServiceWithLogger(context, logger);

        var act = async () => await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("El DNI ingresado ya está registrado.");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("Race condition")
            && e.Exception != null);
    }

    [Fact]
    public async Task CreateAsync_DbUpdateExceptionNoEsperada_LoggeaErrorYReThrow()
    {
        // Una DbUpdateException sin MySqlException 1062 adentro: cae en el path
        // de error no esperado. El Service debe loggear Error y re-throw para
        // que el caller decida qué hacer.
        var dbName = nameof(CreateAsync_DbUpdateExceptionNoEsperada_LoggeaErrorYReThrow);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new FakeDbContext(options)
        {
            ToThrowOnSave = new DbUpdateException("Fallo de BD cualquiera")
        };
        var logger = new TestLogger<ClienteService>();
        var service = NewServiceWithLogger(context, logger);

        var act = async () => await service.CreateAsync(NewCreateDto("12345678"), createdBy: 1);

        await act.Should().ThrowAsync<DbUpdateException>();
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Error
            && e.Message.Contains("no esperada")
            && e.Exception is DbUpdateException);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>
    /// Crea un <see cref="MySqlException"/> con errno 1062 (duplicate entry)
    /// via reflection. MySqlConnector tiene todos sus ctors internal; usamos
    /// el patron de <c>ClienteServiceDniRaceConditionTests</c> que ya esta
    /// probado contra esta version de la libreria.
    /// </summary>
    private static DbUpdateException BuildDuplicateDniDbUpdateException()
    {
        var errorCode = MySqlErrorCode.DuplicateKeyEntry;
        var ctor = typeof(MySqlException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(MySqlErrorCode), typeof(string) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                "MySqlException(MySqlErrorCode, string) ctor not found; " +
                "MySqlConnector cambió su API interna. Actualizar este helper.");
        var mySqlEx = (MySqlException)ctor.Invoke(new object[] { errorCode, "Duplicate entry" });
        return new DbUpdateException("Error de BD", mySqlEx);
    }

    private static (ClienteService service, ExtraGasDbContext context, TestLogger<ClienteService> logger) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var logger = new TestLogger<ClienteService>();
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new ClienteService(context, mapper, cache, logger);
        return (service, context, logger);
    }

    private static ClienteService NewService(ExtraGasDbContext context)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        return new ClienteService(context, mapper, cache, NullLogger<ClienteService>.Instance);
    }

    private static ClienteService NewServiceWithLogger(
        ExtraGasDbContext context, ILogger<ClienteService> logger)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        return new ClienteService(context, mapper, cache, logger);
    }

    private static CreateClienteDto NewCreateDto(string dni) => new()
    {
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = dni,
        TelefonoPrincipal = "1144556677",
    };

    private static UpdateClienteDto NewUpdateDto(ulong id, string newDni) => new()
    {
        Id = id,
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = newDni,
        TelefonoPrincipal = "1144556677",
    };

    /// <summary>
    /// DbContext que tira una <see cref="DbUpdateException"/> controlada en
    /// <c>SaveChangesAsync</c>. InMemory no enforce UNIQUE constraints, así que
    /// sin esto no podemos probar el path de race condition ni el de error
    /// no esperado a nivel Unit.
    /// </summary>
    private sealed class FakeDbContext : ExtraGasDbContext
    {
        public DbUpdateException? ToThrowOnSave { get; set; }

        public FakeDbContext(DbContextOptions<ExtraGasDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            if (ToThrowOnSave is not null) throw ToThrowOnSave;
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }
}
