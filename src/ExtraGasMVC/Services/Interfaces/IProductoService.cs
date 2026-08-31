using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IProductoService
{
    Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default);
    Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default);

    /// <summary>
    /// Listado paginado server-side (Issue #146.5). Reemplaza el patrón
    /// <c>GetAllAsync + LINQ-to-Objects</c> que cargaba toda la tabla en
    /// memoria y rompía con catálogos grandes. El WHERE se traduce a SQL
    /// (incluye <c>OnlyActivos</c> y la búsqueda por código/nombre/desc) y el
    /// conteo se hace con <c>SELECT COUNT(*)</c> separado.
    /// </summary>
    Task<PagedResult<ProductoDto>> GetPagedAsync(
        string? busqueda,
        bool soloActivos,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default);
    Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? usuarioId = null, CancellationToken ct = default);

    /// <summary>
    /// Reactiva un producto soft-deleted. Issue #145 Slice 2: inversa de
    /// <see cref="DeleteAsync"/>; limpia <c>DeletedAt</c> y setea
    /// <c>Activo = true</c> explícitamente (Producto retiene la columna
    /// <c>Activo</c> por #114; a diferencia de Cliente post-#115 donde el
    /// flag se deriva de <c>DeletedAt</c>).
    /// Devuelve <c>false</c> si el producto no existe o ya está activo.
    /// </summary>
    Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el catálogo cerrado <c>unidades_venta</c> (UNIDAD, GARRAFA,
    /// BOLSA, KG) ordenado por <c>Nombre</c>. Cacheado en memoria con TTL
    /// 1h, mismo patrón que <see cref="GetTiposProductoAsync"/> (issue #147
    /// slice 3 item 7).
    /// </summary>
    Task<IEnumerable<UnidadVentaDto>> GetUnidadesVentaAsync(CancellationToken ct = default);

    /// <summary>
    /// Cuenta las dependencias históricas del producto antes del
    /// soft-delete (issue #147 slice 3 item 2). Devuelve los counts de
    /// pedido_items, recepcion_items y movimientos_garrafa que referencian
    /// al producto. El Controller usa el DTO para decidir si renderizar
    /// confirm simple o exigir type-to-confirm (SweetAlert2).
    ///
    /// <para>IMPORTANTE: el conteo NO filtra por <c>deleted_at</c> en
    /// ninguna de las 3 tablas (exploración #43-45: esas tablas no tienen
    /// <c>deleted_at</c>). Es exactamente lo que pide el spec scenario
    /// "count MUST NOT filter by deleted_at".</para>
    ///
    /// <para>Lanza <see cref="KeyNotFoundException"/> si el producto no
    /// existe o está soft-deleted — el caller debe recibir señal clara en
    /// lugar de un DTO con ceros que mentiría sobre el estado.</para>
    /// </summary>
    Task<ProductoDeleteImpactDto> GetDeleteImpactAsync(ulong id, CancellationToken ct = default);
}
