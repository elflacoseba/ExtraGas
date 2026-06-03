namespace ExtraGasMVC.Data.Entities.Views;

public class VGarrafaEnCliente
{
    public ulong GarrafaId { get; set; }
    public string Codigo { get; set; } = null!;
    public byte CapacidadKg { get; set; }
    public ulong ClienteId { get; set; }
    public string Cliente { get; set; } = null!;
    public DateTime? FechaUltimoMovimiento { get; set; }
    public int? DiasEnCliente { get; set; }
}
