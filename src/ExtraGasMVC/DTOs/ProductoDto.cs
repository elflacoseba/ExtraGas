namespace ExtraGasMVC.DTOs;

public class ProductoDto
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
}

public class CreateProductoDto
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public ulong TipoProductoId { get; set; }
    public decimal? CapacidadKg { get; set; }
    public string UnidadVenta { get; set; } = "UNIDAD";
    public decimal PrecioActual { get; set; }
    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
}

public class UpdateProductoDto
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
}
