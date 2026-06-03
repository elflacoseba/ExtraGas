namespace ExtraGasMVC.Data.Entities.Views;

public class VCuentaCorrienteCliente
{
    public ulong ClienteId { get; set; }
    public string Cliente { get; set; } = null!;
    public ulong? PedidoId { get; set; }
    public string? Comprobante { get; set; }
    public DateTime Fecha { get; set; }
    public string TipoMovimiento { get; set; } = null!;
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public string? Observaciones { get; set; }
}
