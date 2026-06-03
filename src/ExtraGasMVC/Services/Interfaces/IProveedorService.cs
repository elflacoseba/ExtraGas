using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProveedorService
{
    Task<Proveedor?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<Proveedor?> GetByCuitAsync(string cuit, CancellationToken ct = default);
    Task<IEnumerable<Proveedor>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Proveedor>> GetActivosAsync(CancellationToken ct = default);
    Task<Proveedor> CreateAsync(Proveedor proveedor, CancellationToken ct = default);
    Task<Proveedor> UpdateAsync(Proveedor proveedor, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
