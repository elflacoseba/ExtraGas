namespace ExtraGasMVC.Data.Entities.Views;

public class VSaldoCliente
{
    public ulong ClienteId { get; set; }
    public string Cliente { get; set; } = null!;
    public string? TelefonoPrincipal { get; set; }
    public int PedidosPendientes { get; set; }
    public decimal SaldoTotal { get; set; }
}
