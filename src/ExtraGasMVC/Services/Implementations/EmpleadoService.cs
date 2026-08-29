using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class EmpleadoService : IEmpleadoService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public EmpleadoService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<EmpleadoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var empleado = await _context.Empleados
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return empleado is null ? null : _mapper.Map<EmpleadoDto>(empleado);
    }

    public async Task<SearchResultDto<EmpleadoDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Empleados
            .AsNoTracking()
            .AsQueryable();

        if (soloActivos)
            query = query.Where(e => e.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            query = query.Where(e =>
                e.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Apellido.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.Dni != null && e.Dni.Contains(q))
                || (e.Cuil != null && e.Cuil.Contains(q))
                || (e.Telefono != null && e.Telefono.Contains(q)));
        }

        var total = await query.CountAsync(ct);

        var empleados = await query
            .OrderBy(e => e.Apellido)
            .ThenBy(e => e.Nombre)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new SearchResultDto<EmpleadoDto>
        {
            Items = _mapper.Map<List<EmpleadoDto>>(empleados),
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio
        };
    }

    public async Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto dto, ulong? createdBy, CancellationToken ct = default)
    {
        if (!await IsDniUniqueAsync(dto.Dni, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        var empleado = _mapper.Map<Empleado>(dto);
        empleado.CreatedAt = DateTime.UtcNow;
        empleado.UpdatedAt = DateTime.UtcNow;
        empleado.CreatedBy = createdBy;
        empleado.UpdatedBy = createdBy;

        _context.Empleados.Add(empleado);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<EmpleadoDto>(empleado);
    }

    public async Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto dto, ulong? updatedBy, CancellationToken ct = default)
    {
        var empleado = await _context.Empleados.FindAsync(new object[] { dto.Id }, ct);
        if (empleado == null)
            throw new KeyNotFoundException($"Empleado con Id {dto.Id} no encontrado.");

        if (!await IsDniUniqueAsync(dto.Dni, dto.Id, ct))
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        _mapper.Map(dto, empleado);
        empleado.UpdatedAt = DateTime.UtcNow;
        empleado.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<EmpleadoDto>(empleado);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var empleado = await _context.Empleados.FindAsync(new object[] { id }, ct);
        if (empleado == null)
            return false;

        empleado.DeletedAt = DateTime.UtcNow;
        empleado.Activo = false;
        empleado.UpdatedAt = DateTime.UtcNow;
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

        return !await _context.Empleados
            .AsNoTracking()
            .AnyAsync(e => e.Dni == dni, ct);
    }

    private async Task<bool> IsDniUniqueAsync(string? dni, ulong excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dni))
            return true;

        return !await _context.Empleados
            .AsNoTracking()
            .AnyAsync(e => e.Dni == dni && e.Id != excludeId, ct);
    }
}
