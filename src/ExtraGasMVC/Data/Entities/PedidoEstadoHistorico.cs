namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Registro append-only de cambios de estado de un <see cref="Pedido"/>.
/// Una fila por transición efectiva (estado anterior != estado nuevo).
/// Sin soft-delete, sin updated_at: la tabla es inmutable por convención y
/// por diseño (issue #165).
///
/// El Service <c>PedidoService</c> es el único punto de INSERT, vía el
/// helper privado <c>RegistrarCambioEstadoAsync</c> que se invoca dentro
/// del mismo <c>SaveChangesAsync</c> que muta <c>pedidos.estado_pedido_id</c>.
/// Si el <c>SaveChanges</c> falla, ni la mutación del pedido ni la fila
/// de historial quedan persistidas — atomicidad garantizada por compartir
/// transacción.
///
/// Decisión de diseño: las columnas se llaman en snake_case en SQL
/// (<c>estado_anterior_id</c>, <c>motivo_cancelacion</c>, etc.) siguiendo
/// la convención del repositorio (AGENTS.md §Convenciones), mapeadas vía
/// <c>PedidoEstadoHistoricoConfiguration</c>.
/// </summary>
public class PedidoEstadoHistorico
{
    public ulong Id { get; set; }
    public ulong PedidoId { get; set; }
    public ulong? EstadoAnteriorId { get; set; }
    public ulong EstadoNuevoId { get; set; }
    public string? MotivoCancelacion { get; set; }
    public ulong? UsuarioId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Pedido Pedido { get; set; } = null!;
    public EstadoPedido? EstadoAnterior { get; set; }
    public EstadoPedido EstadoNuevo { get; set; } = null!;
    public Usuario? Usuario { get; set; }
}