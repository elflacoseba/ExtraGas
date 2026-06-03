namespace ExtraGasMVC.Data.Entities.Views;

public class VRecepcionResumen
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ProveedorId { get; set; }
    public string Proveedor { get; set; } = null!;
    public string ProveedorCuit { get; set; } = null!;
    public ulong EmpleadoId { get; set; }
    public string Empleado { get; set; } = null!;
    public string? NumeroFacturaProveedor { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string EstadoPago { get; set; } = null!;
}
