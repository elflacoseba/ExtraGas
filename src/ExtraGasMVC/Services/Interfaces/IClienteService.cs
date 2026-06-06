using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IClienteService
{
    Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default);
    Task<SearchResultDto<ClienteDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default);
    Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
    Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default);
}
