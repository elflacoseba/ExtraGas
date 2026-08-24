namespace ExtraGasMVC.DTOs;

/// <summary>
/// Read shape returned by <c>IRecepcionService</c>. Mirrors
/// <c>RecepcionProveedor</c> and enriches with display-friendly lookups
/// (proveedor, empleado, items with product name).
/// </summary>
public class RecepcionDto
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ProveedorId { get; set; }
    public string? ProveedorNombre { get; set; }
    public ulong EmpleadoId { get; set; }
    public string? EmpleadoNombre { get; set; }
    public string? NumeroFacturaProveedor { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string? Observaciones { get; set; }
    public List<RecepcionItemDto> Items { get; set; } = new();
}

/// <summary>
/// Read shape for one reception line. <c>Subtotal</c> is a computed column
/// in MySQL (<c>cantidad * precio_unitario</c>) so it comes back filled in.
/// </summary>
public class RecepcionItemDto
{
    public ulong Id { get; set; }
    public ulong RecepcionId { get; set; }
    public ulong ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public string? ProductoCodigo { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Copied from <c>Producto.ManejaGarrafaIndividual</c>. Drives whether the
    /// UI renders the codes textarea (issue #45).
    /// </summary>
    public bool ManejaGarrafaIndividual { get; set; }
}