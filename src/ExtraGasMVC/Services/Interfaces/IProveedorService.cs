using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProveedorService
{
    Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<ProveedorDto?> GetByCuitAsync(string cuit, CancellationToken ct = default);

    [Obsolete("Use SearchAsync instead")]
    Task<IEnumerable<ProveedorDto>> GetAllAsync(CancellationToken ct = default);

    [Obsolete("Use SearchAsync with soloActivos=true instead")]
    Task<IEnumerable<ProveedorDto>> GetActivosAsync(CancellationToken ct = default);

    Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedor, ulong? createdBy, CancellationToken ct = default);
    Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto proveedor, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
    Task<IEnumerable<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default);
    Task<SearchResultDto<ProveedorDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default);
}
