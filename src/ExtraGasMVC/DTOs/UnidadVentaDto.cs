namespace ExtraGasMVC.DTOs;

/// <summary>
/// DTO de salida para el catálogo cerrado <c>unidades_venta</c>. Issue #147
/// slice 3 item 7. Réplica de <see cref="TipoProductoDto"/> para mantener
/// consistencia entre lookups. Se devuelve desde
/// <see cref="Services.Interfaces.IProductoService.GetUnidadesVentaAsync"/>.
/// </summary>
public class UnidadVentaDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
}
