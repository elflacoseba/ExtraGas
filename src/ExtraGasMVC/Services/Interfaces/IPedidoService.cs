using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPedidoService
{
    Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los pedidos con saldo pendiente (<c>saldo &gt; 0</c>), ordenados
    /// del más viejo al más nuevo (la cobranza prioriza antigüedad). Alimenta
    /// la vista <c>Pedidos/Pendientes</c> y el dropdown de selección de pedido
    /// en <c>PagosController.LoadViewBagsAsync</c>.
    /// <para>
    /// Issue #166: paginado para que la vista escale cuando crezca la cantidad
    /// de pedidos con deuda. La normalización de <paramref name="pagina"/> y
    /// <paramref name="tamanio"/> es defensiva — ambos vienen del query string
    /// y no son confiables. Se devuelven siempre los más viejos primero
    /// (<c>OrderBy(p =&gt; p.Fecha)</c>) para que la cobranza vea primero
    /// la deuda más antigua.
    /// </para>
    /// </summary>
    Task<PagedResult<PedidoDto>> GetPendientesAsync(
        int pagina = 1, int tamanio = 25, CancellationToken ct = default);
    Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default);

    Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default);
    Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default);
    Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el historial append-only de cambios de estado del pedido
    /// (issue #165), ordenado del más reciente al más antiguo. Cada fila
    /// incluye los nombres legibles del estado anterior/nuevo y del
    /// usuario que disparó la transición, para alimentar la timeline de
    /// <c>Pedidos/Details.cshtml</c> y el endpoint
    /// <c>/Pedidos/{id}/historial-estados</c>.
    /// </summary>
    /// <remarks>
    /// El índice <c>idx_peh_pedido_created (pedido_id, created_at DESC)</c>
    /// cubre exactamente esta query. Si el pedido no existe o no tiene
    /// transiciones registradas, devuelve enumerable vacío.
    /// </remarks>
    Task<IEnumerable<PedidoEstadoHistoricoDto>> GetHistorialEstadosAsync(ulong pedidoId, CancellationToken ct = default);

    Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default);
    Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default);
    Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default);

    Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default);
    Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default);
    Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default);
    Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default);

    /// <summary>
    /// Ejecuta el canje físico de garrafas en la transición a CONFIRMADO.
    /// Pre-valida cada código (existencia, estado origen, cliente en DEVOLUCION),
    /// valida idempotencia (rechaza si ya hay movimientos para el pedido),
    /// abre una transacción ambiente y delega en
    /// <c>IGarrafaService.RegistrarMovimientoPorCanjeAsync</c>. Finalmente
    /// actualiza el estado del pedido a CONFIRMADO dentro de la misma
    /// transacción. Cualquier falla hace rollback completo.
    /// </summary>
    /// <param name="codigosPorItem">
    /// Diccionario <c>itemId → códigos físicos</c>. Solo incluye items GARRAFA
    /// con tipo de línea ENTREGA o DEVOLUCION — el resto se ignora (un pedido
    /// con solo items VENTA pasa <c>null</c> o un diccionario vacío).
    /// </param>
    /// <returns>
    /// <c>true</c> cuando la transición se aplicó; <c>false</c> cuando el pedido
    /// no existe. Lanza <see cref="InvalidOperationException"/> ante cualquier
    /// error de validación (código inexistente, estado incorrecto, cantidad
    /// no coincide, re-CONFIRMADO, etc.).
    /// </returns>
    Task<bool> RegistrarCanjePedidoAsync(
        ulong pedidoId,
        Dictionary<ulong, List<string>> codigosPorItem,
        ulong? usuarioId,
        CancellationToken ct = default);
}