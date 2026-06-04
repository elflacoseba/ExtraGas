using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ProductoService : IProductoService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public ProductoService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        
        return producto is null ? null : _mapper.Map<ProductoDto>(producto);
    }

    public async Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Codigo == codigo, ct);
        
        return producto is null ? null : _mapper.Map<ProductoDto>(producto);
    }

    public async Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Where(p => p.Activo)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Where(p => p.TipoProductoId == tipoProductoId)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<ProductoDto> CreateAsync(CreateProductoDto productoDto, CancellationToken ct = default)
    {
        var producto = _mapper.Map<Producto>(productoDto);
        producto.CreatedAt = DateTime.UtcNow;
        producto.UpdatedAt = DateTime.UtcNow;
        
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProductoDto>(producto);
    }

    public async Task<ProductoDto> UpdateAsync(UpdateProductoDto productoDto, CancellationToken ct = default)
    {
        var producto = await _context.Productos.FindAsync(new object[] { productoDto.Id }, ct);
        if (producto == null)
            throw new KeyNotFoundException($"Producto con Id {productoDto.Id} no encontrado.");

        _mapper.Map(productoDto, producto);
        producto.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        
        return _mapper.Map<ProductoDto>(producto);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var producto = await _context.Productos.FindAsync(new object[] { id }, ct);
        if (producto == null)
            return false;

        producto.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        
        return true;
    }
}
