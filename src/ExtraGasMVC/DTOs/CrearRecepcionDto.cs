using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

/// <summary>
/// Input shape that the operator submits to confirm a proveedor reception
/// (issue #45). For each item, when the product is GARRAFA
/// (<c>maneja_garrafa_individual = TRUE</c>) the <c>CodigosGarrafa</c> list
/// must contain exactly <c>Cantidad</c> codes.
/// </summary>
public class CrearRecepcionDto
{
    [Display(Name = "Proveedor")]
    [Required(ErrorMessage = "El proveedor es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un proveedor válido.")]
    public ulong ProveedorId { get; set; }

    [Display(Name = "Fecha")]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    public DateTime Fecha { get; set; }

    [Display(Name = "Número de factura del proveedor")]
    [StringLength(50, ErrorMessage = "El número de factura no puede superar {1} caracteres.")]
    public string? NumeroFacturaProveedor { get; set; }

    [Display(Name = "Subtotal")]
    [Range(0, 9999999999.99, ErrorMessage = "El subtotal debe estar entre {1} y {2}.")]
    public decimal Subtotal { get; set; }

    [Display(Name = "Descuento")]
    [Range(0, 9999999999.99, ErrorMessage = "El descuento debe estar entre {1} y {2}.")]
    public decimal Descuento { get; set; }

    [Display(Name = "Total")]
    [Range(0, 9999999999.99, ErrorMessage = "El total debe estar entre {1} y {2}.")]
    public decimal Total { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(2000, ErrorMessage = "Las observaciones no pueden superar {1} caracteres.")]
    public string? Observaciones { get; set; }

    public List<CrearRecepcionItemDto> Items { get; set; } = new();
}

/// <summary>
/// One line in the reception. For non-GARRAFA products leave
/// <c>CodigosGarrafa</c> empty. For GARRAFA products the service enforces
/// <c>CodigosGarrafa.Count == Cantidad</c> and rejects duplicates or
/// existing codes before any write.
/// </summary>
public class CrearRecepcionItemDto
{
    [Display(Name = "Producto")]
    [Required(ErrorMessage = "El producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un producto válido.")]
    public ulong ProductoId { get; set; }

    [Display(Name = "Cantidad")]
    [Required(ErrorMessage = "La cantidad es obligatoria.")]
    [Range(0.01, 99999999.99, ErrorMessage = "La cantidad debe ser mayor a 0.")]
    public decimal Cantidad { get; set; }

    [Display(Name = "Precio unitario")]
    [Required(ErrorMessage = "El precio unitario es obligatorio.")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio unitario debe ser mayor o igual a 0.")]
    public decimal PrecioUnitario { get; set; }

    /// <summary>
    /// Physical garrafa codes captured in the UI textarea. Empty when the
    /// product does NOT manage individual garrafas (carbón, leña).
    /// </summary>
    public List<string> CodigosGarrafa { get; set; } = new();
}