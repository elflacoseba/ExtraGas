using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class PagoService : IPagoService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public PagoService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PagoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var pago = await _context.Pagos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        
        return pago is null ? null : _mapper.Map<PagoDto>(pago);
    }

    public async Task<IEnumerable<PagoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var pagos = await _context.Pagos
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PagoDto>>(pagos);
    }

    public async Task<IEnumerable<PagoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var pagos = await _context.Pagos
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PagoDto>>(pagos);
    }

    public async Task<IEnumerable<PagoDto>> GetByPedidoAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var pagos = await _context.Pagos
            .AsNoTracking()
            .Where(p => p.PedidoId == pedidoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PagoDto>>(pagos);
    }

    public async Task<PagoDto> CreateAsync(CreatePagoDto pago, CancellationToken ct = default)
    {
        var entity = _mapper.Map<Pago>(pago);
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        _context.Pagos.Add(entity);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<PagoDto>(entity);
    }

    public async Task<PagoDto> UpdateAsync(UpdatePagoDto pago, CancellationToken ct = default)
    {
        var entity = await _context.Pagos.FindAsync(new object[] { pago.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Pago con Id {pago.Id} no encontrado.");

        _mapper.Map(pago, entity);
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<PagoDto>(entity);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var pago = await _context.Pagos.FindAsync(new object[] { id }, ct);
        if (pago == null)
            return false;

        pago.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
