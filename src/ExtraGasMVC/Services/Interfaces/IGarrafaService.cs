using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IGarrafaService
{
    Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);
    Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default);
    Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default);
    Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);

    /// <summary>
    /// Returns the catalog rows for the destination states that the given
    /// garrafa is allowed to transition to. Used by the UI to filter the
    /// state dropdown shown on the "Cambiar estado" view.
    /// </summary>
    /// <returns>
    /// Empty enumerable when the garrafa doesn't exist, when its current
    /// state has no outgoing transitions (terminal state), or when the
    /// current state code is not present in the transition matrix.
    /// </returns>
    Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve todos los movimientos registrados para una garrafa específica,
    /// ordenados por fecha descendente. Cada movimiento trae los nombres
    /// legibles del tipo, los estados origen/destino y el empleado.
    /// Devuelve enumerable vacío si la garrafa no existe o no tiene movimientos.
    /// </summary>
    Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los movimientos de garrafa vinculados a un pedido, ordenados
    /// por id ascendente. Usado por la vista Details para mostrar la
    /// trazabilidad del canje (issue #44).
    /// </summary>
    Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Registra un movimiento de canje (ENTREGA_CLIENTE / DEVOLUCION_CLIENTE)
    /// para una garrafa física, dejando que el trigger de BD actualice
    /// <c>estado_garrafa_id</c> y <c>fecha_ultimo_movimiento</c>. La app solo
    /// setea <c>garrafa.cliente_id</c>. NO abre transacción propia: depende de
    /// la transacción ambiente de <c>PedidoService.RegistrarCanjePedidoAsync</c>.
    /// </summary>
    /// <param name="tipoMovimientoCodigo">
    /// <c>ENTREGA_CLIENTE</c> o <c>DEVOLUCION_CLIENTE</c>. Determina el estado
    /// destino esperado (EN_CLIENTE / LLENA_DEPOSITO) y se persiste en la fila
    /// de <c>movimientos_garrafa</c>.
    /// </param>
    /// <param name="clienteId">
    /// <c>pedido.cliente_id</c> para ENTREGA, <c>null</c> para DEVOLUCION.
    /// Se aplica a <c>garrafas.cliente_id</c> y al campo
    /// <c>movimientos_garrafa.cliente_id</c>.
    /// </param>
    Task RegistrarMovimientoPorCanjeAsync(
        ulong garrafaId,
        ulong estadoDestinoId,
        ulong? clienteId,
        ulong pedidoId,
        string tipoMovimientoCodigo,
        ulong? usuarioId,
        CancellationToken ct = default);
}
