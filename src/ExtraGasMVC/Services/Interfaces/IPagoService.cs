using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPagoService
{
    Task<PagoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<PagoDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<PagoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<PagoDto>> GetByPedidoAsync(ulong pedidoId, CancellationToken ct = default);
    Task<PagoDto> CreateAsync(CreatePagoDto pago, CancellationToken ct = default);
    Task<PagoDto> UpdateAsync(UpdatePagoDto pago, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
}
