namespace ExtraGasMVC.Data.Entities.Views;

public class VStockGarrafa
{
    public byte CapacidadKg { get; set; }
    public ulong EstadoGarrafaId { get; set; }
    public string EstadoCodigo { get; set; } = null!;
    public string EstadoNombre { get; set; } = null!;
    public string? EstadoColor { get; set; }
    public int Cantidad { get; set; }
}
