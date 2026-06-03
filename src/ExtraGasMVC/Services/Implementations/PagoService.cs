using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class PagoService : IPagoService
{
    private readonly ExtraGasDbContext _context;

    public PagoService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Pago?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Pagos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IEnumerable<Pago>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Pagos
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Pago>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        return await _context.Pagos
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Pago>> GetByPedidoAsync(ulong pedidoId, CancellationToken ct = default)
    {
        return await _context.Pagos
            .AsNoTracking()
            .Where(p => p.PedidoId == pedidoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<Pago> CreateAsync(Pago pago, CancellationToken ct = default)
    {
        pago.CreatedAt = DateTime.UtcNow;
        pago.UpdatedAt = DateTime.UtcNow;
        
        _context.Pagos.Add(pago);
        await _context.SaveChangesAsync(ct);
        
        return pago;
    }

    public async Task<Pago> UpdateAsync(Pago pago, CancellationToken ct = default)
    {
        pago.UpdatedAt = DateTime.UtcNow;
        
        _context.Pagos.Update(pago);
        await _context.SaveChangesAsync(ct);
        
        return pago;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var pago = await _context.Pagos.FindAsync(new object[] { id }, ct);
        if (pago == null)
            return false;

        pago.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
