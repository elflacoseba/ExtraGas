using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ExtraGasMVC.Services.Implementations;

/// <summary>Confirma y reversa recepciones de proveedor (issue #45).</summary>
public class RecepcionService : IRecepcionService
{
    private const string TipoMovimientoCompra = "COMPRA";

    private readonly ExtraGasDbContext _context;
    private readonly IProductoService _productoService;

    public RecepcionService(ExtraGasDbContext context, IProductoService productoService)
    {
        _context = context;
        _productoService = productoService;
    }

    public async Task<RecepcionDto> CreateAsync(CrearRecepcionDto input, ulong? usuarioId, CancellationToken ct = default)
    {
        if (input.Items is null || input.Items.Count == 0)
            throw new InvalidOperationException("La recepción debe incluir al menos un item.");

        var empleadoId = await ResolverEmpleadoIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException(
                "No se pudo resolver el operador: el usuario autenticado no tiene un empleado activo vinculado.");

        var productoIds = input.Items.Select(i => i.ProductoId).Distinct().ToList();
        var productoById = await _context.Productos.AsNoTracking()
            .Where(p => productoIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ManejaGarrafaIndividual, p.CapacidadKg, p.Nombre })
            .ToDictionaryAsync(p => p.Id, ct);

        var estadoLlenaDepositoId = await _context.EstadosGarrafa.AsNoTracking()
            .Where(e => e.Codigo == GarrafaEstados.LlenaDeposito).Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (estadoLlenaDepositoId == 0) throw new InvalidOperationException("No se encontró el estado LLENA_DEPOSITO en el catálogo.");
        var tipoCompraId = await _context.TiposMovimientoGarrafa.AsNoTracking()
            .Where(t => t.Codigo == TipoMovimientoCompra).Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (tipoCompraId == 0) throw new InvalidOperationException("No se encontró el tipo de movimiento COMPRA en el catálogo.");

