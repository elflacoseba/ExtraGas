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

    public async Task<SearchResultDto<ProveedorDto>> SearchAsync(string? busqueda, bool soloActivos, int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Proveedores
            .AsNoTracking()
            .AsQueryable();

        if (soloActivos)
            query = query.Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            query = query.Where(p =>
                p.RazonSocial.Contains(q)
                || p.Cuit.Contains(q)
                || (p.NombreFantasia != null && p.NombreFantasia.Contains(q)));
        }

        var total = await query.CountAsync(ct);

        var proveedores = await query
            .OrderBy(p => p.RazonSocial)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new SearchResultDto<ProveedorDto>
        {
            Items = _mapper.Map<List<ProveedorDto>>(proveedores),
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio
        };
    }

    public async Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedorDto, ulong? createdBy, CancellationToken ct = default)
    {
        if (!await IsCuitUniqueAsync(proveedorDto.Cuit, ct))
            throw new InvalidOperationException("El CUIT ingresado ya está registrado.");

        var proveedor = _mapper.Map<Proveedor>(proveedorDto);
        proveedor.CreatedAt = DateTime.UtcNow;
        proveedor.UpdatedAt = DateTime.UtcNow;
        proveedor.CreatedBy = createdBy;
        proveedor.UpdatedBy = createdBy;
        
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProveedorDto>(proveedor);
    }

    public async Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto proveedorDto, ulong? updatedBy, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores.FindAsync(new object[] { id }, ct);
        if (proveedor == null)
            throw new KeyNotFoundException($"Proveedor con Id {id} no encontrado.");

        if (!await IsCuitUniqueAsync(proveedorDto.Cuit, id, ct))
            throw new InvalidOperationException("El CUIT ingresado ya está registrado.");

        _mapper.Map(proveedorDto, proveedor);
        proveedor.UpdatedAt = DateTime.UtcNow;
        proveedor.UpdatedBy = updatedBy;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProveedorDto>(proveedor);
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var proveedor = await _context.Proveedores.FindAsync(new object[] { id }, ct);
        if (proveedor == null)
            return false;

        proveedor.DeletedAt = DateTime.UtcNow;
        proveedor.UpdatedBy = updatedBy;
        proveedor.Activo = false;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }

    public async Task<IEnumerable<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default)
    {
        var provincias = await _context.Provincias
            .AsNoTracking()
            .OrderBy(p => p.Nombre)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProvinciaDto>>(provincias);
    }

    private async Task<bool> IsCuitUniqueAsync(string cuit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuit))
            return true;

        return !await _context.Proveedores
            .AsNoTracking()
            .AnyAsync(p => p.Cuit == cuit, ct);
    }

    private async Task<bool> IsCuitUniqueAsync(string cuit, ulong excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuit))
            return true;

        return !await _context.Proveedores
            .AsNoTracking()
            .AnyAsync(p => p.Cuit == cuit && p.Id != excludeId, ct);
    }
}
