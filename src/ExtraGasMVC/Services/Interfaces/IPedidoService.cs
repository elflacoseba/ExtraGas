using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPedidoService
{
    Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> SearchAsync(
        string? numero, ulong? estadoId, ulong? clienteId,
        DateTime? desde, DateTime? hasta,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default);
    Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default);

    /// <summary>
    /// Returns pedidos with saldo pendiente (saldo &gt; 0). Used by the dashboard
    /// dropdown in <c>PagosController</c> and the <c>Pendientes</c> view.
    /// <para>
    /// Not paginated by design — the dataset is bounded by pedidos with debt,
    /// which is small in practice. If this grows large, add pagination
    /// parameters and return a <see cref="PagedResult{T}"/> like
    /// <see cref="SearchAsync"/> does.
    /// </para>
    /// </summary>
    Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default);
    Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default);

    Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default);
    Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default);
    Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default);

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