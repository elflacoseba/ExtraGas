using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

/// <summary>
/// Business operations for recepciones de proveedor (issue #45).
/// All write paths are transactional: any failure rolls back every insert
/// (recepcion, items, garrafas, movimientos) so the database never ends up
/// with a half-committed reception.
/// </summary>
public interface IRecepcionService
{
    /// <summary>
    /// Confirms a reception atomically. Validates each item (cantidad vs.
    /// códigos, dedupe, no DB duplicates) and, for GARRAFA products, creates
    /// one <c>Garrafa</c> + one <c>MovimientoGarrafa</c> (tipo
    /// <c>COMPRA</c>, estado origen = estado destino = <c>LLENA_DEPOSITO</c>,
    /// sin cliente) per submitted code.
    /// </summary>
    /// <param name="usuarioId">
    /// <c>HttpContext.User</c> identifier. The service resolves the operator
    /// <c>EmpleadoId</c> from this. Throws <see cref="InvalidOperationException"/>
    /// when the operator cannot be resolved.
    /// </param>
    /// <returns>The persisted <see cref="RecepcionDto"/> with includes.</returns>
    Task<RecepcionDto> CreateAsync(CrearRecepcionDto dto, ulong? usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a reception and the garrafas / movimientos it produced
    /// when every garrafa is still in <c>LLENA_DEPOSITO</c>. Refuses to
    /// reverse a reception that already moved stock to clients.
    /// </summary>
    /// <returns><c>true</c> when the reversal succeeded; <c>false</c> when the
    /// reception does not exist.</returns>
    Task<bool> ReversarAsync(ulong recepcionId, ulong? usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Productos activos para poblar el dropdown del formulario de alta
    /// (issue #45). Atajo sobre <c>IProductoService.GetActivosAsync</c>:
    /// mantiene el contrato cohesivo del servicio de recepciones.
    /// </summary>
    Task<IEnumerable<ProductoDto>> GetProductosActivosAsync(CancellationToken ct = default);

    /// <summary>
    /// Devuelve las recepciones activas (no soft-deleted) ordenadas por fecha
    /// descendente, limitadas a <paramref name="cantidad"/>. Usado para poblar
    /// el dropdown de Recepción en formularios de Garrafa (issue #48). Cada
    /// <see cref="RecepcionDto"/> trae el nombre del proveedor y del empleado,
    /// pero no los items (no se necesitan para un selector).
    /// </summary>
    Task<IEnumerable<RecepcionDto>> GetRecientesAsync(int cantidad, CancellationToken ct = default);
}