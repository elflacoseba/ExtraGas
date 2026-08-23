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

    // Navigation properties (issues #42)
    public virtual Garrafa? Garrafa { get; set; }
    public virtual TipoMovimientoGarrafa? TipoMovimiento { get; set; }
    public virtual Pedido? Pedido { get; set; }
    public virtual RecepcionProveedor? Recepcion { get; set; }
    public virtual Cliente? Cliente { get; set; }
    public virtual EstadoGarrafa? EstadoOrigen { get; set; }
    public virtual EstadoGarrafa? EstadoDestino { get; set; }
    public virtual Empleado? Empleado { get; set; }
    public virtual Usuario? CreatedByUsuario { get; set; }
}
