namespace ExtraGasMVC.Services.Interfaces;

/// <summary>
/// Emite filas a la tabla <c>audit_log</c> para registrar cambios
/// field-level sobre entidades del dominio.
///
/// <para>Issue #147 slice 2 — contrato de uso:</para>
/// <list type="bullet">
///   <item><b>Una llamada por campo modificado</b>. El caller itera sobre
///   los campos que difieren y emite un <c>LogChangeAsync</c> por cada
///   uno. Más simple que un batch overload y trazable 1:1 al diff.</item>
///   <item><b>NO llama <c>SaveChangesAsync</c></b>. La entry se agrega al
///   change tracker del <c>ExtraGasDbContext</c> compartido (la misma
///   instancia Scoped que el Service que invoca). El <c>SaveChangesAsync</c>
///   del caller persiste la entry de audit JUNTO con su propia mutación en
///   la misma transacción — todo o nada. Si la mutación falla, no queda
///   fila huérfana en <c>audit_log</c>.</item>
///   <item><b>Failure handling</b>: si la inserción en <c>audit_log</c>
///   fallara por una razón no controlada (constraint rara, schema drift),
///   el catch interno loggea y traga. La auditoría es un side-effect, no
///   debe bloquear la operación de negocio.</item>
/// </list>
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Encola una fila en el change tracker describiendo el cambio de un
    /// campo. NO persiste — el caller hace <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="entidad">Tipo de entidad auditada (ej. <c>"Producto"</c>).</param>
    /// <param name="registroId">Id del registro en la tabla fuente.</param>
    /// <param name="campo">Nombre del campo modificado (ej. <c>"PrecioActual"</c>).</param>
    /// <param name="valorAnterior">Valor viejo serializado a string. <c>null</c> para altas.</param>
    /// <param name="valorNuevo">Valor nuevo serializado a string. <c>null</c> cuando no aplica.</param>
    /// <param name="changedBy"><c>usuarios.id</c> del operador, o <c>null</c> para cambios del sistema.</param>
    /// <param name="ct">Token de cancelación (forwarded al caller vía SaveChangesAsync).</param>
    Task LogChangeAsync(
        string entidad,
        long registroId,
        string campo,
        string? valorAnterior,
        string? valorNuevo,
        long? changedBy,
        CancellationToken ct = default);
}
