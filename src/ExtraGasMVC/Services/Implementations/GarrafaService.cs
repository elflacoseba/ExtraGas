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

    public async Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default)
    {
        var estados = await _context.EstadosGarrafa
            .AsNoTracking()
            .OrderBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EstadoGarrafaDto>>(estados);
    }

    public async Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafaDto, CancellationToken ct = default)
    {
        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafaDto.Codigo, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafaDto.Codigo}.");

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

        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafaDto.Codigo && g.Id != garrafaDto.Id, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafaDto.Codigo}.");

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

        var tipoCambioEstadoId = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == "CAMBIO_ESTADO")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tipoCambioEstadoId == 0)
            throw new InvalidOperationException("No se encontró el tipo de movimiento CAMBIO_ESTADO en la base de datos.");

        var estadoOrigen = garrafa.EstadoGarrafaId;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            garrafa.EstadoGarrafaId = dto.NuevoEstadoId;
            garrafa.ClienteId = dto.ClienteId;
            garrafa.FechaUltimoMovimiento = DateTime.UtcNow;
            garrafa.UpdatedAt = DateTime.UtcNow;

            var movimiento = new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = DateTime.UtcNow,
                TipoMovimientoId = tipoCambioEstadoId,
                ClienteId = dto.ClienteId,
                EstadoOrigenId = estadoOrigen,
                EstadoDestinoId = dto.NuevoEstadoId,
                Observaciones = dto.Observaciones
            };

            _context.MovimientosGarrafa.Add(movimiento);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (garrafa == null)
            return false;

        var codigosBloqueados = new[] { "EN_CLIENTE", "EN_TRANSITO" };
        var estadoCodigo = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId)
            .Select(e => e.Codigo)
            .FirstOrDefaultAsync(ct);

        if (estadoCodigo != null && codigosBloqueados.Contains(estadoCodigo))
            throw new InvalidOperationException(
                $"No se puede eliminar una garrafa en estado {estadoCodigo}. Primero cambie su estado.");

        garrafa.DeletedAt = DateTime.UtcNow;
        garrafa.Activo = false;
        garrafa.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return true;
    }
}
