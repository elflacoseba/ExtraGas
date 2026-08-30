namespace ExtraGasMVC.Models.ViewModels;

/// <summary>
/// Filter / pagination parameters for <c>IPedidoService.SearchAsync</c>.
/// Carries the search criteria and pagination as a single value object so the
/// service signature stays under SonarQube csharpsquid:S107 (≤ 7 params).
/// Constructed in the controller from <c>[FromQuery]</c> route parameters.
/// </summary>
/// <param name="Numero">Pedido number fragment (case-sensitive substring match).</param>
/// <param name="EstadoId">FK to <c>estados_pedido</c>; <c>null</c> or 0 to skip.</param>
/// <param name="ClienteId">FK to <c>clientes</c>; <c>null</c> or 0 to skip.</param>
/// <param name="Desde">Inclusive lower bound on <c>pedidos.fecha</c>.</param>
/// <param name="Hasta">Inclusive upper bound on <c>pedidos.fecha</c> (day-resolution).</param>
/// <param name="Pagina">1-based page index.</param>
/// <param name="Tamanio">Page size.</param>
public sealed record PedidoSearchFilter(
    string? Numero,
    ulong? EstadoId,
    ulong? ClienteId,
    DateTime? Desde,
    DateTime? Hasta,
    int Pagina,
    int Tamanio);
