namespace ExtraGasMVC.DTOs;

/// <summary>
/// Vista de un movimiento de garrafa para el historial.
/// Se mapea desde <c>MovimientoGarrafa</c> con AutoMapper.
/// </summary>
public class MovimientoGarrafaDto
{
    public ulong Id { get; set; }
    public DateTime Fecha { get; set; }

    // Tipo de movimiento (join con tipos_movimiento_garrafa)
    public ulong TipoMovimientoId { get; set; }
    public string TipoMovimientoCodigo { get; set; } = null!;
    public string TipoMovimientoNombre { get; set; } = null!;

    // Estado origen (nullable — el primer movimiento al crear la garrafa no tiene origen)
    public ulong? EstadoOrigenId { get; set; }
    public string? EstadoOrigenCodigo { get; set; }
    public string? EstadoOrigenNombre { get; set; }

    // Estado destino
    public ulong EstadoDestinoId { get; set; }
    public string EstadoDestinoCodigo { get; set; } = null!;
    public string EstadoDestinoNombre { get; set; } = null!;

    // Empleado que registró el movimiento (nullable — los movimientos automáticos no tienen empleado)
    public ulong? EmpleadoId { get; set; }
    public string? EmpleadoNombreCompleto { get; set; }

    // Pedido / Recepción asociado (nullable — los cambios manuales no tienen pedido)
    public ulong? PedidoId { get; set; }
    public ulong? RecepcionId { get; set; }

    // Código de la garrafa involucrada (issue #44: requerido para la card de
    // trazabilidad en Pedidos/Details).
    public string? GarrafaCodigo { get; set; }

    public string? Observaciones { get; set; }
}
