using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProductoService
{
    Task<Producto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<Producto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<Producto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Producto>> GetActivosAsync(CancellationToken ct = default);
    Task<IEnumerable<Producto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default);
    Task<Producto> CreateAsync(Producto producto, CancellationToken ct = default);
    Task<Producto> UpdateAsync(Producto producto, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
