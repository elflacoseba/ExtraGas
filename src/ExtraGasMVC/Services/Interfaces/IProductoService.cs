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

    /// <summary>
    /// Reactiva un producto soft-deleted. Issue #145 Slice 2: inversa de
    /// <see cref="DeleteAsync"/>; limpia <c>DeletedAt</c> y setea
    /// <c>Activo = true</c> explícitamente (Producto retiene la columna
    /// <c>Activo</c> por #114; a diferencia de Cliente post-#115 donde el
    /// flag se deriva de <c>DeletedAt</c>).
    /// Devuelve <c>false</c> si el producto no existe o ya está activo.
    /// </summary>
    Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
}
