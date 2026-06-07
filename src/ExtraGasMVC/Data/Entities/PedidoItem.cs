using ExtraGasMVC.Data.Entities.Enums;

namespace ExtraGasMVC.Data.Entities;

public class PedidoItem
{
    public ulong Id { get; set; }
    public ulong PedidoId { get; set; }
    public ulong ProductoId { get; set; }
    public TipoLinea TipoLinea { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual Pedido? Pedido { get; set; }
    public virtual Producto? Producto { get; set; }
}
