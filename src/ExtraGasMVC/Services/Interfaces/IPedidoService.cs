using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPedidoService
{
    Task<Pedido?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<Pedido>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Pedido>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<Pedido>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);
    Task<IEnumerable<Pedido>> GetPendientesAsync(CancellationToken ct = default);
    Task<Pedido> CreateAsync(Pedido pedido, CancellationToken ct = default);
    Task<Pedido> UpdateAsync(Pedido pedido, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, CancellationToken ct = default);
}
