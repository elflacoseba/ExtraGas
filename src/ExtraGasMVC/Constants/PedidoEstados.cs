namespace ExtraGasMVC.Constants;

/// <summary>
/// Canonical state codes for pedidos, matching the database catalog
/// <c>estados_pedido.codigo</c>. Used instead of magic strings for
/// compile-time safety and discoverability.
/// </summary>
public static class PedidoEstados
{
    public const string Pendiente = "PENDIENTE";
    public const string Confirmado = "CONFIRMADO";
    public const string EnPreparacion = "EN_PREPARACION";
    public const string Entregado = "ENTREGADO";
    public const string Cancelado = "CANCELADO";

    /// <summary>
    /// States that are considered final — no further transitions allowed.
    /// </summary>
    public static readonly HashSet<string> EstadosFinales = new()
    {
        Entregado,
        Cancelado
    };

    /// <summary>
    /// States where only DireccionEntrega and Observaciones can be edited.
    /// </summary>
    public static readonly HashSet<string> EstadosSoloLecturaParcial = new()
    {
        Confirmado,
        EnPreparacion
    };
}