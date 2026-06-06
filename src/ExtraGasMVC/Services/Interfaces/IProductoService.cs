using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProductoService
{
    Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default);
    Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default);

    Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default);
    Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
