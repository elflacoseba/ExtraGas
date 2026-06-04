using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class GarrafaService : IGarrafaService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public GarrafaService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        
        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Codigo == codigo, ct);
        
        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Where(g => g.ClienteId == clienteId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Where(g => g.EstadoGarrafaId == estadoId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafaDto, CancellationToken ct = default)
    {
        var garrafa = _mapper.Map<Garrafa>(garrafaDto);
        garrafa.CreatedAt = DateTime.UtcNow;
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        _context.Garrafas.Add(garrafa);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafaDto, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { garrafaDto.Id }, ct);
        if (garrafa == null)
            throw new KeyNotFoundException($"Garrafa con Id {garrafaDto.Id} no encontrada.");

        _mapper.Map(garrafaDto, garrafa);
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { id }, ct);
        if (garrafa == null)
            return false;

        garrafa.EstadoGarrafaId = dto.NuevoEstadoId;
        garrafa.ClienteId = dto.ClienteId;
        garrafa.FechaUltimoMovimiento = DateTime.UtcNow;
        garrafa.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
