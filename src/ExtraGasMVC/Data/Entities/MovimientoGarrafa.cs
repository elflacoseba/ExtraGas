namespace ExtraGasMVC.Data.Entities;

public class MovimientoGarrafa
{
    public ulong Id { get; set; }
    public ulong GarrafaId { get; set; }
    public DateTime Fecha { get; set; }
    public ulong TipoMovimientoId { get; set; }
    public ulong? PedidoId { get; set; }
    public ulong? RecepcionId { get; set; }
    public ulong? ClienteId { get; set; }
    public ulong? EstadoOrigenId { get; set; }
    public ulong EstadoDestinoId { get; set; }
    public ulong? EmpleadoId { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
}
