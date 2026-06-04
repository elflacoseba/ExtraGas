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

    public async Task<ClienteDto> CreateAsync(CreateClienteDto clienteDto, CancellationToken ct = default)
    {
        var cliente = _mapper.Map<Cliente>(clienteDto);
        cliente.CreatedAt = DateTime.UtcNow;
        cliente.UpdatedAt = DateTime.UtcNow;
        
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<ClienteDto> UpdateAsync(UpdateClienteDto clienteDto, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { clienteDto.Id }, ct);
        if (cliente == null)
            throw new KeyNotFoundException($"Cliente con Id {clienteDto.Id} no encontrado.");

        _mapper.Map(clienteDto, cliente);
        cliente.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null)
            return false;

        cliente.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
