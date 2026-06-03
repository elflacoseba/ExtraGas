namespace ExtraGasMVC.Data.Entities.Views;

public class VRegularidadCliente
{
    public ulong ClienteId { get; set; }
    public string Cliente { get; set; } = null!;
    public int TotalPedidos { get; set; }
    public DateTime? UltimoPedido { get; set; }
    public DateTime? PrimerPedido { get; set; }
    public double? DiasPromedioEntrePedidos { get; set; }
    public decimal? TotalFacturado { get; set; }
    public decimal? SaldoPendiente { get; set; }
}
