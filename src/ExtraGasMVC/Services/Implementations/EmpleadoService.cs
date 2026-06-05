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

    public async Task<IEnumerable<EmpleadoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var empleados = await _context.Empleados
            .AsNoTracking()
            .OrderBy(e => e.Apellido)
            .ThenBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EmpleadoDto>>(empleados);
    }

    public async Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto dto, CancellationToken ct = default)
    {
        var empleado = _mapper.Map<Empleado>(dto);
        empleado.CreatedAt = DateTime.UtcNow;
        empleado.UpdatedAt = DateTime.UtcNow;

        _context.Empleados.Add(empleado);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<EmpleadoDto>(empleado);
    }

    public async Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto dto, CancellationToken ct = default)
    {
        var empleado = await _context.Empleados.FindAsync(new object[] { dto.Id }, ct);
        if (empleado == null)
            throw new KeyNotFoundException($"Empleado con Id {dto.Id} no encontrado.");

        _mapper.Map(dto, empleado);
        empleado.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<EmpleadoDto>(empleado);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var empleado = await _context.Empleados.FindAsync(new object[] { id }, ct);
        if (empleado == null)
            return false;

        empleado.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
    }
}
