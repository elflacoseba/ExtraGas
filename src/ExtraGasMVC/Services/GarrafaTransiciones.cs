using ExtraGasMVC.Constants;

namespace ExtraGasMVC.Services;

/// <summary>
/// State machine that defines the legal transitions between garrafa states
/// performed through the <c>CAMBIO_ESTADO</c> manual flow. Transitions driven
/// by <c>compras</c>, <c>pedidos</c>, or <c>bajas</c> still go through the
/// domain services and do not consult this matrix.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why hard-coded in C# rather than a database lookup table:</b> the
/// allowed transitions are an immutable contract of the domain — they
/// describe physical reality (a damaged garrafa cannot be sold as full;
/// a discarded garrafa cannot return to service). Encoded in code they
/// are reviewed alongside the service, validated by the compiler, and
/// trivially unit-testable.
/// </para>
/// <para>
/// If a transition needs to change, the update is a code change reviewed
/// through PR — not a silent DB write. See <c>db/docs/DECISIONES.md</c>
/// ADR #16 for the rationale.
/// </para>
/// <para>
/// <b>Lifecycle:</b>
/// </para>
/// <code>
///   LLENA_DEPOSITO ──┬─> EN_TRANSITO ──┬─> LLENA_DEPOSITO  (returned full)
///                   ├─> EN_CLIENTE   ──┼─> VACIA_DEPOSITO  (returned empty — normal swap)
///                   ├─> VACIA_DEPOSITO                  (consumed in-house)
///                   └─> DAÑADA
///   VACIA_DEPOSITO  ──> LLENA_DEPOSITO                  (refilled at plant)
///                   ──> EN_CLIENTE                       (delivered empty)
///                   ──> DAÑADA
///                   ──> FUERA_SERVICIO                   (retired)
///   DAÑADA          ──> VACIA_DEPOSITO                   (repaired)
///                   ──> FUERA_SERVICIO                   (unrecoverable)
///   FUERA_SERVICIO  ──> (terminal — no outgoing transitions)
/// </code>
/// </remarks>
public static class GarrafaTransiciones
{
    /// <summary>
    /// Transition matrix: origin state code → set of allowed destination state codes.
    /// Self-transitions are not represented (they are rejected as no-ops).
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Matriz =
        new(StringComparer.Ordinal)
        {
            [GarrafaEstados.LlenaDeposito] = new HashSet<string>(StringComparer.Ordinal)
            {
                GarrafaEstados.EnTransito,
                GarrafaEstados.EnCliente,
                GarrafaEstados.VaciaDeposito,
                GarrafaEstados.Danada
            },
            [GarrafaEstados.VaciaDeposito] = new HashSet<string>(StringComparer.Ordinal)
            {
                GarrafaEstados.LlenaDeposito,
                GarrafaEstados.EnCliente,
                GarrafaEstados.Danada,
                GarrafaEstados.FueraServicio
            },
            [GarrafaEstados.EnTransito] = new HashSet<string>(StringComparer.Ordinal)
            {
                GarrafaEstados.LlenaDeposito,
                GarrafaEstados.VaciaDeposito,
                GarrafaEstados.EnCliente,
                GarrafaEstados.Danada
            },
            [GarrafaEstados.EnCliente] = new HashSet<string>(StringComparer.Ordinal)
            {
                GarrafaEstados.VaciaDeposito,
                GarrafaEstados.LlenaDeposito,
                GarrafaEstados.Danada,
                GarrafaEstados.FueraServicio
            },
            [GarrafaEstados.Danada] = new HashSet<string>(StringComparer.Ordinal)
            {
                GarrafaEstados.VaciaDeposito,
                GarrafaEstados.FueraServicio
            },

            // FUERA_SERVICIO is terminal — once a garrafa is retired it cannot
            // return to the active inventory. This is the example called out
            // in issue #40 (FUERA_SERVICIO → LLENA_DEPOSITO must be rejected).
            [GarrafaEstados.FueraServicio] = new HashSet<string>(StringComparer.Ordinal)
        };

    /// <summary>
    /// Returns <c>true</c> when transitioning from <paramref name="origenCodigo"/>
    /// to <paramref name="destinoCodigo"/> is allowed by the matrix.
    /// A self-transition (origen == destino) is always rejected.
    /// </summary>
    public static bool EsValida(string origenCodigo, string destinoCodigo)
    {
        if (string.IsNullOrEmpty(origenCodigo) || string.IsNullOrEmpty(destinoCodigo))
            return false;

        if (string.Equals(origenCodigo, destinoCodigo, StringComparison.Ordinal))
            return false;

        return Matriz.TryGetValue(origenCodigo, out var destinos)
               && destinos.Contains(destinoCodigo);
    }

    /// <summary>
    /// Returns the set of destination state codes reachable from
    /// <paramref name="origenCodigo"/>. Returns an empty set when the origin
    /// is unknown or terminal.
    /// </summary>
    public static HashSet<string> DestinosPermitidos(string origenCodigo)
    {
        if (string.IsNullOrEmpty(origenCodigo))
            return new HashSet<string>(StringComparer.Ordinal);

        return Matriz.TryGetValue(origenCodigo, out var destinos)
            ? destinos
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
