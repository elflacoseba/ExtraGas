using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IClienteService
{
    Task<Cliente?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<Cliente>> GetAllAsync(CancellationToken ct = default);
    Task<Cliente?> GetByDniAsync(string dni, CancellationToken ct = default);
    Task<IEnumerable<Cliente>> GetActivosAsync(CancellationToken ct = default);
    Task<Cliente> CreateAsync(Cliente cliente, CancellationToken ct = default);
    Task<Cliente> UpdateAsync(Cliente cliente, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
