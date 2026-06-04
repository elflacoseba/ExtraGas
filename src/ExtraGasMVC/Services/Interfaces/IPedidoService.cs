using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPedidoService
{
    Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);
    Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default);
    Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, CancellationToken ct = default);
    Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, CancellationToken ct = default);
}