        // Validaciones previas: cantidad==codigos, dedupe, codigo existente, capacidad.
        foreach (var (item, idx) in input.Items.Select((it, i) => (it, i)))
        {
            if (!productoById.TryGetValue(item.ProductoId, out var producto))
                throw new InvalidOperationException($"Item {idx + 1}: el producto {item.ProductoId} no existe o está inactivo.");
            if (!producto.ManejaGarrafaIndividual) continue;

            if (decimal.Truncate(item.Cantidad) != item.Cantidad)
                throw new InvalidOperationException($"Item {idx + 1} ({producto.Nombre}): la cantidad debe ser entera para GARRAFA (recibido {item.Cantidad}).");

            var esperado = (int)item.Cantidad;
            var codigos = (item.CodigosGarrafa ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
            if (esperado == 0 && codigos.Count == 0) continue;
            if (esperado != codigos.Count)
                throw new InvalidOperationException($"Item {idx + 1} ({producto.Nombre}): esperaba {esperado} código(s) y recibió {codigos.Count}.");

            var dups = codigos.GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dups.Count > 0)
                throw new InvalidOperationException($"Item {idx + 1} ({producto.Nombre}): código(s) duplicado(s): {string.Join(", ", dups)}.");

            if (!producto.CapacidadKg.HasValue)
                throw new InvalidOperationException($"Item {idx + 1} ({producto.Nombre}): el producto GARRAFA no tiene capacidad_kg definida.");

            var existentes = await _context.Garrafas.IgnoreQueryFilters()
                .Where(g => codigos.Contains(g.Codigo)).Select(g => g.Codigo).ToListAsync(ct);
            if (existentes.Count > 0)
                throw new InvalidOperationException($"Item {idx + 1} ({producto.Nombre}): código(s) ya existente(s): {string.Join(", ", existentes)}.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var recepcion = new RecepcionProveedor
            {
                Fecha = input.Fecha, ProveedorId = input.ProveedorId, EmpleadoId = empleadoId,
                NumeroFacturaProveedor = input.NumeroFacturaProveedor,
                Subtotal = input.Subtotal, Descuento = input.Descuento, Total = input.Total,
                Observaciones = input.Observaciones,
                CreatedAt = now, UpdatedAt = now, CreatedBy = usuarioId, UpdatedBy = usuarioId,
            };
            _context.RecepcionesProveedor.Add(recepcion);
            await _context.SaveChangesAsync(ct);   // trigger llena recepcion.Numero

            foreach (var itemDto in input.Items)
            {
                _context.RecepcionItems.Add(new RecepcionItem
                {
                    RecepcionId = recepcion.Id, ProductoId = itemDto.ProductoId,
                    Cantidad = itemDto.Cantidad, PrecioUnitario = itemDto.PrecioUnitario,
                    CreatedAt = now, UpdatedAt = now,
                });
                await _context.SaveChangesAsync(ct);

                var producto = productoById[itemDto.ProductoId];
                if (!producto.ManejaGarrafaIndividual) continue;

                var capacidad = (byte)decimal.Truncate(producto.CapacidadKg!.Value);
                var codigosUnicos = (itemDto.CodigosGarrafa ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var codigo in codigosUnicos)
                {
                    _context.Garrafas.Add(new Garrafa
                    {
                        Codigo = codigo, CapacidadKg = capacidad,
                        ProveedorId = recepcion.ProveedorId, RecepcionId = recepcion.Id,
                        FechaCompra = DateOnly.FromDateTime(recepcion.Fecha),
                        EstadoGarrafaId = estadoLlenaDepositoId, Activo = true,
                        CreatedAt = now, UpdatedAt = now,
                        CreatedBy = usuarioId, UpdatedBy = usuarioId,
                    });
                    Garrafa garrafa;
                    try { await _context.SaveChangesAsync(ct); garrafa = _context.Garrafas.Local.Single(g => g.Codigo == codigo); }
                    catch (DbUpdateException dbex) when (dbex.InnerException is MySqlException my && my.Number == 1062)
                    { throw new InvalidOperationException($"Código duplicado detectado al guardar: {codigo}."); }

                    _context.MovimientosGarrafa.Add(new MovimientoGarrafa
                    {
                        GarrafaId = garrafa.Id, Fecha = recepcion.Fecha,
                        TipoMovimientoId = tipoCompraId, RecepcionId = recepcion.Id,
                        EstadoOrigenId = estadoLlenaDepositoId, EstadoDestinoId = estadoLlenaDepositoId,
                        EmpleadoId = empleadoId, Observaciones = $"Compra - {recepcion.Numero}",
                        CreatedBy = usuarioId,
                    });
                    await _context.SaveChangesAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
            return await LoadRecepcionWithLookupsAsync(recepcion.Id, ct)
                ?? throw new InvalidOperationException("La recepción se creó pero no pudo releerse.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> ReversarAsync(ulong recepcionId, ulong? usuarioId, CancellationToken ct = default)
    {
        // Reversión post-confirmación: solo procede si TODAS las garrafas
        // siguen en LLENA_DEPOSITO. Soft delete header + garrafas. Los
        // movimientos_garrafa NO se tocan (tabla append-only).
        var recepcion = await _context.RecepcionesProveedor.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == recepcionId, ct);
        if (recepcion is null || recepcion.DeletedAt is not null) return false;

        var estadosGarrafas = await (
            from g in _context.Garrafas.AsNoTracking()
            join e in _context.EstadosGarrafa.AsNoTracking() on g.EstadoGarrafaId equals e.Id
            where g.RecepcionId == recepcionId && g.DeletedAt == null
            select new { g.Codigo, EstadoCodigo = e.Codigo }
        ).ToListAsync(ct);

        var fueraDeDeposito = estadosGarrafas
            .Where(g => g.EstadoCodigo != GarrafaEstados.LlenaDeposito)
            .Select(g => $"{g.Codigo} ({g.EstadoCodigo})").ToList();
        if (fueraDeDeposito.Count > 0)
            throw new InvalidOperationException(
                $"No se puede revertir: {fueraDeDeposito.Count} garrafa(s) ya no están en LLENA_DEPOSITO. " +
                $"Detalle: {string.Join(", ", fueraDeDeposito)}.");

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            recepcion.DeletedAt = now; recepcion.UpdatedAt = now; recepcion.UpdatedBy = usuarioId;
            var garrafas = await _context.Garrafas
                .Where(g => g.RecepcionId == recepcionId && g.DeletedAt == null).ToListAsync(ct);
            foreach (var g in garrafas) { g.DeletedAt = now; g.Activo = false; g.UpdatedAt = now; g.UpdatedBy = usuarioId; }
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public Task<IEnumerable<ProductoDto>> GetProductosActivosAsync(CancellationToken ct = default)
        => _productoService.GetActivosAsync(ct);

    private async Task<ulong?> ResolverEmpleadoIdAsync(ulong? usuarioId, CancellationToken ct)
    {
        if (!usuarioId.HasValue) return null;
        return await _context.Empleados.AsNoTracking()
            .Where(e => e.UsuarioId == usuarioId.Value && e.Activo)
            .Select(e => (ulong?)e.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<RecepcionDto?> LoadRecepcionWithLookupsAsync(ulong recepcionId, CancellationToken ct)
    {
        var header = await _context.RecepcionesProveedor.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recepcionId, ct);
        if (header is null) return null;

        var proveedor = await (from r in _context.RecepcionesProveedor.AsNoTracking()
                               join p in _context.Proveedores.AsNoTracking() on r.ProveedorId equals p.Id
                               where r.Id == recepcionId select p.RazonSocial).FirstOrDefaultAsync(ct);
        var empleado = await (from r in _context.RecepcionesProveedor.AsNoTracking()
                              join e in _context.Empleados.AsNoTracking() on r.EmpleadoId equals e.Id
                              where r.Id == recepcionId select e.Apellido + ", " + e.Nombre).FirstOrDefaultAsync(ct);
        var items = await (from i in _context.RecepcionItems.AsNoTracking()
                           join p in _context.Productos.AsNoTracking() on i.ProductoId equals p.Id
                           where i.RecepcionId == recepcionId
                           orderby i.Id
                           select new RecepcionItemDto
                           {
                               Id = i.Id, RecepcionId = i.RecepcionId, ProductoId = i.ProductoId,
                               ProductoNombre = p.Nombre, ProductoCodigo = p.Codigo,
                               Cantidad = i.Cantidad, PrecioUnitario = i.PrecioUnitario,
                               Subtotal = i.Subtotal, ManejaGarrafaIndividual = p.ManejaGarrafaIndividual,
                           }).ToListAsync(ct);

        return new RecepcionDto
        {
            Id = header.Id, Numero = header.Numero, Fecha = header.Fecha,
            ProveedorId = header.ProveedorId, ProveedorNombre = proveedor,
            EmpleadoId = header.EmpleadoId, EmpleadoNombre = empleado,
            NumeroFacturaProveedor = header.NumeroFacturaProveedor,
            Subtotal = header.Subtotal, Descuento = header.Descuento, Total = header.Total,
            MontoPagado = header.MontoPagado, Saldo = header.Saldo,
            Observaciones = header.Observaciones, Items = items,
        };
    }
}