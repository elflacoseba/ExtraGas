using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IClienteService
{
    Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default);
    Task<ClienteDto> CreateAsync(CreateClienteDto cliente, CancellationToken ct = default);
    Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
