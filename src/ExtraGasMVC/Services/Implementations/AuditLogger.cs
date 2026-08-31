using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExtraGasMVC.Services.Implementations;

/// <summary>
/// Implementación Scoped de <see cref="IAuditLogger"/> (issue #147 slice 2).
/// Comparte el <see cref="ExtraGasDbContext"/> con el Service que invoca
/// (Scoped lifetime = misma instancia por request), por lo que las entries
/// se acumulan en el change tracker y se persisten junto con la mutación
/// del caller — atómico.
///
/// <para><b>Por qué NO llama SaveChangesAsync</b>: el contrato del interface
/// es que el logger solo encola. El caller hace un solo SaveChanges
/// (ProductoService.UpdateAsync → sus cambios de producto + N filas de
/// audit_log). Si el commit falla, no queda fila huérfana de audit ni un
/// cambio de producto sin registrar. Si lo inverso (logger persistiera),
/// un crash entre el SaveChanges del logger y el del caller dejaría la
/// auditoría sin la mutación que documenta — peor para investigación.</para>
///
/// <para><b>Failure handling</b>: si EF rechaza el <c>Add</c> (caso muy raro
/// porque no validamos constraints — el log no tiene FKs), el catch loggea
/// y traga. La operación de negocio no debe fallar por un side-effect de
/// auditoría. Mismo patrón que <see cref="AuditoriaLoginService.RecordAsync"/>.</para>
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ExtraGasDbContext _context;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ExtraGasDbContext context, ILogger<AuditLogger> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogChangeAsync(
        string entidad,
        long registroId,
        string campo,
        string? valorAnterior,
        string? valorNuevo,
        long? changedBy,
        CancellationToken ct = default)
    {
        try
        {
            var entry = new AuditLogEntry
            {
                Entidad = entidad,
                RegistroId = registroId < 0 ? 0UL : (ulong)registroId,
                Campo = campo,
                ValorAnterior = valorAnterior,
                ValorNuevo = valorNuevo,
                UserId = changedBy.HasValue ? (changedBy.Value < 0 ? 0UL : (ulong)changedBy.Value) : (ulong?)null,
                ChangedAt = DateTime.UtcNow,
            };

            // Add es sync (no hay IO en InMemory; en MySQL el INSERT
            // ocurriría en el SaveChanges del caller). La firma devuelve
            // Task para homogeneidad con la familia de IAuditLogger.
            _context.AuditLog.Add(entry);
        }
        catch (Exception ex)
        {
            // El log nunca debe romper una operación de negocio. Loggeamos
            // y seguimos — la mutación del caller commit sin la fila de
            // audit. Si el problema es estructural (schema drift), esto
            // aparecería en todos los UPDATEs y sería visible en el
            // monitoring.
            _logger.LogWarning(ex,
                "AuditLogger: no se pudo encolar entry para {Entidad}#{RegistroId}.{Campo}",
                entidad, registroId, campo);
        }

        // ct es forward al caller vía SaveChangesAsync — el logger no
        // hace IO asíncrono propio. Await Task.CompletedTask satisface
        // la firma sin agregar overhead.
        await Task.CompletedTask;
    }
}
