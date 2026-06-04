using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ProveedorService : IProveedorService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public ProveedorService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<ProveedorDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        
        return proveedor is null ? null : _mapper.Map<ProveedorDto>(proveedor);
    }

    public async Task<ProveedorDto?> GetByCuitAsync(string cuit, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Cuit == cuit, ct);
        
        return proveedor is null ? null : _mapper.Map<ProveedorDto>(proveedor);
    }

    public async Task<IEnumerable<ProveedorDto>> GetAllAsync(CancellationToken ct = default)
    {
        var proveedores = await _context.Proveedores
            .AsNoTracking()
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProveedorDto>>(proveedores);
    }

    public async Task<IEnumerable<ProveedorDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var proveedores = await _context.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo)
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProveedorDto>>(proveedores);
    }

    public async Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedorDto, CancellationToken ct = default)
    {
        var proveedor = _mapper.Map<Proveedor>(proveedorDto);
        proveedor.CreatedAt = DateTime.UtcNow;
        proveedor.UpdatedAt = DateTime.UtcNow;
        
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProveedorDto>(proveedor);
    }

    public async Task<ProveedorDto> UpdateAsync(UpdateProveedorDto proveedorDto, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores.FindAsync(new object[] { proveedorDto.Id }, ct);
        if (proveedor == null)
            throw new KeyNotFoundException($"Proveedor con Id {proveedorDto.Id} no encontrado.");

        _mapper.Map(proveedorDto, proveedor);
        proveedor.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProveedorDto>(proveedor);
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
