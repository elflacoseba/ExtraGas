using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExtraGasMVC.Services.Implementations;

public class PedidoService : IPedidoService
{
    private const string EstadosCacheKey = "estados_pedido_all";
    private const string CanalesCacheKey = "canales_venta_all";
    private const string MediosCacheKey = "medios_contacto_all";

    private const string TipoMovimientoEntregaCliente = "ENTREGA_CLIENTE";
    private const string TipoMovimientoDevolucionCliente = "DEVOLUCION_CLIENTE";

    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;
    private readonly IGarrafaService _garrafaService;

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

    public PedidoService(
        ExtraGasDbContext context,
        IMapper mapper,
        IMemoryCache cache,
        IGarrafaService garrafaService)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
        _garrafaService = garrafaService;
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

    public async Task<PagedResult<PedidoDto>> SearchAsync(
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

        return new PagedResult<PedidoDto>
        {
            Items = _mapper.Map<List<PedidoDto>>(pedidos),
            Total = total,
            Page = pagina,
            PageSize = tamanio
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

    public async Task<PagedResult<PedidoDto>> GetByClienteAsync(ulong clienteId, int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = GetWithIncludes()
            .AsNoTracking()
            .Where(p => p.ClienteId == clienteId);

        var total = await query.CountAsync(ct);

        var pedidos = await query
            .OrderByDescending(p => p.Fecha)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new PagedResult<PedidoDto>
        {
            Items = _mapper.Map<List<PedidoDto>>(pedidos),
            Total = total,
            Page = pagina,
            PageSize = tamanio
        };
    }

    public async Task<PagedResult<PedidoDto>> GetByEstadoAsync(ulong estadoId, int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = GetWithIncludes()
            .AsNoTracking()
            .Where(p => p.EstadoPedidoId == estadoId);

        var total = await query.CountAsync(ct);

        var pedidos = await query
            .OrderByDescending(p => p.Fecha)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new PagedResult<PedidoDto>
        {
            Items = _mapper.Map<List<PedidoDto>>(pedidos),
            Total = total,
            Page = pagina,
            PageSize = tamanio
        };
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
            .Where(i => i.PedidoId == pedidoId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoItemDto>>(items);
    }

    #endregion

    #region Commands

    public async Task<PedidoDto> CreateAsync(CreatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default)
    {
        var estadoPendiente = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Codigo == PedidoEstados.Pendiente, ct)
            ?? throw new InvalidOperationException("No se encontró el estado PENDIENTE en el catálogo.");

        var entity = _mapper.Map<Pedido>(pedido);
        entity.EstadoPedidoId = estadoPendiente.Id;
        entity.Subtotal = 0;
        entity.Descuento = 0;
        entity.Total = 0;
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = usuarioId;
        entity.UpdatedBy = usuarioId;

        _context.Pedidos.Add(entity);
        await _context.SaveChangesAsync(ct);

        return (await GetByIdAsync(entity.Id, ct))!;
    }

    public async Task<PedidoDto> UpdateAsync(UpdatePedidoDto pedido, ulong? usuarioId, CancellationToken ct = default)
    {
        var entity = await _context.Pedidos.FindAsync(new object[] { pedido.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Pedido con Id {pedido.Id} no encontrado.");

        // Business rule: final state orders cannot be edited
        var estadoActual = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.EstadoPedidoId, ct);

        if (estadoActual is not null && PedidoEstados.EstadosFinales.Contains(estadoActual.Codigo))
            throw new InvalidOperationException($"No se puede editar un pedido en estado final ({estadoActual.Nombre}).");

        // Business rule: CONFIRMADO/EN_PREPARACION orders can only edit DireccionEntrega and Observaciones
        var isPartialEdit = estadoActual is not null && PedidoEstados.EstadosSoloLecturaParcial.Contains(estadoActual.Codigo);

        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;

        if (isPartialEdit)
        {
            // Only allow editing delivery address and observations
            entity.DireccionEntrega = pedido.DireccionEntrega;
            entity.Observaciones = pedido.Observaciones;
            entity.Descuento = pedido.Descuento;
        }
        else
        {
            entity.Fecha = pedido.Fecha;
            entity.FechaEntrega = pedido.FechaEntrega;
            entity.ClienteId = pedido.ClienteId;
            entity.EmpleadoId = pedido.EmpleadoId;
            entity.CanalVentaId = pedido.CanalVentaId;
            entity.MedioContactoId = pedido.MedioContactoId;
            entity.DireccionEntrega = pedido.DireccionEntrega;
            entity.Observaciones = pedido.Observaciones;
            entity.Descuento = pedido.Descuento;
        }

        await RecalculateTotalsAsync(entity.Id, ct);

        return (await GetByIdAsync(entity.Id, ct))!;
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

    public async Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default)
    {
        // Business rule: only PENDIENTE orders can have items added
        var pedido = await _context.Pedidos.FindAsync(new object[] { item.PedidoId }, ct);
        if (pedido is null)
            throw new KeyNotFoundException($"Pedido con Id {item.PedidoId} no encontrado.");

        var estadoPedido = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EstadoPedidoId, ct);

        if (estadoPedido is not null && estadoPedido.Codigo != PedidoEstados.Pendiente)
            throw new InvalidOperationException($"No se pueden agregar items en estado {estadoPedido.Nombre}. Solo se permite en estado Pendiente.");

        var tipoLinea = ParseTipoLinea(item.TipoLinea);

        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == item.ProductoId, ct);

        if (producto == null)
            throw new KeyNotFoundException($"Producto con Id {item.ProductoId} no encontrado.");

        // Duplicate check — defensive; database unique constraint is the authoritative guard
        var yaExiste = await _context.PedidoItems
            .AsNoTracking()
            .AnyAsync(i => i.PedidoId == item.PedidoId
                        && i.ProductoId == item.ProductoId
                        && i.TipoLinea == tipoLinea, ct);

        if (yaExiste)
            throw new InvalidOperationException(
                $"El producto \"{producto.Nombre}\" ya está agregado al pedido con tipo {item.TipoLinea}. " +
                $"Si necesita modificar la cantidad, elimine el item existente y vuelva a cargarlo.");

        var entity = _mapper.Map<PedidoItem>(item);
        entity.PrecioUnitario = producto.PrecioActual;
        entity.TipoLinea = tipoLinea;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            _context.PedidoItems.Add(entity);
            await _context.SaveChangesAsync(ct);
            await RecalculateTotalsInternalAsync(pedido.Id, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"El producto \"{producto.Nombre}\" ya está agregado al pedido con tipo {item.TipoLinea}. " +
                $"Si necesita modificar la cantidad, elimine el item existente y vuelva a cargarlo.");
        }

        return (await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .FirstOrDefaultAsync(i => i.Id == entity.Id, ct)) is { } saved
            ? _mapper.Map<PedidoItemDto>(saved)
            : throw new InvalidOperationException("No se pudo recuperar el item creado.");
    }

