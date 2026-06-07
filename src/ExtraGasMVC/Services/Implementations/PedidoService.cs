using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExtraGasMVC.Services.Implementations;

public class PedidoService : IPedidoService
{
    private const string EstadosCacheKey = "estados_pedido_all";
    private const string CanalesCacheKey = "canales_venta_all";
    private const string MediosCacheKey = "medios_contacto_all";

    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Valid state transitions for pedidos, keyed by current state code.
    /// Business rule: PENDIENTE → CONFIRMADO or CANCELADO,
    /// CONFIRMADO → PENDIENTE, EN_PREPARACION, or CANCELADO,
    /// EN_PREPARACION → CONFIRMADO, ENTREGADO, or CANCELADO.
    /// Final states (ENTREGADO, CANCELADO) have no outgoing transitions.
    /// </summary>
    private static readonly Dictionary<string, string[]> TransicionesValidasPorCodigo = new()
    {
        [PedidoEstados.Pendiente]      = new[] { PedidoEstados.Confirmado, PedidoEstados.Cancelado },
        [PedidoEstados.Confirmado]     = new[] { PedidoEstados.Pendiente, PedidoEstados.EnPreparacion, PedidoEstados.Cancelado },
        [PedidoEstados.EnPreparacion]  = new[] { PedidoEstados.Confirmado, PedidoEstados.Entregado, PedidoEstados.Cancelado },
    };

    public PedidoService(ExtraGasDbContext context, IMapper mapper, IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
    }

    #region Queries

