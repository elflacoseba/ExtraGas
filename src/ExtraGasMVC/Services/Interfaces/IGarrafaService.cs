using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IGarrafaService
{
    Task<Garrafa?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<Garrafa?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<Garrafa>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Garrafa>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<Garrafa>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);
    Task<Garrafa> CreateAsync(Garrafa garrafa, CancellationToken ct = default);
    Task<Garrafa> UpdateAsync(Garrafa garrafa, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, ulong? clienteId, CancellationToken ct = default);
}
