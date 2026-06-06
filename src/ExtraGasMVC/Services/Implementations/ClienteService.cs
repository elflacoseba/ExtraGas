using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ClienteService : IClienteService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public ClienteService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
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
            var q = busqueda.Trim().ToLower();
            query = query.Where(c =>
                c.Nombre.ToLower().Contains(q)
                || c.Apellido.ToLower().Contains(q)
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

    public async Task<ClienteDto> CreateAsync(CreateClienteDto clienteDto, ulong? createdBy, CancellationToken ct = default)
    {
        if (!await IsDniUniqueAsync(clienteDto.Dni, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        var cliente = _mapper.Map<Cliente>(clienteDto);
        cliente.CreatedAt = DateTime.UtcNow;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.CreatedBy = createdBy;
        cliente.UpdatedBy = createdBy;

        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<ClienteDto> UpdateAsync(UpdateClienteDto clienteDto, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { clienteDto.Id }, ct);
        if (cliente == null)
            throw new KeyNotFoundException($"Cliente con Id {clienteDto.Id} no encontrado.");

        if (!await IsDniUniqueAsync(clienteDto.Dni, clienteDto.Id, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        _mapper.Map(clienteDto, cliente);
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null)
            return false;

        cliente.DeletedAt = DateTime.UtcNow;
        cliente.Activo = false;
        cliente.UpdatedAt = DateTime.UtcNow;
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
        var provincias = await _context.Provincias
            .AsNoTracking()
            .OrderBy(p => p.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<List<ProvinciaDto>>(provincias);
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
