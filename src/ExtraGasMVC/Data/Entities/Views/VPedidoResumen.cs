namespace ExtraGasMVC.Data.Entities.Views;

public class VPedidoResumen
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool Entregado { get; set; }
    public ulong ClienteId { get; set; }
    public string Cliente { get; set; } = null!;
    public string ClienteTelefono { get; set; } = null!;
    public ulong EmpleadoId { get; set; }
    public string Empleado { get; set; } = null!;
    public ulong EstadoPedidoId { get; set; }
    public string EstadoCodigo { get; set; } = null!;
    public string EstadoNombre { get; set; } = null!;
    public ulong CanalVentaId { get; set; }
    public string CanalCodigo { get; set; } = null!;
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string EstadoPago { get; set; } = null!;
}
