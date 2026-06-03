namespace ExtraGasMVC.Data.Entities.Views;

public class VPagoPorFormaPago
{
    public DateTime Fecha { get; set; }
    public string FormaPagoCodigo { get; set; } = null!;
    public string FormaPagoNombre { get; set; } = null!;
    public int CantidadPagos { get; set; }
    public decimal MontoTotal { get; set; }
}
