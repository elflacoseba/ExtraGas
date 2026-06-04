using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ClienteService : IClienteService
{
    private readonly ExtraGasDbContext _context;

    public ClienteService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<Cliente?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<Cliente>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Clientes
            .AsNoTracking()
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);
    }

    public async Task<Cliente?> GetByDniAsync(string dni, CancellationToken ct = default)
    {
        return await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Dni == dni, ct);
    }

    public async Task<IEnumerable<Cliente>> GetActivosAsync(CancellationToken ct = default)
    {
        return await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);
    }

    public async Task<Cliente> CreateAsync(Cliente cliente, CancellationToken ct = default)
    {
        cliente.CreatedAt = DateTime.UtcNow;
        cliente.UpdatedAt = DateTime.UtcNow;
        
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync(ct);
        
        return cliente;
    }

    public async Task<Cliente> UpdateAsync(Cliente cliente, CancellationToken ct = default)
    {
        cliente.UpdatedAt = DateTime.UtcNow;
        
        _context.Clientes.Update(cliente);
        await _context.SaveChangesAsync(ct);
        
        return cliente;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null)
            return false;

        cliente.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
