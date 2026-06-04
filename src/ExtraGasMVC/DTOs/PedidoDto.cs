namespace ExtraGasMVC.DTOs;

public class PedidoDto
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool Entregado { get; set; }
    public ulong ClienteId { get; set; }
    public ulong EmpleadoId { get; set; }
    public ulong EstadoPedidoId { get; set; }
    public ulong CanalVentaId { get; set; }
    public ulong? MedioContactoId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string? Observaciones { get; set; }
    public string? DireccionEntrega { get; set; }
}

public class CreatePedidoDto
{
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool Entregado { get; set; }
    public ulong ClienteId { get; set; }
    public ulong EmpleadoId { get; set; }
    public ulong EstadoPedidoId { get; set; }
    public ulong CanalVentaId { get; set; }
    public ulong? MedioContactoId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public string? Observaciones { get; set; }
    public string? DireccionEntrega { get; set; }
}

public class UpdatePedidoDto
{
    public ulong Id { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool Entregado { get; set; }
    public ulong ClienteId { get; set; }
    public ulong EmpleadoId { get; set; }
    public ulong EstadoPedidoId { get; set; }
    public ulong CanalVentaId { get; set; }
    public ulong? MedioContactoId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public string? Observaciones { get; set; }
    public string? DireccionEntrega { get; set; }
}
