using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class PedidoService : IPedidoService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public PedidoService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        
        return pedido is null ? null : _mapper.Map<PedidoDto>(pedido);
    }

    public async Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.EstadoPedidoId == estadoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Where(p => p.Saldo > 0)
            .OrderBy(p => p.Fecha)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<PedidoDto> CreateAsync(CreatePedidoDto pedidoDto, CancellationToken ct = default)
    {
        var pedido = _mapper.Map<Pedido>(pedidoDto);
        pedido.CreatedAt = DateTime.UtcNow;
        pedido.UpdatedAt = DateTime.UtcNow;
        
        _context.Pedidos.Add(pedido);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<PedidoDto>(pedido);
    }

    public async Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedidoDto, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoDto.Id }, ct);
        if (pedido == null)
            throw new KeyNotFoundException($"Pedido con Id {pedidoDto.Id} no encontrado.");

        _mapper.Map(pedidoDto, pedido);
        pedido.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<PedidoDto>(pedido);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        pedido.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        pedido.EstadoPedidoId = nuevoEstadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
