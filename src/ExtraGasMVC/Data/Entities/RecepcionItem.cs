namespace ExtraGasMVC.Data.Entities;

public class RecepcionItem
{
    public ulong Id { get; set; }
    public ulong RecepcionId { get; set; }
    public ulong ProductoId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
