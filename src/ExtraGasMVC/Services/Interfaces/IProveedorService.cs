using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProveedorService
{
    Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<ProveedorDto?> GetByCuitAsync(string cuit, CancellationToken ct = default);
    Task<IEnumerable<ProveedorDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<ProveedorDto>> GetActivosAsync(CancellationToken ct = default);
    Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedor, CancellationToken ct = default);
    Task<ProveedorDto> UpdateAsync(UpdateProveedorDto proveedor, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
