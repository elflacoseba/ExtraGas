using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExtraGasMVC.Services.Implementations;

public class ProveedorService : IProveedorService
{
    private const string ProvinciasCacheKey = "provincias_all";
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;

    public ProveedorService(ExtraGasDbContext context, IMapper mapper, IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
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
            var q = busqueda.Trim().ToLower();
            query = query.Where(p =>
                p.RazonSocial.ToLower().Contains(q)
                || p.Cuit.ToLower().Contains(q)
                || (p.NombreFantasia != null && p.NombreFantasia.ToLower().Contains(q)));
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

    public async Task<ProveedorDto> CreateAsync(CreateProveedorDto proveedor, ulong? createdBy, CancellationToken ct = default)
    {
        if (!await IsCuitUniqueAsync(proveedor.Cuit, ct))
            throw new InvalidOperationException("El CUIT ingresado ya está registrado.");

        var entity = _mapper.Map<Proveedor>(proveedor);
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = createdBy;
        entity.UpdatedBy = createdBy;

        _context.Proveedores.Add(entity);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ProveedorDto>(entity);
    }

    public async Task<ProveedorDto> UpdateAsync(ulong id, UpdateProveedorDto proveedor, ulong? updatedBy, CancellationToken ct = default)
    {
        var entity = await _context.Proveedores.FindAsync(new object[] { id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Proveedor con Id {id} no encontrado.");

        if (!await IsCuitUniqueAsync(proveedor.Cuit, id, ct))
            throw new InvalidOperationException("El CUIT ingresado ya está registrado.");

        _mapper.Map(proveedor, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ProveedorDto>(entity);
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
        return await _cache.GetOrCreateAsync(ProvinciasCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            entry.SlidingExpiration = TimeSpan.FromMinutes(15);

            var provincias = await _context.Provincias
                .AsNoTracking()
                .OrderBy(p => p.Nombre)
                .ToListAsync(ct);

            return _mapper.Map<List<ProvinciaDto>>(provincias);
        }) ?? [];
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
