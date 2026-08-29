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
            .Include(p => p.TipoProducto)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return producto is null ? null : _mapper.Map<ProductoDto>(producto);
    }

    public async Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .FirstOrDefaultAsync(p => p.Codigo == codigo, ct);

        return producto is null ? null : _mapper.Map<ProductoDto>(producto);
    }

    public async Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .Where(p => p.Activo)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .Where(p => p.TipoProductoId == tipoProductoId)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default)
    {
        var tipos = await _context.TiposProducto
            .AsNoTracking()
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);
        
        return _mapper.Map<IEnumerable<TipoProductoDto>>(tipos);
    }

    public async Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default)
    {
        var entity = _mapper.Map<Producto>(producto);
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = usuarioId;
        entity.UpdatedBy = usuarioId;

        _context.Productos.Add(entity);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ProductoDto>(entity);
    }

    public async Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default)
    {
        var entity = await _context.Productos.FindAsync(new object[] { producto.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Producto con Id {producto.Id} no encontrado.");

        _mapper.Map(producto, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ProductoDto>(entity);
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
