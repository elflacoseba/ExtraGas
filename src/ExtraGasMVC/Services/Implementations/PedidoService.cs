using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class PedidoService : IPedidoService
{
    private readonly ExtraGasDbContext _context;

    public PedidoService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Pedido?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IEnumerable<Pedido>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Pedidos
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Pedido>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        return await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Pedido>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        return await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.EstadoPedidoId == estadoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Pedido>> GetPendientesAsync(CancellationToken ct = default)
    {
        return await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.Saldo > 0)
            .OrderBy(p => p.Fecha)
            .ToListAsync(ct);
    }

    public async Task<Pedido> CreateAsync(Pedido pedido, CancellationToken ct = default)
    {
        pedido.CreatedAt = DateTime.UtcNow;
        pedido.UpdatedAt = DateTime.UtcNow;
        
        _context.Pedidos.Add(pedido);
        await _context.SaveChangesAsync(ct);
        
        return pedido;
    }

    public async Task<Pedido> UpdateAsync(Pedido pedido, CancellationToken ct = default)
    {
        pedido.UpdatedAt = DateTime.UtcNow;
        
        _context.Pedidos.Update(pedido);
        await _context.SaveChangesAsync(ct);
        
        return pedido;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        pedido.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        pedido.EstadoPedidoId = nuevoEstadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
