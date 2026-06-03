using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPagoService
{
    Task<Pago?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<Pago>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Pago>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<Pago>> GetByPedidoAsync(ulong pedidoId, CancellationToken ct = default);
    Task<Pago> CreateAsync(Pago pago, CancellationToken ct = default);
    Task<Pago> UpdateAsync(Pago pago, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
