namespace ExtraGasMVC.Models.ViewModels;

public class DashboardViewModel
{
    public int TotalClientesActivos { get; set; }
    public int TotalPedidos { get; set; }
    public int PedidosPendientes { get; set; }
    public int TotalProductosActivos { get; set; }
    public int TotalGarrafas { get; set; }
    public decimal TotalCobrado { get; set; }
    public decimal TotalSaldo { get; set; }
    public IList<TopProducto> TopProductos { get; set; } = new List<TopProducto>();
    public IList<ExtraGasMVC.Data.Entities.Views.VPedidoResumen> UltimosPedidos { get; set; } = new List<ExtraGasMVC.Data.Entities.Views.VPedidoResumen>();
}

public class TopProducto
{
    public string Producto { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
}
