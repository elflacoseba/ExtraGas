using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ExtraGasMVC.Services.Implementations;

/// <summary>Confirma y reversa recepciones de proveedor (issue #45).</summary>
public class RecepcionService : IRecepcionService
{
    private const string TipoMovimientoCompra = "COMPRA";

    /// <summary>
    /// Proyección mínima de Producto usada durante la validación previa al commit.
    /// Evita arrastrar navegaciones ni columnas que no se necesitan en este flujo.
    /// </summary>
    private sealed record ProductoResumen(ulong Id, bool ManejaGarrafaIndividual, decimal? CapacidadKg, string Nombre);

    private readonly ExtraGasDbContext _context;
    private readonly IProductoService _productoService;

    public RecepcionService(ExtraGasDbContext context, IProductoService productoService)
    {
        _context = context;
        _productoService = productoService;
    }

    public async Task<RecepcionDto> CreateAsync(CrearRecepcionDto dto, ulong? usuarioId, CancellationToken ct = default)
    {
        if (dto.Items is null || dto.Items.Count == 0)
            throw new InvalidOperationException("La recepción debe incluir al menos un item.");

        // El Total NO viene del DTO (es derivado). Lo calcula el Service para
        // mantener la invariante contable aunque un cliente envíe un valor
        // distinto. RecepcionTotalRules valida que Subtotal >= Descuento.
        decimal total;
        try
        {
            total = RecepcionTotalRules.Calcular(dto.Subtotal, dto.Descuento);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        var empleadoId = await ResolverEmpleadoIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException(
                "No se pudo resolver el operador: el usuario autenticado no tiene un empleado activo vinculado.");

        var productoById = await LoadProductosByIdAsync(dto.Items, ct);
        var (estadoLlenaDepositoId, tipoCompraId) = await LoadCatalogosCompraAsync(ct);

        await ValidarItemsPreCommitAsync(dto.Items, productoById, ct);

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var recepcion = new RecepcionProveedor
            {
                Fecha = dto.Fecha, ProveedorId = dto.ProveedorId, EmpleadoId = empleadoId,
                NumeroFacturaProveedor = dto.NumeroFacturaProveedor,
                Subtotal = dto.Subtotal, Descuento = dto.Descuento, Total = total,
                Observaciones = dto.Observaciones,
                CreatedAt = now, UpdatedAt = now, CreatedBy = usuarioId, UpdatedBy = usuarioId,
            };
            _context.RecepcionesProveedor.Add(recepcion);
            await _context.SaveChangesAsync(ct);   // trigger llena recepcion.Numero

            foreach (var itemDto in dto.Items)
            {
                _context.RecepcionItems.Add(new RecepcionItem
                {
                    RecepcionId = recepcion.Id, ProductoId = itemDto.ProductoId,
                    Cantidad = itemDto.Cantidad, PrecioUnitario = itemDto.PrecioUnitario,
                    CreatedAt = now, UpdatedAt = now,
                });
                await _context.SaveChangesAsync(ct);

                if (!productoById[itemDto.ProductoId].ManejaGarrafaIndividual) continue;
                await CrearGarrafasYMovimientosAsync(
                    itemDto, recepcion, empleadoId, usuarioId,
                    productoById[itemDto.ProductoId], estadoLlenaDepositoId, tipoCompraId, ct);
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

    /// <summary>
    /// Resuelve los IDs de productos del dto en una sola query y los devuelve
    /// en un diccionario para evitar lookups repetidos en el bucle principal.
    /// Issue #145 Slice 4: filtra `Activo=true` además del QueryFilter global
    /// sobre <c>DeletedAt</c>. Sin el filtro, un producto desactivado por un
    /// admin (Activo=false, DeletedAt=null) pasaba el query y luego
    /// <see cref="ValidarItemsPreCommitAsync"/> no podía detectarlo. El
    /// invariante "producto en dropdown ⇒ Activo" es defended acá y
    /// documentado en ADR #19 de db/docs/DECISIONES.md.
    /// </summary>
    private async Task<Dictionary<ulong, ProductoResumen>> LoadProductosByIdAsync(
        IEnumerable<CrearRecepcionItemDto> items, CancellationToken ct)
    {
        var productoIds = items.Select(i => i.ProductoId).Distinct().ToList();
        var rows = await _context.Productos.AsNoTracking()
            .Where(p => productoIds.Contains(p.Id) && p.Activo)
            .Select(p => new ProductoResumen(p.Id, p.ManejaGarrafaIndividual, p.CapacidadKg, p.Nombre))
            .ToListAsync(ct);
        return rows.ToDictionary(p => p.Id);
    }

    /// <summary>
    /// Resuelve los IDs del estado LLENA_DEPOSITO y del tipo de movimiento
    /// COMPRA en una sola query cada uno. Falla rápido si faltan en el
    /// catálogo — son lookups obligatorios para registrar la recepción.
    /// </summary>
    private async Task<(ulong estadoLlenaDepositoId, ulong tipoCompraId)> LoadCatalogosCompraAsync(CancellationToken ct)
    {
        var estadoLlenaDepositoId = await _context.EstadosGarrafa.AsNoTracking()
            .Where(e => e.Codigo == GarrafaEstados.LlenaDeposito).Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (estadoLlenaDepositoId == 0)
            throw new InvalidOperationException("No se encontró el estado LLENA_DEPOSITO en el catálogo.");

        var tipoCompraId = await _context.TiposMovimientoGarrafa.AsNoTracking()
            .Where(t => t.Codigo == TipoMovimientoCompra).Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (tipoCompraId == 0)
            throw new InvalidOperationException("No se encontró el tipo de movimiento COMPRA en el catálogo.");

        return (estadoLlenaDepositoId, tipoCompraId);
    }

    /// <summary>
    /// Validaciones previas: cantidad==codigos, dedupe, codigo existente,
    /// capacidad. Falla rápido (sin escribir nada) ante cualquier error.
    /// </summary>
    private async Task ValidarItemsPreCommitAsync(
        IEnumerable<CrearRecepcionItemDto> items,
        Dictionary<ulong, ProductoResumen> productoById,
        CancellationToken ct)
    {
        foreach (var (item, idx) in items.Select((it, i) => (it, i)))
        {
            if (!productoById.TryGetValue(item.ProductoId, out var producto))
                throw new InvalidOperationException($"Item {idx + 1}: el producto {item.ProductoId} no existe o está inactivo.");

            if (!producto.ManejaGarrafaIndividual) continue;

            ValidarCantidadEntera(item, producto, idx);
            await ValidarCodigosGarrafaAsync(item, producto, idx, ct);
        }
    }

    /// <summary>
    /// Verifica que la cantidad sea entera para productos GARRAFA. La
    /// comparación se hace contra el truncate explícito para no confundir
    /// 2.0 (válido) con 2.5 (rechazado).
    /// </summary>
    private static void ValidarCantidadEntera(CrearRecepcionItemDto item, ProductoResumen producto, int idx)
    {
        if (decimal.Truncate(item.Cantidad) != item.Cantidad)
            throw new InvalidOperationException(
                $"Item {idx + 1} ({producto.Nombre}): la cantidad debe ser entera para GARRAFA (recibido {item.Cantidad}).");
    }

    /// <summary>
    /// Validaciones específicas de items GARRAFA: cantidad vs códigos,
    /// duplicados, capacidad_kg definida, códigos ya existentes en la BD.
    /// </summary>
    private async Task ValidarCodigosGarrafaAsync(
        CrearRecepcionItemDto item, ProductoResumen producto, int idx, CancellationToken ct)
    {
        var esperado = (int)item.Cantidad;
        var codigos = (item.CodigosGarrafa ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();

        if (esperado == 0 && codigos.Count == 0) return;
        if (esperado != codigos.Count)
            throw new InvalidOperationException(
                $"Item {idx + 1} ({producto.Nombre}): esperaba {esperado} código(s) y recibió {codigos.Count}.");

        var dups = codigos.GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dups.Count > 0)
            throw new InvalidOperationException(
                $"Item {idx + 1} ({producto.Nombre}): código(s) duplicado(s): {string.Join(", ", dups)}.");

        if (!producto.CapacidadKg.HasValue)
            throw new InvalidOperationException(
                $"Item {idx + 1} ({producto.Nombre}): el producto GARRAFA no tiene capacidad_kg definida.");

        var existentes = await _context.Garrafas.IgnoreQueryFilters()
            .Where(g => codigos.Contains(g.Codigo)).Select(g => g.Codigo).ToListAsync(ct);
        if (existentes.Count > 0)
            throw new InvalidOperationException(
                $"Item {idx + 1} ({producto.Nombre}): código(s) ya existente(s): {string.Join(", ", existentes)}.");
    }

    /// <summary>
    /// Inserta una Garrafa + su MovimientoGarrafa de COMPRA por cada código
    /// único del item. Asume que ya estamos dentro de la transacción del
    /// caller (no abre una nueva).
    /// </summary>
    private async Task CrearGarrafasYMovimientosAsync(
        CrearRecepcionItemDto itemDto,
        RecepcionProveedor recepcion,
        ulong empleadoId,
        ulong? usuarioId,
        ProductoResumen producto,
        ulong estadoLlenaDepositoId,
        ulong tipoCompraId,
        CancellationToken ct)
    {
        var capacidad = (byte)decimal.Truncate(producto.CapacidadKg!.Value);
        var codigosUnicos = (itemDto.CodigosGarrafa ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var codigo in codigosUnicos)
            await CrearUnaGarrafaConSuMovimientoAsync(
                codigo, capacidad, recepcion, empleadoId, usuarioId,
                estadoLlenaDepositoId, tipoCompraId, ct);
    }

    /// <summary>
    /// Inserta una sola garrafa y su movimiento de COMPRA atado a la
    /// recepción. Captura el error 1062 de MySQL (duplicado) que se le haya
    /// escapado a la validación previa y lo traduce a un mensaje claro.
    /// </summary>
    private async Task CrearUnaGarrafaConSuMovimientoAsync(
        string codigo,
        byte capacidad,
        RecepcionProveedor recepcion,
        ulong empleadoId,
        ulong? usuarioId,
        ulong estadoLlenaDepositoId,
        ulong tipoCompraId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        _context.Garrafas.Add(new Garrafa
        {
            Codigo = codigo,
            CapacidadKg = capacidad,
            ProveedorId = recepcion.ProveedorId,
            RecepcionId = recepcion.Id,
            FechaCompra = DateOnly.FromDateTime(recepcion.Fecha),
            EstadoGarrafaId = estadoLlenaDepositoId,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = usuarioId,
            UpdatedBy = usuarioId,
        });

        Garrafa garrafa;
        try
        {
            await _context.SaveChangesAsync(ct);
            garrafa = _context.Garrafas.Local.Single(g => g.Codigo == codigo);
        }
        catch (DbUpdateException dbex) when (dbex.InnerException is MySqlException my && my.Number == 1062)
        {
            throw new InvalidOperationException($"Código duplicado detectado al guardar: {codigo}.");
        }

        _context.MovimientosGarrafa.Add(new MovimientoGarrafa
        {
            GarrafaId = garrafa.Id,
            Fecha = recepcion.Fecha,
            TipoMovimientoId = tipoCompraId,
            RecepcionId = recepcion.Id,
            EstadoOrigenId = estadoLlenaDepositoId,
            EstadoDestinoId = estadoLlenaDepositoId,
            EmpleadoId = empleadoId,
            Observaciones = $"Compra - {recepcion.Numero}",
            CreatedBy = usuarioId,
        });
        await _context.SaveChangesAsync(ct);
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

    public async Task<IEnumerable<RecepcionDto>> GetRecientesAsync(int cantidad, CancellationToken ct = default)
    {
        // Issue #48: dropdown de Recepción en formularios de Garrafa. Filtramos
        // soft-deleted (una recepción reversada ya no debe ser elegible) y
        // ordenamos por fecha desc para que el operador vea primero lo más
        // reciente. Sin items: el selector solo necesita número + proveedor.
        if (cantidad <= 0) return Array.Empty<RecepcionDto>();

        var headers = await _context.RecepcionesProveedor.AsNoTracking()
            .Where(r => r.DeletedAt == null)
            .OrderByDescending(r => r.Fecha)
            .Take(cantidad)
            .ToListAsync(ct);

        if (headers.Count == 0) return Array.Empty<RecepcionDto>();

        var proveedorIds = headers.Select(h => h.ProveedorId).Distinct().ToList();
        var empleadoIds = headers.Select(h => h.EmpleadoId).Distinct().ToList();

        var proveedoresById = await _context.Proveedores.AsNoTracking()
            .Where(p => proveedorIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.RazonSocial, ct);

        var empleadosById = await _context.Empleados.AsNoTracking()
            .Where(e => empleadoIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => $"{e.Apellido}, {e.Nombre}", ct);

        return headers.Select(h => new RecepcionDto
        {
            Id = h.Id,
            Numero = h.Numero,
            Fecha = h.Fecha,
            ProveedorId = h.ProveedorId,
            ProveedorNombre = proveedoresById.TryGetValue(h.ProveedorId, out var pn) ? pn : null,
            EmpleadoId = h.EmpleadoId,
            EmpleadoNombre = empleadosById.TryGetValue(h.EmpleadoId, out var en) ? en : null,
            NumeroFacturaProveedor = h.NumeroFacturaProveedor,
            Subtotal = h.Subtotal,
            Descuento = h.Descuento,
            Total = h.Total,
            MontoPagado = h.MontoPagado,
            Saldo = h.Saldo,
            Observaciones = h.Observaciones,
            Items = new List<RecepcionItemDto>(),
        }).ToList();
    }

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