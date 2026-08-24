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
    Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, CancellationToken ct = default);
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
}
