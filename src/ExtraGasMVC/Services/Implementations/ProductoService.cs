using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ProductoService : IProductoService
{
    private readonly ExtraGasDbContext _context;

    public ProductoService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Producto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Producto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        return await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Codigo == codigo, ct);
    }

    public async Task<IEnumerable<Producto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Productos
            .AsNoTracking()
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Producto>> GetActivosAsync(CancellationToken ct = default)
    {
        return await _context.Productos
            .AsNoTracking()
            .Where(p => p.Activo)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Producto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default)
    {
        return await _context.Productos
            .AsNoTracking()
            .Where(p => p.TipoProductoId == tipoProductoId)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
    }

    public async Task<Producto> CreateAsync(Producto producto, CancellationToken ct = default)
    {
        producto.CreatedAt = DateTime.UtcNow;
        producto.UpdatedAt = DateTime.UtcNow;
        
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync(ct);
        
        return producto;
    }

    public async Task<Producto> UpdateAsync(Producto producto, CancellationToken ct = default)
    {
        producto.UpdatedAt = DateTime.UtcNow;
        
        _context.Productos.Update(producto);
        await _context.SaveChangesAsync(ct);
        
        return producto;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var producto = await _context.Productos.FindAsync(new object[] { id }, ct);
        if (producto == null)
            return false;

        producto.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
