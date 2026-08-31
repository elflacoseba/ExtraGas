using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests.Services;

/// <summary>
/// Tests del <see cref="IAuditLogger"/> (issue #147 slice 2).
///
/// Contrato verificado:
///   - <c>LogChangeAsync</c> agrega una <see cref="AuditLogEntry"/> al change
///     tracker del <see cref="ExtraGasDbContext"/> compartido.
///   - Copia los 7 campos (entidad, registroId, campo, valorAnterior,
///     valorNuevo, userId, changedAt) verbatim.
///   - Acepta <c>valorAnterior</c> y <c>valorNuevo</c> nulos (caso altas /
///     bajas donde uno de los dos lados no aplica).
///   - NO llama <c>SaveChangesAsync</c>: el caller commit junto con su
///     propia mutación para garantizar atomicidad.
///
/// Patrón: DbContext InMemory (mismo que ProductoServiceTests) — no
/// requiere Testcontainers porque no testeamos la migración acá, solo el
/// contrato del logger. El integration test con Testcontainers vive en
/// <see cref="Integration.ProductoAuditLogIntegrationTests"/>.
/// </summary>
public class AuditLoggerTests
{
    private static (AuditLogger logger, ExtraGasDbContext context) NewLogger(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var logger = new AuditLogger(context, NullLogger<AuditLogger>.Instance);
        return (logger, context);
    }

    [Fact]
    public async Task LogChangeAsync_AddsEntryToChangeTracker()
    {
        // Spec scenario: el logger agrega UNA entry al ChangeTracker del
        // DbContext compartido. Como NO llama SaveChanges (atomicidad con
        // el caller), verificamos el side-effect en el ChangeTracker
        // después de invocar.
        var (logger, context) = NewLogger(nameof(LogChangeAsync_AddsEntryToChangeTracker));

        await logger.LogChangeAsync(
            entidad: "Producto",
            registroId: 42,
            campo: "PrecioActual",
            valorAnterior: "1000",
            valorNuevo: "1500",
            changedBy: 7);

        var pending = context.ChangeTracker
            .Entries<AuditLogEntry>()
            .Where(e => e.State == EntityState.Added)
            .ToList();
        pending.Should().ContainSingle(
            "LogChangeAsync debe encolar exactamente una AuditLogEntry en estado Added");
    }

    [Fact]
    public async Task LogChangeAsync_SetsAllRequiredFields()
    {
        // Spec scenario "precio change emits one row": verificar que los
        // 7 campos del entity reflejan los argumentos verbatim. Sin esto
        // un refactor podría silenciosamente tirar campos.
        var (logger, context) = NewLogger(nameof(LogChangeAsync_SetsAllRequiredFields));

        var before = DateTime.UtcNow.AddSeconds(-1);
        await logger.LogChangeAsync(
            entidad: "Producto",
            registroId: 99,
            campo: "PrecioActual",
            valorAnterior: "1000.00",
            valorNuevo: "1500.00",
            changedBy: 7);
        var after = DateTime.UtcNow.AddSeconds(1);

        var entry = context.ChangeTracker.Entries<AuditLogEntry>().Single();
        var entity = entry.Entity;
        entity.Entidad.Should().Be("Producto");
        entity.RegistroId.Should().Be(99UL);
        entity.Campo.Should().Be("PrecioActual");
        entity.ValorAnterior.Should().Be("1000.00");
        entity.ValorNuevo.Should().Be("1500.00");
        entity.UserId.Should().Be(7UL);
        entity.ChangedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after,
            "ChangedAt debe setearse al momento de la llamada (now-ish)");
    }

    [Fact]
    public async Task LogChangeAsync_AcceptsNullPreviousAndNextValues()
    {
        // Caso real: cuando se crea un registro no hay valor anterior
        // (null), y ciertos escenarios (clear de un campo) tienen valor
        // nuevo null. El logger debe aceptarlo sin tirar.
        var (logger, context) = NewLogger(nameof(LogChangeAsync_AcceptsNullPreviousAndNextValues));

        await logger.LogChangeAsync(
            entidad: "Cliente",
            registroId: 1,
            campo: "TelefonoPrincipal",
            valorAnterior: null,   // campo recién creado
            valorNuevo: "+541112345678",
            changedBy: 3);

        var entity = context.ChangeTracker.Entries<AuditLogEntry>().Single().Entity;
        entity.ValorAnterior.Should().BeNull();
        entity.ValorNuevo.Should().Be("+541112345678");
    }

    [Fact]
    public async Task LogChangeAsync_AcceptsNullChangedBy_ForSystemChanges()
    {
        // Backfills, jobs programados y migraciones no tienen un usuario
        // humano detrás. El userId debe ser nullable sin tirar.
        var (logger, context) = NewLogger(nameof(LogChangeAsync_AcceptsNullChangedBy_ForSystemChanges));

        await logger.LogChangeAsync(
            entidad: "Producto",
            registroId: 5,
            campo: "Activo",
            valorAnterior: "false",
            valorNuevo: "true",
            changedBy: null);

        var entity = context.ChangeTracker.Entries<AuditLogEntry>().Single().Entity;
        entity.UserId.Should().BeNull(
            "cambios system-initiated (backfill, jobs) tienen userId null");
    }

    [Fact]
    public async Task LogChangeAsync_DoesNotCallSaveChanges()
    {
        // Contrato atómico: el logger NO persiste, solo encola. El caller
        // hace SaveChanges UNA vez con su propia mutación para garantizar
        // que la fila de audit y la mutación son atómicas. Verificamos
        // que después de LogChangeAsync la entry sigue en Added (no
        // Unchanged, que sería señal de que SaveChanges la vio).
        var (logger, context) = NewLogger(nameof(LogChangeAsync_DoesNotCallSaveChanges));

        await logger.LogChangeAsync("Producto", 1, "Nombre", "V1", "V2", 1);

        context.ChangeTracker.Entries<AuditLogEntry>().Should()
            .ContainSingle(e => e.State == EntityState.Added,
                "la entry debe quedar en Added — el logger NO llama SaveChanges");
    }

    [Fact]
    public async Task LogChangeAsync_SupportsCancellationToken()
    {
        // El interface toma CancellationToken ct = default. Verificamos
        // que el logger lo acepta (no que lo use — es pass-through, pero
        // la firma debe coincidir con la que el caller va a pasar).
        var (logger, context) = NewLogger(nameof(LogChangeAsync_SupportsCancellationToken));
        using var cts = new CancellationTokenSource();

        await logger.LogChangeAsync(
            "Producto", 1, "Nombre", "A", "B", 1, cts.Token);

        context.ChangeTracker.Entries<AuditLogEntry>().Should().HaveCount(1);
    }
}
