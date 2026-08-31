namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Lookup table de unidades de venta. Issue #147 slice 3 item 7.
/// Réplica del shape de <see cref="TipoProducto"/> (mismo ADR #4 sobre
/// catálogos-en-lugar-de-ENUM). Catálogo cerrado: solo se popula vía
/// migración SQL (seed de 4 valores: UNIDAD, GARRAFA, BOLSA, KG). No
/// hay UI CRUD — ver ADR #20 que se documenta en slice 3.
/// </summary>
public class UnidadVenta
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
