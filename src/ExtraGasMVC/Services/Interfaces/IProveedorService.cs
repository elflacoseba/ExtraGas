using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProveedorService
{
    Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<ProveedorDto?> GetByCuitAsync(string cuit, CancellationToken ct = default);

    Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedor, ulong? createdBy, CancellationToken ct = default);
    Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto proveedor, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
    Task<IEnumerable<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default);
    Task<PagedResult<ProveedorDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default);
}
