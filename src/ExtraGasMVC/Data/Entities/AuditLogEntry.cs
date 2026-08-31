namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Una fila por cambio de campo en cualquier entidad del sistema. La tabla
/// <c>audit_log</c> es append-only: no tiene <c>created_by</c>,
/// <c>updated_by</c> ni <c>deleted_at</c> — la tabla misma es la auditoría.
///
/// <para>Issue #147 slice 2: el <see cref="IAuditLogger"/> emite una fila
/// por cada campo modificado cuando un Service hace UPDATE. El
/// <c>SaveChangesAsync</c> del caller commit las filas en la misma
/// transacción que la mutación de la entidad auditada — todo o nada.</para>
///
/// <para><b>Sin FK a la entidad auditada ni a <c>usuarios</c></b>: el log
/// debe sobrevivir la baja del registro fuente y la baja del usuario que
/// hizo el cambio. La integridad referencial lógica se delega al caller
/// (<c>IAuditLogger</c> usa los IDs como referencia, no como constraint).</para>
/// </summary>
public class AuditLogEntry
{
    public ulong Id { get; set; }

    /// <summary>
    /// Tipo de entidad auditada (ej. <c>"Producto"</c>, <c>"Cliente"</c>).
    /// Es un discriminador string — no FK, para que agregar nuevas entidades
    /// no requiera migrar esta tabla.
    /// </summary>
    public string Entidad { get; set; } = null!;

    /// <summary>
    /// Id del registro en la tabla fuente (mismo tipo que la PK de la tabla
    /// auditada). <c>BIGINT UNSIGNED</c> en BD para matchear la convención
    /// del resto del schema.
    /// </summary>
    public ulong RegistroId { get; set; }

    /// <summary>
    /// Nombre del campo modificado (ej. <c>"PrecioActual"</c>). String, no FK
    /// a metadatos de la entidad: el contrato es que el caller sabe el
    /// nombre del campo y lo emite verbatim.
    /// </summary>
    public string Campo { get; set; } = null!;

    /// <summary>
    /// Valor anterior serializado como string. <c>NULL</c> para altas
    /// (no había valor previo) o cuando la representación textual no aplica.
    /// </summary>
    public string? ValorAnterior { get; set; }

    /// <summary>
    /// Valor nuevo serializado como string. <c>NULL</c> para bajas
    /// (campo eliminado) o cuando no aplica representación textual.
    /// </summary>
    public string? ValorNuevo { get; set; }

    /// <summary>
    /// <c>usuarios.id</c> del operador que hizo el cambio. <c>NULL</c> para
    /// cambios system-initiated (backfills, jobs, migraciones). No es FK en
    /// BD — el log debe sobrevivir la baja del usuario.
    /// </summary>
    public ulong? UserId { get; set; }

    /// <summary>
    /// Timestamp del cambio. <c>DEFAULT CURRENT_TIMESTAMP</c> en BD; el caller
    /// puede setearlo explícitamente cuando necesita un instante distinto al
    /// del INSERT (ej. tests con reloj fijo).
    /// </summary>
    public DateTime ChangedAt { get; set; }
}
