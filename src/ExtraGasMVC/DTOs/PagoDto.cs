namespace ExtraGasMVC.DTOs;

public class PagoDto
{
    public ulong Id { get; set; }
    public string? NumeroRecibo { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ClienteId { get; set; }
    public ulong? PedidoId { get; set; }
    public ulong FormaPagoId { get; set; }
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
}

public class CreatePagoDto
{
    public string? NumeroRecibo { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ClienteId { get; set; }
    public ulong? PedidoId { get; set; }
    public ulong FormaPagoId { get; set; }
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
}

public class UpdatePagoDto
{
    public ulong Id { get; set; }
    public string? NumeroRecibo { get; set; }
    public DateTime Fecha { get; set; }
    public ulong ClienteId { get; set; }
    public ulong? PedidoId { get; set; }
    public ulong FormaPagoId { get; set; }
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
}
