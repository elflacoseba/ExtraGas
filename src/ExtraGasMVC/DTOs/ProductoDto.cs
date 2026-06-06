using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class ProductoDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public ulong TipoProductoId { get; set; }
    public string? TipoProductoNombre { get; set; }
    public decimal? CapacidadKg { get; set; }
    public string UnidadVenta { get; set; } = "UNIDAD";
    public decimal PrecioActual { get; set; }
    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
}

public class CreateProductoDto
{
    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(30, ErrorMessage = "El código no puede superar {1} caracteres.")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar {1} caracteres.")]
    public string Nombre { get; set; } = null!;

    [Display(Name = "Descripción")]
    [StringLength(255, ErrorMessage = "La descripción no puede superar {1} caracteres.")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "El tipo de producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un tipo de producto válido.")]
    public ulong TipoProductoId { get; set; }

    [Display(Name = "Capacidad (kg)")]
    [Range(0.01, 9999999999.99, ErrorMessage = "La capacidad debe ser un valor positivo.")]
    public decimal? CapacidadKg { get; set; }

    [Display(Name = "Unidad de venta")]
    [StringLength(20, ErrorMessage = "La unidad de venta no puede superar {1} caracteres.")]
    public string UnidadVenta { get; set; } = "UNIDAD";

    [Display(Name = "Precio actual")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio debe estar entre {1} y {2}.")]
    public decimal PrecioActual { get; set; }

    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
}

public class UpdateProductoDto
{
    public ulong Id { get; set; }

    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(30, ErrorMessage = "El código no puede superar {1} caracteres.")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar {1} caracteres.")]
    public string Nombre { get; set; } = null!;

    [Display(Name = "Descripción")]
    [StringLength(255, ErrorMessage = "La descripción no puede superar {1} caracteres.")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "El tipo de producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un tipo de producto válido.")]
    public ulong TipoProductoId { get; set; }

    [Display(Name = "Capacidad (kg)")]
    [Range(0.01, 9999999999.99, ErrorMessage = "La capacidad debe ser un valor positivo.")]
    public decimal? CapacidadKg { get; set; }

    [Display(Name = "Unidad de venta")]
    [StringLength(20, ErrorMessage = "La unidad de venta no puede superar {1} caracteres.")]
    public string UnidadVenta { get; set; } = "UNIDAD";

    [Display(Name = "Precio actual")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio debe estar entre {1} y {2}.")]
    public decimal PrecioActual { get; set; }

    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
}
