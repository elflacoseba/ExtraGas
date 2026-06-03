namespace ExtraGasMVC.Data.Entities;

public class Producto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public ulong TipoProductoId { get; set; }
    public decimal? CapacidadKg { get; set; }
    public string UnidadVenta { get; set; } = "UNIDAD";
    public decimal PrecioActual { get; set; }
    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
