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

    /// <summary>
    /// Issue #17: soft-delete per AGENTS.md convention #6. La columna
    /// <c>deleted_at</c> ya existe en la BD (migración
    /// <c>20260607_000003_pedido_items_soft_delete_and_unique.sql</c>) y se
    /// usa como parte del <c>unique_hash</c> generado para que la constraint
    /// única permita re-agregar el mismo (pedido, producto, tipo_linea) tras
    /// una baja.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public virtual Pedido? Pedido { get; set; }
    public virtual Producto? Producto { get; set; }
}
