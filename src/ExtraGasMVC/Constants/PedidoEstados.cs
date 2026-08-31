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
    /// Exposed as <see cref="IReadOnlySet{T}"/> so callers can query membership
    /// without being able to mutate the underlying set (S3887).
    /// </summary>
    private static readonly HashSet<string> _estadosFinales = new()
    {
        Entregado,
        Cancelado
    };

    public static readonly IReadOnlySet<string> EstadosFinales = _estadosFinales;

    /// <summary>
    /// States where only DireccionEntrega and Observaciones can be edited.
    /// Exposed as <see cref="IReadOnlySet{T}"/> so callers can query membership
    /// without being able to mutate the underlying set (S3887).
    /// </summary>
    private static readonly HashSet<string> _estadosSoloLecturaParcial = new()
    {
        Confirmado,
        EnPreparacion
    };

    public static readonly IReadOnlySet<string> EstadosSoloLecturaParcial = _estadosSoloLecturaParcial;
}