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
    Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, CancellationToken ct = default);
    Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
