namespace ExtraGasMVC.Data.Entities;

public class Pago
{
    public ulong Id { get; set; }
    public string? NumeroRecibo { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ClienteId { get; set; }
    public ulong? PedidoId { get; set; }
    public ulong FormaPagoId { get; set; }
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