    public async Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var pedido = await GetWithIncludes()
            .AsNoTracking()
            .Include(p => p.Items)
                .ThenInclude(i => i.Producto)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return pedido is null ? null : _mapper.Map<PedidoDto>(pedido);
    }

    public async Task<SearchResultDto<PedidoDto>> SearchAsync(
        string? numero, ulong? estadoId, ulong? clienteId,
        DateTime? desde, DateTime? hasta,
        int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = GetWithIncludes()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(numero))
        {
            var n = numero.Trim();
            query = query.Where(p => (p.Numero ?? string.Empty).Contains(n));
        }

        if (estadoId.HasValue && estadoId.Value > 0)
            query = query.Where(p => p.EstadoPedidoId == estadoId.Value);

        if (clienteId.HasValue && clienteId.Value > 0)
            query = query.Where(p => p.ClienteId == clienteId.Value);

        if (desde.HasValue)
            query = query.Where(p => p.Fecha >= desde.Value);

        if (hasta.HasValue)
            query = query.Where(p => p.Fecha <= hasta.Value.Date.AddDays(1));

        var total = await query.CountAsync(ct);

        var pedidos = await query
            .OrderByDescending(p => p.Fecha)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new SearchResultDto<PedidoDto>
        {
            Items = _mapper.Map<List<PedidoDto>>(pedidos),
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio
        };
    }

    public async Task<IEnumerable<PedidoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var pedidos = await GetWithIncludes()
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var pedidos = await GetWithIncludes()
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        var pedidos = await GetWithIncludes()
            .AsNoTracking()
            .Where(p => p.EstadoPedidoId == estadoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default)
    {
        var pedidos = await GetWithIncludes()
            .AsNoTracking()
            .Where(p => p.Saldo > 0)
            .OrderBy(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoItemDto>> GetItemsByPedidoAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var items = await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .Where(i => i.PedidoId == pedidoId && i.DeletedAt == null)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoItemDto>>(items);
    }

    #endregion

    #region Commands

    public async Task<PedidoDto> CreateAsync(CreatePedidoDto pedidoDto, ulong? usuarioId, CancellationToken ct = default)
    {
        var estadoPendiente = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Codigo == PedidoEstados.Pendiente, ct)
            ?? throw new InvalidOperationException("No se encontró el estado PENDIENTE en el catálogo.");

        var pedido = _mapper.Map<Pedido>(pedidoDto);
        pedido.EstadoPedidoId = estadoPendiente.Id;
        pedido.Subtotal = 0;
        pedido.Descuento = 0;
        pedido.Total = 0;
        pedido.CreatedAt = DateTime.UtcNow;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.CreatedBy = usuarioId;
        pedido.UpdatedBy = usuarioId;

        _context.Pedidos.Add(pedido);
        await _context.SaveChangesAsync(ct);

        return (await GetByIdAsync(pedido.Id, ct))!;
    }

    public async Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedidoDto, ulong? usuarioId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoDto.Id }, ct);
        if (pedido == null)
            throw new KeyNotFoundException($"Pedido con Id {pedidoDto.Id} no encontrado.");

        // Business rule: final state orders cannot be edited
        var estadoActual = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EstadoPedidoId, ct);

        if (estadoActual is not null && PedidoEstados.EstadosFinales.Contains(estadoActual.Codigo))
            throw new InvalidOperationException($"No se puede editar un pedido en estado final ({estadoActual.Nombre}).");

        // Business rule: CONFIRMADO/EN_PREPARACION orders can only edit DireccionEntrega and Observaciones
        var isPartialEdit = estadoActual is not null && PedidoEstados.EstadosSoloLecturaParcial.Contains(estadoActual.Codigo);

        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

        if (isPartialEdit)
        {
            // Only allow editing delivery address and observations
            pedido.DireccionEntrega = pedidoDto.DireccionEntrega;
            pedido.Observaciones = pedidoDto.Observaciones;
            pedido.Descuento = pedidoDto.Descuento;
        }
        else
        {
            pedido.Fecha = pedidoDto.Fecha;
            pedido.FechaEntrega = pedidoDto.FechaEntrega;
            pedido.ClienteId = pedidoDto.ClienteId;
            pedido.EmpleadoId = pedidoDto.EmpleadoId;
            pedido.CanalVentaId = pedidoDto.CanalVentaId;
            pedido.MedioContactoId = pedidoDto.MedioContactoId;
            pedido.DireccionEntrega = pedidoDto.DireccionEntrega;
            pedido.Observaciones = pedidoDto.Observaciones;
            pedido.Descuento = pedidoDto.Descuento;
        }

        await RecalculateTotalsAsync(pedido.Id, ct);

        return (await GetByIdAsync(pedido.Id, ct))!;
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? usuarioId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        var tienePagos = await _context.Pagos
            .AsNoTracking()
            .AnyAsync(p => p.PedidoId == id && p.DeletedAt == null, ct);

        if (tienePagos)
            throw new InvalidOperationException("No se puede eliminar un pedido que tiene pagos registrados.");

        pedido.DeletedAt = DateTime.UtcNow;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? usuarioId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (pedido == null)
            return false;

        pedido.DeletedAt = null;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? usuarioId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { id }, ct);
        if (pedido == null)
            return false;

        if (pedido.EstadoPedidoId == nuevoEstadoId)
            return true;

        var estados = await _context.EstadosPedido
            .AsNoTracking()
            .ToListAsync(ct);

        var estadoActual = estados.FirstOrDefault(e => e.Id == pedido.EstadoPedidoId);
        var estadoDestino = estados.FirstOrDefault(e => e.Id == nuevoEstadoId);

        if (estadoActual is null)
            throw new InvalidOperationException("El estado actual del pedido no existe.");
        if (estadoDestino is null)
            throw new InvalidOperationException("El estado destino no existe.");

        if (PedidoEstados.EstadosFinales.Contains(estadoActual.Codigo))
            throw new InvalidOperationException($"No se puede cambiar el estado de un pedido en estado final ({estadoActual.Nombre}).");

        if (!TransicionesValidasPorCodigo.TryGetValue(estadoActual.Codigo, out var codigosPermitidos) ||
            !codigosPermitidos.Contains(estadoDestino.Codigo))
        {
            throw new InvalidOperationException(
                $"Transición no permitida: de '{estadoActual.Nombre}' a '{estadoDestino.Nombre}'.");
        }

        if (estadoDestino.Codigo == PedidoEstados.Cancelado)
        {
            if (string.IsNullOrWhiteSpace(motivoCancelacion))
                throw new InvalidOperationException("Debe ingresar un motivo de cancelación.");

            pedido.MotivoCancelacion = motivoCancelacion.Trim();
        }

        pedido.EstadoPedidoId = nuevoEstadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto itemDto, CancellationToken ct = default)
    {
        // Business rule: only PENDIENTE orders can have items added
        var pedido = await _context.Pedidos.FindAsync(new object[] { itemDto.PedidoId }, ct);
        if (pedido is null)
            throw new KeyNotFoundException($"Pedido con Id {itemDto.PedidoId} no encontrado.");

        var estadoPedido = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EstadoPedidoId, ct);

        if (estadoPedido is not null && estadoPedido.Codigo != PedidoEstados.Pendiente)
            throw new InvalidOperationException($"No se pueden agregar items en estado {estadoPedido.Nombre}. Solo se permite en estado Pendiente.");

        var tipoLinea = ParseTipoLinea(itemDto.TipoLinea);

        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == itemDto.ProductoId, ct);

        if (producto == null)
            throw new KeyNotFoundException($"Producto con Id {itemDto.ProductoId} no encontrado.");

        // Duplicate check — defensive; database unique constraint is the authoritative guard
        var yaExiste = await _context.PedidoItems
            .AsNoTracking()
            .AnyAsync(i => i.PedidoId == itemDto.PedidoId
                        && i.ProductoId == itemDto.ProductoId
                        && i.TipoLinea == tipoLinea
                        && i.DeletedAt == null, ct);

        if (yaExiste)
            throw new InvalidOperationException(
                $"El producto \"{producto.Nombre}\" ya está agregado al pedido con tipo {itemDto.TipoLinea}. " +
                $"Si necesita modificar la cantidad, elimine el item existente y vuelva a cargarlo.");

        var item = _mapper.Map<PedidoItem>(itemDto);
        item.PrecioUnitario = producto.PrecioActual;
        item.TipoLinea = tipoLinea;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            _context.PedidoItems.Add(item);
            await _context.SaveChangesAsync(ct);
            await RecalculateTotalsInternalAsync(pedido.Id, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"El producto \"{producto.Nombre}\" ya está agregado al pedido con tipo {itemDto.TipoLinea}. " +
                $"Si necesita modificar la cantidad, elimine el item existente y vuelva a cargarlo.");
        }

        return (await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .FirstOrDefaultAsync(i => i.Id == item.Id, ct)) is { } saved
            ? _mapper.Map<PedidoItemDto>(saved)
            : throw new InvalidOperationException("No se pudo recuperar el item creado.");
    }

    public async Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto itemDto, CancellationToken ct = default)
    {
        var item = await _context.PedidoItems.FindAsync(new object[] { itemDto.Id }, ct);
        if (item == null)
            throw new KeyNotFoundException($"Item con Id {itemDto.Id} no encontrado.");

        _mapper.Map(itemDto, item);
        item.TipoLinea = ParseTipoLinea(itemDto.TipoLinea);
        item.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        await RecalculateTotalsAsync(item.PedidoId, ct);

        return (await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .FirstOrDefaultAsync(i => i.Id == item.Id, ct)) is { } saved
            ? _mapper.Map<PedidoItemDto>(saved)
            : throw new InvalidOperationException("No se pudo recuperar el item actualizado.");
    }

    public async Task<bool> RemoveItemAsync(ulong itemId, CancellationToken ct = default)
    {
        var item = await _context.PedidoItems.FindAsync(new object[] { itemId }, ct);
        if (item == null)
            return false;

        // Business rule: only PENDIENTE orders can have items removed
        var pedido = await _context.Pedidos.FindAsync(new object[] { item.PedidoId }, ct);
        if (pedido is not null)
        {
            var estadoPedido = await _context.EstadosPedido
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == pedido.EstadoPedidoId, ct);

            if (estadoPedido is not null && estadoPedido.Codigo != PedidoEstados.Pendiente)
                throw new InvalidOperationException($"No se pueden eliminar items en estado {estadoPedido.Nombre}. Solo se permite en estado Pendiente.");
        }

        var pedidoId = item.PedidoId;

        // Soft-delete per project convention (AGENTS.md decision #6)
        item.DeletedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            await _context.SaveChangesAsync(ct);
            await RecalculateTotalsInternalAsync(pedidoId, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return true;
    }

    #endregion

    #region State & Lookups

    public async Task<List<EstadoPedidoDto>> GetTransicionesDisponiblesAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.EstadoPedido)
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

        if (pedido is null || pedido.EstadoPedido is null)
            return new List<EstadoPedidoDto>();

        var codigoActual = pedido.EstadoPedido.Codigo;
        if (!TransicionesValidasPorCodigo.TryGetValue(codigoActual, out var codigosPermitidos))
            return new List<EstadoPedidoDto>();

        var estados = await _context.EstadosPedido
            .AsNoTracking()
            .ToListAsync(ct);

        return estados
            .Where(e => codigosPermitidos.Contains(e.Codigo))
            .Select(e => _mapper.Map<EstadoPedidoDto>(e))
            .ToList();
    }

    public async Task<List<EstadoPedidoDto>> GetEstadosPedidoAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(EstadosCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var estados = await _context.EstadosPedido
                .AsNoTracking()
                .OrderBy(e => e.Nombre)
                .ToListAsync(ct);
            return _mapper.Map<List<EstadoPedidoDto>>(estados);
        }) ?? [];
    }

    public async Task<List<CanalVentaDto>> GetCanalesVentaAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(CanalesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var canales = await _context.CanalesVenta
                .AsNoTracking()
                .OrderBy(c => c.Nombre)
                .ToListAsync(ct);
            return _mapper.Map<List<CanalVentaDto>>(canales);
        }) ?? [];
    }

    public async Task<List<MedioContactoPedidoDto>> GetMediosContactoAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(MediosCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var medios = await _context.MediosContactoPedido
                .AsNoTracking()
                .OrderBy(m => m.Nombre)
                .ToListAsync(ct);
            return _mapper.Map<List<MedioContactoPedidoDto>>(medios);
        }) ?? [];
    }

    public async Task<IEnumerable<EmpleadoDto>> GetEmpleadosActivosAsync(CancellationToken ct = default)
    {
        var empleados = await _context.Empleados
            .AsNoTracking()
            .Where(e => e.Activo)
            .OrderBy(e => e.Apellido)
            .ThenBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EmpleadoDto>>(empleados);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Common include chain for Pedido queries. Reduces duplication across query methods.
    /// </summary>
    private IQueryable<Pedido> GetWithIncludes()
    {
        return _context.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido);
    }

    /// <summary>
    /// Recalculates Subtotal and Total for a pedido based on its active items.
    /// <para>
    /// Business rule for TipoLinea (per AGENTS.md decision #2):
    /// - VENTA: product sold to the customer → adds to total
    /// - ENTREGA: full gas cylinder delivered to customer → adds to total (represents the physical canje charge)
    /// - DEVOLUCION: empty cylinder returned by customer → subtracts from total (deposit refund)
    /// </para>
    /// <para>
    /// In the canje model, ENTREGA and VENTA serve the same financial purpose (charge).
    /// If the business rule changes, this method must be updated accordingly.
    /// </para>
    /// </summary>
    private async Task RecalculateTotalsAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var items = await _context.PedidoItems
            .AsNoTracking()
            .Where(i => i.PedidoId == pedidoId && i.DeletedAt == null)
            .ToListAsync(ct);

        var subtotal = CalculateSubtotal(items);
        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoId }, ct);
        if (pedido == null) return;

        pedido.Subtotal = subtotal;
        pedido.Total = subtotal - (subtotal * pedido.Descuento / 100m);
        pedido.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Same as RecalculateTotalsAsync but for use within an existing transaction.
    /// Does not create its own SaveChangesAsync — the caller manages the transaction.
    /// </summary>
    private async Task RecalculateTotalsInternalAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var items = await _context.PedidoItems
            .Where(i => i.PedidoId == pedidoId && i.DeletedAt == null)
            .ToListAsync(ct);

        var subtotal = CalculateSubtotal(items);
        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoId }, ct);
        if (pedido == null) return;

        pedido.Subtotal = subtotal;
        pedido.Total = subtotal - (subtotal * pedido.Descuento / 100m);
        pedido.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Computes subtotal from items: VENTA and ENTREGA add, DEVOLUCION subtracts.
    /// </summary>
    private static decimal CalculateSubtotal(List<PedidoItem> items)
    {
        var subtotal = 0m;
        foreach (var item in items)
        {
            var lineaSubtotal = item.Cantidad * item.PrecioUnitario;
            if (item.TipoLinea == TipoLinea.DEVOLUCION)
                subtotal -= lineaSubtotal;
            else
                subtotal += lineaSubtotal;
        }
        return subtotal;
    }

    private static TipoLinea ParseTipoLinea(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TipoLinea.VENTA;
        return Enum.TryParse<TipoLinea>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException(
                $"Tipo de línea inválido: '{value}'. Valores válidos: ENTREGA, DEVOLUCION, VENTA.");
    }

    /// <summary>
    /// Checks whether a DbUpdateException is a unique constraint violation (MySQL error 1062).
    /// Used to handle duplicate (PedidoId, ProductoId, TipoLinea) inserts gracefully.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var innerException = ex.InnerException;
        if (innerException is MySqlConnector.MySqlException mysqlEx)
            return mysqlEx.Number == 1062; // Duplicate entry
        return false;
    }

    #endregion
}