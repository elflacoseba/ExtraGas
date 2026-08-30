using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class ProductoService : IProductoService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    // Issue #145 Slice 2: ILogger<ProductoService> inyectado para trazabilidad
    // del restore (operación privilegiada, AdminOnly). ASP.NET Core registra
    // ILogger<T> por convencion; no hace falta tocar Program.cs.
    private readonly ILogger<ProductoService> _logger;

    public ProductoService(
        ExtraGasDbContext context,
        IMapper mapper,
        ILogger<ProductoService> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
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
        // Issue #114: Activo no viene del DTO. Lo setea el Service en true
        // porque es estado, no dato de carga del operador.
        entity.Activo = true;
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

        // Snapshot de Activo ANTES del AutoMapper: el formulario de Edit no
        // debe poder modificarlo. Si el operador lo manda distinto (sea por
        // bug del DTO, por curl o por form antiguo en cache), lo restauramos
        // silenciosamente. ManejaGarrafaIndividual NO se preserva — es config.
        var activoOriginal = entity.Activo;

        // Issue #145 Slice 3: snapshot del precio ANTES del AutoMapper para
        // detectar cambios reales. Se compara contra `entity.PrecioActual`
        // después del Map y se registra una fila append-only en
        // producto_precios_historico cuando hay cambio real. El guardado
        // `precioAnterior != 0` evita phantom rows en el primer update sobre
        // un producto recién creado con precio=0 (caso seed manual / backfill).
        var precioAnterior = entity.PrecioActual;

        _mapper.Map(producto, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;
        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal);

        // Hook de histórico: solo cuando hay cambio real (precioAnterior != 0
        // && precioAnterior != nuevo). Atómico: la fila append-only y el
        // update del producto commitean en el mismo SaveChangesAsync. Si
        // SaveChangesAsync falla, no queda fila huérfana.
        var precioNuevo = entity.PrecioActual;
        if (precioAnterior != precioNuevo && precioAnterior != 0m)
        {
            _context.ProductoPreciosHistorico.Add(new ProductoPrecioHistorico
            {
                ProductoId = entity.Id,
                PrecioAnterior = precioAnterior,
                PrecioNuevo = precioNuevo,
                MotivoCambioPrecio = producto.MotivoCambioPrecio,
                ChangedBy = usuarioId,
                ChangedAt = DateTime.UtcNow,
            });
            _logger.LogInformation(
                "Producto {ProductoId} cambió de precio: {PrecioAnterior} → {PrecioNuevo} (motivo: {Motivo}, operador: {ChangedBy})",
                entity.Id, precioAnterior, precioNuevo, producto.MotivoCambioPrecio ?? "<sin motivo>", usuarioId);
        }

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<ProductoDto>(entity);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var producto = await _context.Productos.FindAsync(new object[] { id }, ct);
        if (producto == null)
            return false;

        // Issue #114 (replicado): soft-delete completo — marca DeletedAt Y
        // baja Activo. Mantiene la invariante "Activo=false implica
        // DeletedAt != null" que las vistas y la consulta de activos esperan.
        // Antes solo se seteaba DeletedAt, dejando Activo=true: un zombie.
        producto.DeletedAt = DateTime.UtcNow;
        producto.Activo = false;
        producto.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        // Patrón tomado de PedidoService.RestoreAsync (línea 296). Usamos
        // IgnoreQueryFilters() porque el QueryFilter global oculta los
        // registros soft-deleted — sin esto no encontraríamos el producto
        // desde la papelera.
        var producto = await _context.Productos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (producto == null)
            return false;

        // Producto ya activo: nada que restaurar. Devolvemos false para que el
        // Controller mapee TempData[Error] en lugar de un falso Success.
        // Coherente con el spec de task 2.1 (RestoreAsync_OnAlreadyActive_ReturnsFalse).
        if (producto.DeletedAt == null)
            return false;

        // Producto retiene la columna Activo (a diferencia de Cliente post-#115
        // donde se deriva de DeletedAt). Setear explícitamente Activo=true
        // preserva la invariante "Activo=false implica DeletedAt != null"
        // (definida por #114, replicada en Productos por #121). Sin este set
        // quedaría un zombie: DeletedAt=null + Activo=false.
        producto.DeletedAt = null;
        producto.Activo = true;
        producto.UpdatedAt = DateTime.UtcNow;
        producto.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        // Trazabilidad: RestoreAsync es AdminOnly y revierte un soft-delete,
        // operación que el auditor quiere ver en logs. No loggeamos el caso
        // "no encontrado" porque es flujo esperado (404 desde la papelera).
        _logger.LogInformation(
            "Producto {ProductoId} reactivado por {UpdatedBy}",
            producto.Id, updatedBy);

        return true;
    }
}
