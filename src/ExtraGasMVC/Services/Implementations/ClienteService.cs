using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExtraGasMVC.Services.Implementations;

public class ClienteService : IClienteService
{
    private const string ProvinciasCacheKey = "provincias_all";
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;

    public ClienteService(ExtraGasDbContext context, IMapper mapper, IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return cliente is null ? null : _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default)
    {
        var clientes = await _context.Clientes
            .AsNoTracking()
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ClienteDto>>(clientes);
    }

    public async Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Dni == dni, ct);

        return cliente is null ? null : _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var clientes = await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ClienteDto>>(clientes);
    }

    public async Task<SearchResultDto<ClienteDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Clientes
            .AsNoTracking()
            .AsQueryable();

        if (soloActivos)
            query = query.Where(c => c.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            query = query.Where(c =>
                c.Nombre.Contains(q)
                || c.Apellido.Contains(q)
                || (c.Dni != null && c.Dni.Contains(q))
                || (c.CuitCuil != null && c.CuitCuil.Contains(q))
                || c.TelefonoPrincipal.Contains(q));
        }

        var total = await query.CountAsync(ct);

        var clientes = await query
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new SearchResultDto<ClienteDto>
        {
            Items = _mapper.Map<List<ClienteDto>>(clientes),
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio
        };
    }

    public async Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default)
    {
        if (!await IsDniUniqueAsync(cliente.Dni, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        var entity = _mapper.Map<Cliente>(cliente);
        entity.Activo = true;
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = createdBy;
        entity.UpdatedBy = createdBy;

        _context.Clientes.Add(entity);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ClienteDto>(entity);
    }

    public async Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default)
    {
        var entity = await _context.Clientes.FindAsync(new object[] { cliente.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Cliente con Id {cliente.Id} no encontrado.");

        if (!await IsDniUniqueAsync(cliente.Dni, cliente.Id, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        _mapper.Map(cliente, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ClienteDto>(entity);
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null)
            return false;

        cliente.DeletedAt = DateTime.UtcNow;
        cliente.Activo = false;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente == null)
            return false;

        cliente.DeletedAt = null;
        cliente.Activo = true;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default)
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

    private async Task<bool> IsDniUniqueAsync(string? dni, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dni))
            return true;

        return !await _context.Clientes
            .AsNoTracking()
            .AnyAsync(c => c.Dni == dni, ct);
    }

    private async Task<bool> IsDniUniqueAsync(string? dni, ulong excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dni))
            return true;

        return !await _context.Clientes
            .AsNoTracking()
            .AnyAsync(c => c.Dni == dni && c.Id != excludeId, ct);
    }
}
