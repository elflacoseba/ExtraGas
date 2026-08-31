namespace ExtraGasMVC.DTOs;

/// <summary>
/// DTO de impacto previo al soft-delete de un Producto. Issue #147 slice 3
/// item 2. Devuelto por
/// <see cref="Services.Interfaces.IProductoService.GetDeleteImpactAsync"/> y
/// usado por el Controller + View para decidir si mostrar un confirm simple
/// o exigir type-to-confirm.
/// </summary>
/// <param name="ProductoId">Id del producto a borrar (eco del request).</param>
/// <param name="Codigo">Código del producto (eco para type-to-confirm).</param>
/// <param name="PedidoItemsCount">Cantidad de pedido_items que referencian al producto.</param>
/// <param name="RecepcionItemsCount">Cantidad de recepcion_items que referencian al producto.</param>
/// <param name="MovimientosGarrafaCount">Cantidad de movimientos_garrafa que referencian al producto.</param>
public record ProductoDeleteImpactDto(
    int ProductoId,
    string Codigo,
    int PedidoItemsCount,
    int RecepcionItemsCount,
    int MovimientosGarrafaCount)
{
    /// <summary>
    /// Suma de los 3 contadores. El View usa este número para decidir
    /// "0 → confirm simple; &gt; 0 → type-to-confirm".
    /// </summary>
    public int TotalCount => PedidoItemsCount + RecepcionItemsCount + MovimientosGarrafaCount;

    /// <summary>
    /// Convenience: <c>TotalCount &gt; 0</c>. Mismo uso en el View.
    /// </summary>
    public bool HasDependencies => TotalCount > 0;
}
