namespace ExtraGasMVC.DTOs;

/// <summary>
/// DTO de una fila de <c>pedido_estados_historico</c> (issue #165).
/// Expone los datos crudos (ids, motivo, fecha) y, para la vista de
/// timeline, los nombres legibles de los estados y del usuario que
/// disparó la transición.
/// </summary>
public class PedidoEstadoHistoricoDto
{
    public ulong Id { get; set; }
    public ulong PedidoId { get; set; }
    public ulong? EstadoAnteriorId { get; set; }
    public ulong EstadoNuevoId { get; set; }
    public string? MotivoCancelacion { get; set; }
    public ulong? UsuarioId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Display fields (resolved via Include en el Service).
    public string? EstadoAnteriorCodigo { get; set; }
    public string? EstadoAnteriorNombre { get; set; }
    public string? EstadoAnteriorColor { get; set; }
    public string EstadoNuevoCodigo { get; set; } = null!;
    public string EstadoNuevoNombre { get; set; } = null!;
    public string? EstadoNuevoColor { get; set; }

    public string? UsuarioNombre { get; set; }
}