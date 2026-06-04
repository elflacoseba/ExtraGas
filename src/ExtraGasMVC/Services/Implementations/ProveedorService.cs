using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ProveedorService : IProveedorService
{
    private readonly ExtraGasDbContext _context;

    public ProveedorService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Proveedor?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Proveedor?> GetByCuitAsync(string cuit, CancellationToken ct = default)
    {
        return await _context.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Cuit == cuit, ct);
    }

    public async Task<IEnumerable<Proveedor>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Proveedores
            .AsNoTracking()
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Proveedor>> GetActivosAsync(CancellationToken ct = default)
    {
        return await _context.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo)
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
    }

    public async Task<Proveedor> CreateAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        proveedor.CreatedAt = DateTime.UtcNow;
        proveedor.UpdatedAt = DateTime.UtcNow;
        
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync(ct);
        
        return proveedor;
    }

    public async Task<Proveedor> UpdateAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        proveedor.UpdatedAt = DateTime.UtcNow;
        
        _context.Proveedores.Update(proveedor);
        await _context.SaveChangesAsync(ct);
        
        return proveedor;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores.FindAsync(new object[] { id }, ct);
        if (proveedor == null)
            return false;

        proveedor.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
