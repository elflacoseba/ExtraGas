namespace ExtraGasMVC.Data.Entities.Views;

public class VProductoMasVendido
{
    public DateTime Fecha { get; set; }
    public ulong ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = null!;
    public string ProductoNombre { get; set; } = null!;
    public string TipoProducto { get; set; } = null!;
    public decimal CantidadVendida { get; set; }
    public decimal CantidadEntregada { get; set; }
    public decimal CantidadDevuelta { get; set; }
    public decimal MontoTotal { get; set; }
}