    public async Task<PedidoItemDto> UpdateItemAsync(UpdatePedidoItemDto item, CancellationToken ct = default)
    {
        var entity = await _context.PedidoItems.FindAsync(new object[] { item.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Item con Id {item.Id} no encontrado.");

        _mapper.Map(item, entity);
        entity.TipoLinea = ParseTipoLinea(item.TipoLinea);
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        await RecalculateTotalsAsync(entity.PedidoId, ct);

        return (await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .FirstOrDefaultAsync(i => i.Id == entity.Id, ct)) is { } saved
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

        // Hard-delete: pedido_items se borran físicamente al eliminar del detalle
        _context.PedidoItems.Remove(item);

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

    public async Task<bool> RegistrarCanjePedidoAsync(
        ulong pedidoId,
        Dictionary<ulong, List<string>> codigosPorItem,
        ulong? usuarioId,
        CancellationToken ct = default)
    {
        // 1) El pedido debe existir y no estar ya en CONFIRMADO.
        var pedido = await LoadPedidoParaCanjeAsync(pedidoId, ct);
        if (pedido is null)
            return false;

        // 2) Idempotencia: si ya hay movimientos para este pedido, rechazar.
        await AsegurarNoCanjeadoAsync(pedidoId, ct);

        // 3) Resolver los IDs de los lookups que vamos a usar varias veces.
        var (estadoIdByCodigo, tipoMovimientoIdByCodigo, estadoConfirmadoId) =
            await LoadCatalogosParaCanjeAsync(ct);

        // 4) Si no hay items a canjear (pedido solo VENTA / carbón / leña), el
        // método degenera en un simple cambio de estado.
        if (codigosPorItem is null || codigosPorItem.Count == 0)
        {
            return await ConfirmarSinCanjeAsync(pedido, estadoConfirmadoId, usuarioId, ct);
        }

        // 5) Cargar los items solicitados con su producto.
        var items = await LoadItemsParaCanjeAsync(pedidoId, codigosPorItem, ct);

        // 6) Defensa en profundidad: validar que los items son GARRAFA-capaces
        // y que su tipo de línea es ENTREGA/DEVOLUCION.
        ValidarItemsSonGarrafaCanjeable(items);

        // 7) Pre-validar cada código antes de escribir nada.
        var codigosPorItemLimpios = NormalizarYValidarCodigos(items, codigosPorItem);

        // 8) Segunda pasada: validar cada código contra existencia, estado y
        // cliente (en DEVOLUCION). Cargamos todas las garrafas en una sola query
        // por item para no hacer N+1.
        foreach (var item in items)
            await ValidarCodigosContraInventarioAsync(item, codigosPorItemLimpios[item.Id], pedido, ct);

        // 9) Validación completa. Transacción ambiente.
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            await AplicarCanjeYConfirmarAsync(
                items, codigosPorItemLimpios, pedidoId, pedido,
                estadoIdByCodigo, tipoMovimientoIdByCodigo, estadoConfirmadoId, usuarioId, ct);

            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Carga el pedido para el canje. Devuelve null si no existe. Falla con
    /// InvalidOperationException si ya está CONFIRMADO.
    /// </summary>
    private async Task<Pedido?> LoadPedidoParaCanjeAsync(ulong pedidoId, CancellationToken ct)
    {
        var pedido = await _context.Pedidos
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

        if (pedido is null)
            return null;

        if (pedido.EstadoPedidoId != 0)
        {
            var estadoActualCodigo = await _context.EstadosPedido
                .AsNoTracking()
                .Where(e => e.Id == pedido.EstadoPedidoId)
                .Select(e => e.Codigo)
                .FirstOrDefaultAsync(ct);

            if (estadoActualCodigo == PedidoEstados.Confirmado)
                throw new InvalidOperationException(
                    "El pedido ya se encuentra en estado CONFIRMADO. No se puede confirmar dos veces.");
        }

        return pedido;
    }

    /// <summary>
    /// Rechaza el canje si ya hay movimientos de garrafa para este pedido
    /// (idempotencia: el pedido ya pasó por canje y fue revertido a
    /// PENDIENTE — requiere un nuevo pedido, no re-confirmar el mismo).
    /// </summary>
    private async Task AsegurarNoCanjeadoAsync(ulong pedidoId, CancellationToken ct)
    {
        var yaCanjeado = await _context.MovimientosGarrafa
            .AsNoTracking()
            .AnyAsync(m => m.PedidoId == pedidoId, ct);

        if (yaCanjeado)
            throw new InvalidOperationException(
                "Este pedido ya tiene movimientos de canje registrados. " +
                "No se puede confirmar dos veces.");
    }

    /// <summary>
    /// Resuelve los IDs de los lookups que vamos a usar varias veces en un
    /// solo viaje a la BD (estados de garrafa, tipo de movimiento, estado CONFIRMADO).
    /// </summary>
    private async Task<(Dictionary<string, ulong> estadoIdByCodigo, Dictionary<string, ulong> tipoMovimientoIdByCodigo, ulong estadoConfirmadoId)>
        LoadCatalogosParaCanjeAsync(CancellationToken ct)
    {
        var lookupCodigos = new[]
        {
            GarrafaEstados.LlenaDeposito,
            GarrafaEstados.EnCliente,
            PedidoEstados.Confirmado
        };

        var catalogos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => lookupCodigos.Contains(e.Codigo))
            .Select(e => new { e.Id, e.Codigo })
            .ToListAsync(ct);
        var estadoIdByCodigo = catalogos.ToDictionary(e => e.Codigo, e => e.Id);

        var tiposMovimiento = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == TipoMovimientoEntregaCliente
                     || t.Codigo == TipoMovimientoDevolucionCliente)
            .Select(t => new { t.Id, t.Codigo })
            .ToListAsync(ct);
        var tipoMovimientoIdByCodigo = tiposMovimiento.ToDictionary(t => t.Codigo, t => t.Id);

        if (!tipoMovimientoIdByCodigo.ContainsKey(TipoMovimientoEntregaCliente)
            || !tipoMovimientoIdByCodigo.ContainsKey(TipoMovimientoDevolucionCliente))
        {
            throw new InvalidOperationException(
                "Faltan tipos de movimiento ENTREGA_CLIENTE / DEVOLUCION_CLIENTE en la base de datos.");
        }

        var estadoConfirmadoId = await _context.EstadosPedido
            .AsNoTracking()
            .Where(e => e.Codigo == PedidoEstados.Confirmado)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(ct);

        if (estadoConfirmadoId == 0)
            throw new InvalidOperationException(
                "No se encontró el estado CONFIRMADO en el catálogo estados_pedido.");

        return (estadoIdByCodigo, tipoMovimientoIdByCodigo, estadoConfirmadoId);
    }

    /// <summary>
    /// Caso degenerado: pedido sin items a canjear (solo VENTA / carbón / leña).
    /// La transición y la auditoría se aplican igual; no abrimos transacción
    /// porque no hay escrituras múltiples.
    /// </summary>
    private async Task<bool> ConfirmarSinCanjeAsync(
        Pedido pedido, ulong estadoConfirmadoId, ulong? usuarioId, CancellationToken ct)
    {
        pedido.EstadoPedidoId = estadoConfirmadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Carga los items solicitados con su producto. Esto valida que el
    /// item existe y pertenece al pedido. Lanza si hay items faltantes.
    /// </summary>
    private async Task<List<PedidoItem>> LoadItemsParaCanjeAsync(
        ulong pedidoId,
        Dictionary<ulong, List<string>> codigosPorItem,
        CancellationToken ct)
    {
        var itemIds = codigosPorItem.Keys.ToList();
        var items = await _context.PedidoItems
            .AsNoTracking()
            .Include(i => i.Producto)
            .Where(i => i.PedidoId == pedidoId && itemIds.Contains(i.Id))
            .ToListAsync(ct);

        if (items.Count != itemIds.Count)
        {
            var encontrados = items.Select(i => i.Id).ToHashSet();
            var faltantes = itemIds.Where(id => !encontrados.Contains(id)).ToList();
            throw new InvalidOperationException(
                $"Los siguientes items no pertenecen al pedido {pedidoId}: {string.Join(", ", faltantes)}.");
        }

        return items;
    }

    /// <summary>
    /// Defensa en profundidad: el controller solo debería enviar items
    /// GARRAFA-capaces con tipo ENTREGA/DEVOLUCION. Rechequearlo acá
    /// protege contra requests hand-crafted.
    /// </summary>
    private static void ValidarItemsSonGarrafaCanjeable(IEnumerable<PedidoItem> items)
    {
        foreach (var item in items)
        {
            if (item.Producto is null || !item.Producto.ManejaGarrafaIndividual)
                throw new InvalidOperationException(
                    $"El item {item.Id} ({item.Producto?.Nombre ?? "sin producto"}) no requiere tracking de garrafas y no puede participar en un canje.");

            if (item.TipoLinea is not (TipoLinea.ENTREGA or TipoLinea.DEVOLUCION))
                throw new InvalidOperationException(
                    $"El item {item.Id} tiene tipo de línea {item.TipoLinea}; solo ENTREGA o DEVOLUCION participan en el canje.");
        }
    }

    /// <summary>
    /// Normaliza los códigos físicos (trim, dedupe, descarte vacíos) y
    /// valida que la cantidad coincida con la cantidad esperada del item.
    /// </summary>
    private static Dictionary<ulong, List<string>> NormalizarYValidarCodigos(
        IEnumerable<PedidoItem> items,
        Dictionary<ulong, List<string>> codigosPorItem)
    {
        var codigosPorItemLimpios = new Dictionary<ulong, List<string>>(codigosPorItem.Count);

        foreach (var item in items)
        {
            if (!codigosPorItem.TryGetValue(item.Id, out var codigos) || codigos is null)
                throw new InvalidOperationException(
                    $"Faltan los códigos físicos para el item {item.Id} ({item.Producto!.Nombre}).");

            // Normalizar: trim + descartar vacíos + dedupe preservando orden.
            var limpios = codigos
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (item.Cantidad != Math.Truncate(item.Cantidad))
                throw new InvalidOperationException(
                    $"La cantidad del item {item.Id} ({item.Producto!.Nombre}) debe ser entera para productos con tracking de garrafas.");

            var cantidadEsperada = (int)item.Cantidad;
            if (limpios.Count != cantidadEsperada)
                throw new InvalidOperationException(
                    $"El item {item.Id} ({item.Producto!.Nombre}) esperaba {cantidadEsperada} código(s) y recibió {limpios.Count}.");

            codigosPorItemLimpios[item.Id] = limpios;
        }

        return codigosPorItemLimpios;
    }

    /// <summary>
    /// Segunda pasada: valida cada código contra existencia, estado y
    /// cliente (en DEVOLUCION). Carga todas las garrafas en una sola query
    /// por item para no hacer N+1.
    /// </summary>
    private async Task ValidarCodigosContraInventarioAsync(
        PedidoItem item, List<string> codigos, Pedido pedido, CancellationToken ct)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Where(g => codigos.Contains(g.Codigo))
            .Select(g => new { g.Id, g.Codigo, g.EstadoGarrafaId, g.ClienteId, EstadoCodigo = g.EstadoGarrafa!.Codigo })
            .ToListAsync(ct);

        var encontrados = garrafas.ToDictionary(g => g.Codigo, g => g, StringComparer.Ordinal);

        foreach (var codigo in codigos)
        {
            if (!encontrados.TryGetValue(codigo, out var garrafa))
                throw new InvalidOperationException(
                    $"El código '{codigo}' (item {item.Id}, {item.Producto!.Nombre}) no existe en el inventario de garrafas.");

            ValidarCodigoContraReglasDeCanje(codigo, garrafa, item, pedido);
        }
    }

    /// <summary>
    /// Aplica las reglas de canje a un código físico: ENTREGA exige
    /// LLENA_DEPOSITO, DEVOLUCION exige EN_CLIENTE y que la garrafa sea
    /// del mismo cliente del pedido.
    /// </summary>
    private static void ValidarCodigoContraReglasDeCanje(
        string codigo, dynamic garrafa, PedidoItem item, Pedido pedido)
    {
        if (item.TipoLinea == TipoLinea.ENTREGA)
        {
            if (garrafa.EstadoCodigo != GarrafaEstados.LlenaDeposito)
                throw new InvalidOperationException(
                    $"El código '{codigo}' no se puede entregar: está en estado {garrafa.EstadoCodigo}, se requiere {GarrafaEstados.LlenaDeposito}.");
            return;
        }

        // DEVOLUCION
        if (garrafa.EstadoCodigo != GarrafaEstados.EnCliente)
            throw new InvalidOperationException(
                $"El código '{codigo}' no se puede devolver: está en estado {garrafa.EstadoCodigo}, se requiere {GarrafaEstados.EnCliente}.");

        if (garrafa.ClienteId != pedido.ClienteId)
            throw new InvalidOperationException(
                $"El código '{codigo}' no se puede devolver a este pedido: pertenece al cliente {garrafa.ClienteId}, no al cliente del pedido ({pedido.ClienteId}).");
    }

    /// <summary>
    /// Aplica el canje delegando cada código en
    /// <see cref="IGarrafaService.RegistrarMovimientoPorCanjeAsync"/> y
    /// finalmente cambia el estado del pedido a CONFIRMADO.
    /// </summary>
    private async Task AplicarCanjeYConfirmarAsync(
        List<PedidoItem> items,
        Dictionary<ulong, List<string>> codigosPorItemLimpios,
        ulong pedidoId,
        Pedido pedido,
        Dictionary<string, ulong> estadoIdByCodigo,
        Dictionary<string, ulong> tipoMovimientoIdByCodigo,
        ulong estadoConfirmadoId,
        ulong? usuarioId,
        CancellationToken ct)
    {
        foreach (var item in items)
        {
            var codigos = codigosPorItemLimpios[item.Id];
            var tipoMovimientoCodigo = item.TipoLinea == TipoLinea.ENTREGA
                ? TipoMovimientoEntregaCliente
                : TipoMovimientoDevolucionCliente;

            var estadoDestinoCodigo = item.TipoLinea == TipoLinea.ENTREGA
                ? GarrafaEstados.EnCliente
                : GarrafaEstados.LlenaDeposito;

            var estadoDestinoId = estadoIdByCodigo[estadoDestinoCodigo];
            var clienteIdParaCanje = item.TipoLinea == TipoLinea.ENTREGA
                ? (ulong?)pedido.ClienteId
                : null;

            await AplicarCanjeDeItemAsync(
                codigos, pedidoId, tipoMovimientoCodigo,
                estadoDestinoId, clienteIdParaCanje, usuarioId, ct);
        }

        pedido.EstadoPedidoId = estadoConfirmadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Aplica los movimientos de canje de todos los códigos de un item.
    /// Resuelve los IDs de las garrafas (con tracking) en una sola query
    /// para evitar N+1 contra el servicio de garrafas.
    /// </summary>
    private async Task AplicarCanjeDeItemAsync(
        List<string> codigos,
        ulong pedidoId,
        string tipoMovimientoCodigo,
        ulong estadoDestinoId,
        ulong? clienteIdParaCanje,
        ulong? usuarioId,
        CancellationToken ct)
    {
        // Volvemos a buscar la garrafa SIN AsNoTracking porque
        // GarrafaService.RegistrarMovimientoPorCanjeAsync la va a mutar
        // (ClienteId). La query anterior usó AsNoTracking solo para
        // validar; acá la hacemos con tracking porque la mutación es
        // nuestra.
        var garrafasTracking = await _context.Garrafas
            .Where(g => codigos.Contains(g.Codigo))
            .ToDictionaryAsync(g => g.Codigo, g => g.Id, StringComparer.Ordinal, ct);

        foreach (var codigo in codigos)
        {
            var garrafaId = garrafasTracking[codigo];
            await _garrafaService.RegistrarMovimientoPorCanjeAsync(
                garrafaId,
                estadoDestinoId,
                clienteIdParaCanje,
                pedidoId,
                tipoMovimientoCodigo,
                usuarioId,
                ct);
        }
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
            .Where(i => i.PedidoId == pedidoId)
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
            .Where(i => i.PedidoId == pedidoId)
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