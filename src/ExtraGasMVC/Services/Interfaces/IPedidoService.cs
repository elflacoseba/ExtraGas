using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPedidoService
{
    Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<SearchResultDto<PedidoDto>> SearchAsync(
        string? numero, ulong? estadoId, ulong? clienteId,
        DateTime? desde, DateTime? hasta,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);
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
}