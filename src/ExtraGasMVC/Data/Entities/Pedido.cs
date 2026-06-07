namespace ExtraGasMVC.Data.Entities;

public class Pedido
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool Entregado { get; set; }
    public ulong ClienteId { get; set; }
    public ulong EmpleadoId { get; set; }
    public ulong EstadoPedidoId { get; set; }
    public ulong CanalVentaId { get; set; }
    public ulong? MedioContactoId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string? Observaciones { get; set; }
    public string? DireccionEntrega { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual Cliente? Cliente { get; set; }
    public virtual Empleado? Empleado { get; set; }
    public virtual EstadoPedido? EstadoPedido { get; set; }
    public virtual CanalVenta? CanalVenta { get; set; }
    public virtual MedioContactoPedido? MedioContactoPedido { get; set; }
    public virtual ICollection<PedidoItem> Items { get; set; } = new List<PedidoItem>();
}
