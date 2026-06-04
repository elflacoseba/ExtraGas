using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class GarrafaService : IGarrafaService
{
    private readonly ExtraGasDbContext _context;

    public GarrafaService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Garrafa?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Garrafas
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<Garrafa?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        return await _context.Garrafas
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Codigo == codigo, ct);
    }

    public async Task<IEnumerable<Garrafa>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Garrafas
            .AsNoTracking()
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Garrafa>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        return await _context.Garrafas
            .AsNoTracking()
            .Where(g => g.ClienteId == clienteId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Garrafa>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        return await _context.Garrafas
            .AsNoTracking()
            .Where(g => g.EstadoGarrafaId == estadoId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
    }

    public async Task<Garrafa> CreateAsync(Garrafa garrafa, CancellationToken ct = default)
    {
        garrafa.CreatedAt = DateTime.UtcNow;
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        _context.Garrafas.Add(garrafa);
        await _context.SaveChangesAsync(ct);
        
        return garrafa;
    }

    public async Task<Garrafa> UpdateAsync(Garrafa garrafa, CancellationToken ct = default)
    {
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        _context.Garrafas.Update(garrafa);
        await _context.SaveChangesAsync(ct);
        
        return garrafa;
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, ulong? clienteId, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { id }, ct);
        if (garrafa == null)
            return false;

        garrafa.EstadoGarrafaId = nuevoEstadoId;
        garrafa.ClienteId = clienteId;
        garrafa.FechaUltimoMovimiento = DateTime.UtcNow;
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
