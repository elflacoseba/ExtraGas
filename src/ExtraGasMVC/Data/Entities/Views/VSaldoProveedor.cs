namespace ExtraGasMVC.Data.Entities.Views;

public class VSaldoProveedor
{
    public ulong ProveedorId { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string Cuit { get; set; } = null!;
    public int RecepcionesPendientes { get; set; }
    public decimal SaldoTotal { get; set; }
}
