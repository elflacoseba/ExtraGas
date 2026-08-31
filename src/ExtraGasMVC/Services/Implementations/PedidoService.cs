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

    /// <summary>
    /// Internal context for <see cref="AplicarCanjeYConfirmarAsync"/>. Bundles
    /// the correlated parameters into a single value object so the method
    /// signature stays under SonarQube csharpsquid:S107 (≤ 7 params). Used only
    /// inside the canje-confirmacion flow; never crosses the service boundary.
    /// Issue #136.
    /// </summary>
    private sealed record CanjeConfirmacionContext(
        List<PedidoItem> Items,
        Dictionary<ulong, List<string>> CodigosPorItemLimpios,
        ulong PedidoId,
        Pedido Pedido,
        Dictionary<string, ulong> EstadoIdByCodigo,
        ulong EstadoConfirmadoId,
        ulong? UsuarioId);

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

    public async Task<PagedResult<PedidoDto>> SearchAsync(PedidoSearchFilter filter, CancellationToken ct = default)
    {
        var query = GetWithIncludes()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Numero))
        {
            var n = filter.Numero.Trim();
            query = query.Where(p => (p.Numero ?? string.Empty).Contains(n));
        }

        if (filter.EstadoId.HasValue && filter.EstadoId.Value > 0)
            query = query.Where(p => p.EstadoPedidoId == filter.EstadoId.Value);

        if (filter.ClienteId.HasValue && filter.ClienteId.Value > 0)
            query = query.Where(p => p.ClienteId == filter.ClienteId.Value);

        if (filter.Desde.HasValue)
            query = query.Where(p => p.Fecha >= filter.Desde.Value);

        if (filter.Hasta.HasValue)
            query = query.Where(p => p.Fecha <= filter.Hasta.Value.Date.AddDays(1));

        var total = await query.CountAsync(ct);

        var pedidos = await query
            .OrderByDescending(p => p.Fecha)
            .Skip((filter.Pagina - 1) * filter.Tamanio)
            .Take(filter.Tamanio)
            .ToListAsync(ct);

        return new PagedResult<PedidoDto>
        {
            Items = _mapper.Map<List<PedidoDto>>(pedidos),
            Total = total,
            Page = filter.Pagina,
            PageSize = filter.Tamanio
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

        // Issue #165: capturar el estado previo ANTES de pisar el entity.
        // Si capturáramos después, estadoAnteriorId == nuevoEstadoId y la
        // fila de historial diría "transición de X a X".
        var estadoAnteriorId = pedido.EstadoPedidoId;

        pedido.EstadoPedidoId = nuevoEstadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

        // Issue #165: append-only audit. Se inserta dentro del MISMO
        // SaveChangesAsync que persiste la mutación de pedido — si el
        // SaveChanges falla, ni el cambio de estado ni la fila de historial
        // quedan persistidas. Atomicidad garantizada por compartir el
        // SaveChanges (no hace falta transacción explícita acá).
        await RegistrarCambioEstadoAsync(id, estadoAnteriorId, nuevoEstadoId, usuarioId, motivoCancelacion, ct);

        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto item, CancellationToken ct = default)
    {
        // Issue #164: validación de estado extraída al helper compartido
        // EnsurePedidoEditableForItemsAsync. Garantiza que solo se muten
        // items en pedidos PENDIENTE (mismo contrato que Update/Remove).
        await EnsurePedidoEditableForItemsAsync(item.PedidoId, "agregar", ct);

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
            await RecalculateTotalsAsync(item.PedidoId, ct);
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

        // Issue #164: bloquear mutaciones de items en cualquier estado que no
        // sea PENDIENTE. La UI bloquea los inputs por estado, pero el endpoint
        // HTTP no enforce nada, así que esta validación es la única defensa
        // del lado del service. Garantiza además que RecalculateTotalsAsync
        // abajo no pise el Total de un pedido cerrado (ENTREGADO/CANCELADO),
        // manteniendo consistencia con el monto_pagado que mantiene el
        // trigger de pagos.
        await EnsurePedidoEditableForItemsAsync(entity.PedidoId, "modificar", ct);

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

        // Issue #164: validación de estado extraída al helper compartido.
        // Antes validaba inline con `if (pedido is not null)` y NO fallaba
        // cuando el pedido había sido borrado — alineado ahora con
        // AddItemAsync, que sí lanza KeyNotFoundException si el pedido no
        // existe. Defensa en profundidad: un RemoveItemAsync sobre un item
        // huérfano (pedido borrado) es un estado inconsistente que debe
        // rechazarse, no procesarse silenciosamente.
        await EnsurePedidoEditableForItemsAsync(item.PedidoId, "eliminar", ct);

        var pedidoId = item.PedidoId;

        // Issue #17: soft-delete per AGENTS.md convention #6. Antes este método
        // hacía hard-delete (_context.PedidoItems.Remove), lo cual perdía el
        // historial de qué productos tuvo un pedido. Ahora se setea
        // DeletedAt y el HasQueryFilter de PedidoItemConfiguration oculta la
        // fila de las queries por defecto (GetItemsByPedidoAsync,
        // RecalculateTotalsAsync, LoadItemsParaCanjeAsync, etc.). El
        // unique_hash generado en BD (migración 20260607_000003) cambia al
        // setear DeletedAt, así que el operador puede re-agregar el mismo
        // (producto, tipo_linea) sin chocar con la constraint única.
        item.DeletedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            await _context.SaveChangesAsync(ct);
            await RecalculateTotalsAsync(pedidoId, ct);
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

        // 3) Issue #145 Slice 4: validar que cada PedidoItem.ProductoId sigue
        // activo. Cubre el race "producto desactivado entre draft y confirm"
        // documentado en ADR #19 de db/docs/DECISIONES.md. Falla rápido con
        // InvalidOperationException nombrando el producto, antes de abrir
        // transacción. Cubre tanto el path canje (con codigosPorItem) como
        // el VENTA-only (ConfirmarSinCanjeAsync) porque se ejecuta antes del
        // fork.
        await ValidarProductosActivosAsync(pedidoId, ct);

        // 4) Resolver los IDs de los lookups que vamos a usar varias veces.
        var (estadoIdByCodigo, estadoConfirmadoId) =
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
        var ctx = new CanjeConfirmacionContext(
            Items: items,
            CodigosPorItemLimpios: codigosPorItemLimpios,
            PedidoId: pedidoId,
            Pedido: pedido,
            EstadoIdByCodigo: estadoIdByCodigo,
            EstadoConfirmadoId: estadoConfirmadoId,
            UsuarioId: usuarioId);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            await AplicarCanjeYConfirmarAsync(ctx, ct);

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
    /// Issue #145 Slice 4: revalida dentro del flujo de confirmación que cada
    /// producto de los <c>PedidoItem</c> del pedido sigue
    /// <c>Activo = true AND DeletedAt = null</c>. Cubre el race "producto
    /// desactivado entre draft y confirm" documentado en ADR #19 de
    /// <c>db/docs/DECISIONES.md</c>. Implementación en dos queries (no
    /// navegación) para esquivar el QueryFilter global de Producto: si
    /// proyectara la navegación <c>i.Producto!</c>, EF aplicaría el filtro
    /// <c>DeletedAt IS NULL</c> al JOIN y los soft-deleted desaparecerían
    /// del WHERE — la misma trampa que el bug original. Extrayendo los IDs
    /// primero y consultando después con <c>IgnoreQueryFilters()</c>
    /// detectamos ambos casos (Activo=false OR DeletedAt!=null). Falla
    /// rápido con un mensaje que nombra al producto para que el operador sepa
    /// qué refrescar del carrito. Se ejecuta antes de
    /// <see cref="LoadCatalogosParaCanjeAsync"/> y cubre tanto el path canje
    /// como el path VENTA-only (<see cref="ConfirmarSinCanjeAsync"/>).
    /// </summary>
    private async Task ValidarProductosActivosAsync(ulong pedidoId, CancellationToken ct)
    {
        // Query 1: extraer los ProductoId del pedido (sin filtrar — el
        // pedido ya está validado por LoadPedidoParaCanjeAsync).
        var productoIds = await _context.PedidoItems
            .AsNoTracking()
            .Where(i => i.PedidoId == pedidoId)
            .Select(i => i.ProductoId)
            .ToListAsync(ct);

        if (productoIds.Count == 0) return;

        // Query 2: detectar productos desactivados o soft-deleted SIN que el
        // QueryFilter global los oculte. IgnoreQueryFilters es la única forma
        // de ver DeletedAt != null acá.
        var productosInactivos = await _context.Productos
            .IgnoreQueryFilters()
            .Where(p => productoIds.Contains(p.Id) && (!p.Activo || p.DeletedAt != null))
            .Select(p => new { p.Id, p.Nombre })
            .ToListAsync(ct);

        if (productosInactivos.Count > 0)
        {
            var nombres = string.Join(", ", productosInactivos.Select(p => $"{p.Nombre} (id={p.Id})"));
            throw new InvalidOperationException(
                $"El producto {nombres} fue desactivado, refrescá el pedido");
        }
    }

    /// <summary>
    /// Resuelve los IDs de los lookups que vamos a usar varias veces en un
    /// solo viaje a la BD (estados de garrafa, estado CONFIRMADO). Valida en
    /// el mismo viaje que los tipos de movimiento ENTREGA_CLIENTE /
    /// DEVOLUCION_CLIENTE existan (la app los necesita pero solo por código,
    /// no por id directo).
    /// </summary>
    private async Task<(Dictionary<string, ulong> estadoIdByCodigo, ulong estadoConfirmadoId)>
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

        // Validación: chequeamos presencia por código (no necesitamos el id
        // porque el código mapea directo a constantes en la rama de canje).
        // La consulta ya filtra a los dos códigos esperados; si llegamos a 0
        // o 1, falta al menos uno en el catálogo.
        var tiposMovimientoCodigos = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == TipoMovimientoEntregaCliente
                     || t.Codigo == TipoMovimientoDevolucionCliente)
            .Select(t => t.Codigo)
            .ToListAsync(ct);

        if (!tiposMovimientoCodigos.Contains(TipoMovimientoEntregaCliente)
            || !tiposMovimientoCodigos.Contains(TipoMovimientoDevolucionCliente))
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

        return (estadoIdByCodigo, estadoConfirmadoId);
    }

    /// <summary>
    /// Caso degenerado: pedido sin items a canjear (solo VENTA / carbón / leña).
    /// La transición y la auditoría se aplican igual; no abrimos transacción
    /// porque no hay escrituras múltiples.
    /// </summary>
    private async Task<bool> ConfirmarSinCanjeAsync(
        Pedido pedido, ulong estadoConfirmadoId, ulong? usuarioId, CancellationToken ct)
    {
        // Issue #165: capturar estado previo antes de la mutación.
        var estadoAnteriorId = pedido.EstadoPedidoId;

        pedido.EstadoPedidoId = estadoConfirmadoId;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

        // Append-only audit. ConfirmarSinCanjeAsync no abre transacción
        // propia (es un único SaveChanges); el helper y la mutación del
        // pedido commitean atómicamente juntos.
        await RegistrarCambioEstadoAsync(pedido.Id, estadoAnteriorId, estadoConfirmadoId, usuarioId, motivo: null, ct);

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
    private async Task AplicarCanjeYConfirmarAsync(CanjeConfirmacionContext ctx, CancellationToken ct)
    {
        foreach (var item in ctx.Items)
        {
            var codigos = ctx.CodigosPorItemLimpios[item.Id];
            var tipoMovimientoCodigo = item.TipoLinea == TipoLinea.ENTREGA
                ? TipoMovimientoEntregaCliente
                : TipoMovimientoDevolucionCliente;

            var estadoDestinoCodigo = item.TipoLinea == TipoLinea.ENTREGA
                ? GarrafaEstados.EnCliente
                : GarrafaEstados.LlenaDeposito;

            var estadoDestinoId = ctx.EstadoIdByCodigo[estadoDestinoCodigo];
            var clienteIdParaCanje = item.TipoLinea == TipoLinea.ENTREGA
                ? (ulong?)ctx.Pedido.ClienteId
                : null;

            await AplicarCanjeDeItemAsync(
                codigos, ctx.PedidoId, tipoMovimientoCodigo,
                estadoDestinoId, clienteIdParaCanje, ctx.UsuarioId, ct);
        }

        // Issue #165: capturar estado previo antes de la mutación. La
        // transición a CONFIRMADO via canje puede partir de PENDIENTE
        // (caso normal) o EN_PREPARACION (post-#144), por eso leemos el
        // id vigente en el entity en lugar de hardcodear.
        var estadoAnteriorId = ctx.Pedido.EstadoPedidoId;

        ctx.Pedido.EstadoPedidoId = ctx.EstadoConfirmadoId;
        ctx.Pedido.UpdatedAt = DateTime.UtcNow;
        ctx.Pedido.UpdatedBy = ctx.UsuarioId;

        // Append-only audit. La transacción ambiente abierta por
        // RegistrarCanjePedidoAsync cubre este SaveChanges — si la fila de
        // historial falla por cualquier razón (FK violada, etc.), la
        // transacción rollbackea y el pedido queda en su estado original.
        await RegistrarCambioEstadoAsync(ctx.PedidoId, estadoAnteriorId, ctx.EstadoConfirmadoId, ctx.UsuarioId, motivo: null, ct);

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

    /// <summary>
    /// Issue #165: helper privado que registra una fila append-only en
    /// <c>pedido_estados_historico</c>. NO llama a <c>SaveChanges</c> por
    /// sí mismo — se invoca dentro del mismo <c>SaveChangesAsync</c> del
    /// caller para garantizar atomicidad con la mutación de
    /// <c>pedidos.estado_pedido_id</c>. Si el SaveChanges falla, ni el
    /// cambio de estado ni la fila de historial quedan persistidas.
    /// </summary>
    /// <param name="estadoAnteriorId">
    /// Id del estado del pedido ANTES de la transición. El caller debe
    /// capturar este valor de <c>pedido.EstadoPedidoId</c> ANTES de
    /// pisarlo, o la fila diría "transición de X a X".
    /// </param>
    /// <param name="motivo">
    /// Motivo de cancelación cuando aplica. Coincide con el valor
    /// persistido en <c>pedidos.motivo_cancelacion</c>; null en cualquier
    /// transición cuyo destino no sea CANCELADO.
    /// </param>
    private Task RegistrarCambioEstadoAsync(
        ulong pedidoId,
        ulong? estadoAnteriorId,
        ulong estadoNuevoId,
        ulong? usuarioId,
        string? motivo,
        CancellationToken ct = default)
    {
        _context.PedidoEstadosHistorico.Add(new PedidoEstadoHistorico
        {
            PedidoId = pedidoId,
            EstadoAnteriorId = estadoAnteriorId,
            EstadoNuevoId = estadoNuevoId,
            UsuarioId = usuarioId,
            MotivoCancelacion = motivo,
            CreatedAt = DateTime.UtcNow,
        });
        return Task.CompletedTask;
    }

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

    /// <summary>
    /// Issue #165: devuelve el historial append-only de cambios de estado
    /// del pedido, ordenado del más reciente al más antiguo. Cubre la
    /// timeline de <c>Pedidos/Details.cshtml</c> y el endpoint
    /// <c>/Pedidos/{id}/historial-estados</c>.
    ///
    /// El índice <c>idx_peh_pedido_created (pedido_id, created_at DESC)</c>
    /// cubre exactamente esta query. El <c>Id DESC</c> como tiebreaker
    /// garantiza orden estable cuando dos filas comparten timestamp (caso
    /// raro pero posible bajo clock skew entre la app y MySQL).
    /// </summary>
    public async Task<IEnumerable<PedidoEstadoHistoricoDto>> GetHistorialEstadosAsync(
        ulong pedidoId, CancellationToken ct = default)
    {
        var entries = await _context.PedidoEstadosHistorico
            .AsNoTracking()
            .Include(h => h.EstadoAnterior)
            .Include(h => h.EstadoNuevo)
            .Include(h => h.Usuario)
            .Where(h => h.PedidoId == pedidoId)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoEstadoHistoricoDto>>(entries);
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
    /// Issue #164: helper compartido por <see cref="AddItemAsync"/>,
    /// <see cref="UpdateItemAsync"/> y <see cref="RemoveItemAsync"/>.
    /// Garantiza que el pedido esté en estado PENDIENTE antes de cualquier
    /// mutación de items — la UI bloquea los inputs por estado, pero el
    /// endpoint HTTP no enforce nada, así que esta validación es la única
    /// defensa del lado del service.
    /// <para>
    /// Reemplaza tres bloques inline idénticos (uno por método) que
    /// repetían la misma consulta + el mismo mensaje de error. Centralizar
    /// además elimina la inconsistencia previa: <c>AddItemAsync</c> y
    /// <c>RemoveItemAsync</c> validaban, <c>UpdateItemAsync</c> no. Un
    /// POST directo al endpoint UpdateItem podía modificar cantidad /
    /// precio / tipo de línea de items en pedidos ENTREGADO o CANCELADO,
    /// e incluso pisar el <c>Total</c> vía <see cref="RecalculateTotalsAsync"/>.
    /// </para>
    /// </summary>
    /// <param name="pedidoId">Id del pedido a validar.</param>
    /// <param name="accionPasado">
    /// Verbo en pasado para el mensaje de error. Ej: "agregar", "modificar",
    /// "eliminar". Produce mensajes como:
    /// "No se pueden modificar items en estado Confirmado. Solo se permite
    /// en estado Pendiente." (mismo formato que el mensaje histórico).
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// Si el pedido no existe. <see cref="AddItemAsync"/> ya lo hacía;
    /// <see cref="RemoveItemAsync"/> antes NO fallaba en este caso — ahora
    /// se unifica el comportamiento en favor de defensa en profundidad (un
    /// Remove sobre un item huérfano es estado inconsistente, no algo a
    /// procesar silenciosamente).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si el pedido existe pero su estado no es PENDIENTE. La excepción se
    /// lanza solo si el estado se encontró en el catálogo
    /// <c>estados_pedido</c>; si el catálogo está corrupto (estadoId
    /// huérfano) se omite la validación para no bloquear operaciones
    /// legítimas con un mensaje confuso.
    /// </exception>
    private async Task EnsurePedidoEditableForItemsAsync(
        ulong pedidoId, string accionPasado, CancellationToken ct)
    {
        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoId }, ct);
        if (pedido is null)
            throw new KeyNotFoundException($"Pedido con Id {pedidoId} no encontrado.");

        var estadoPedido = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EstadoPedidoId, ct);

        if (estadoPedido is not null && estadoPedido.Codigo != PedidoEstados.Pendiente)
            throw new InvalidOperationException(
                $"No se pueden {accionPasado} items en estado {estadoPedido.Nombre}. Solo se permite en estado Pendiente.");
    }

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
    /// <para>
    /// Issue #17: la query aplica el <c>HasQueryFilter</c> de
    /// <c>PedidoItemConfiguration</c>, así que los items soft-deleted quedan
    /// fuera del cálculo. Antes del fix #17, además, este método no llamaba
    /// <c>SaveChangesAsync</c> cuando se invocaba desde dentro de una
    /// transacción (<c>AddItemAsync</c> / <c>RemoveItemAsync</c>), así que el
    /// subtotal quedaba desactualizado en BD hasta el próximo
    /// <c>UpdateAsync</c>. El <c>SaveChangesAsync</c> participa en la
    /// transacción ambiente si existe, así que es seguro llamarlo tanto
    /// standalone (vía <c>UpdateAsync</c>) como dentro de una transacción
    /// abierta por el caller.
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