using AutoMapper;
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

    public PedidoService(ExtraGasDbContext context, IMapper mapper, IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PedidoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var pedido = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
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
        var query = _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
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
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
            .Where(p => p.EstadoPedidoId == estadoId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<PedidoDto>>(pedidos);
    }

    public async Task<IEnumerable<PedidoDto>> GetPendientesAsync(CancellationToken ct = default)
    {
        var pedidos = await _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Include(p => p.Empleado)
            .Include(p => p.EstadoPedido)
            .Include(p => p.CanalVenta)
            .Include(p => p.MedioContactoPedido)
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

    public async Task<PedidoDto> CreateAsync(CreatePedidoDto pedidoDto, ulong? usuarioId, CancellationToken ct = default)
    {
        var estadoPendiente = await _context.EstadosPedido
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Codigo == "PENDIENTE", ct)
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

        pedido.Fecha = pedidoDto.Fecha;
        pedido.FechaEntrega = pedidoDto.FechaEntrega;
        pedido.ClienteId = pedidoDto.ClienteId;
        pedido.EmpleadoId = pedidoDto.EmpleadoId;
        pedido.CanalVentaId = pedidoDto.CanalVentaId;
        pedido.MedioContactoId = pedidoDto.MedioContactoId;
        pedido.DireccionEntrega = pedidoDto.DireccionEntrega;
        pedido.Observaciones = pedidoDto.Observaciones;
        pedido.Descuento = pedidoDto.Descuento;
        pedido.UpdatedAt = DateTime.UtcNow;
        pedido.UpdatedBy = usuarioId;

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

        if (estadoActual.EsFinal)
            throw new InvalidOperationException($"No se puede cambiar el estado de un pedido en estado final ({estadoActual.Nombre}).");

        var transicionesValidas = TransicionesValidasPorCodigo;
        if (!transicionesValidas.TryGetValue(estadoActual.Codigo, out var codigosPermitidos) ||
            !codigosPermitidos.Contains(estadoDestino.Codigo))
        {
            throw new InvalidOperationException(
                $"Transición no permitida: de '{estadoActual.Nombre}' a '{estadoDestino.Nombre}'.");
        }

        if (estadoDestino.Codigo == "CANCELADO")
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

    private static readonly Dictionary<string, string[]> TransicionesValidasPorCodigo = new()
    {
        ["PENDIENTE"]      = new[] { "CONFIRMADO", "CANCELADO" },
        ["CONFIRMADO"]     = new[] { "PENDIENTE", "EN_PREPARACION", "CANCELADO" },
        ["EN_PREPARACION"] = new[] { "CONFIRMADO", "ENTREGADO", "CANCELADO" },
    };

    public async Task<PedidoItemDto> AddItemAsync(CreatePedidoItemDto itemDto, CancellationToken ct = default)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == itemDto.ProductoId, ct);

        if (producto == null)
            throw new KeyNotFoundException($"Producto con Id {itemDto.ProductoId} no encontrado.");

        var yaExiste = await _context.PedidoItems
            .AsNoTracking()
            .AnyAsync(i => i.PedidoId == itemDto.PedidoId
                        && i.ProductoId == itemDto.ProductoId
                        && i.TipoLinea == ParseTipoLinea(itemDto.TipoLinea), ct);

        if (yaExiste)
            throw new InvalidOperationException(
                $"El producto \"{producto.Nombre}\" ya está agregado al pedido con tipo {itemDto.TipoLinea}. " +
                $"Si necesita modificar la cantidad, elimine el item existente y vuelva a cargarlo.");

        var item = _mapper.Map<PedidoItem>(itemDto);
        item.PrecioUnitario = producto.PrecioActual;

        _context.PedidoItems.Add(item);
        await _context.SaveChangesAsync(ct);

        await RecalculateTotalsAsync(item.PedidoId, ct);

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

        var pedidoId = item.PedidoId;
        _context.PedidoItems.Remove(item);
        await _context.SaveChangesAsync(ct);
        await RecalculateTotalsAsync(pedidoId, ct);

        return true;
    }

    public async Task RecalculateTotalsAsync(ulong pedidoId, CancellationToken ct = default)
    {
        var items = await _context.PedidoItems
            .AsNoTracking()
            .Where(i => i.PedidoId == pedidoId)
            .ToListAsync(ct);

        var subtotal = 0m;
        foreach (var item in items)
        {
            var lineaSubtotal = item.Cantidad * item.PrecioUnitario;
            if (item.TipoLinea == TipoLinea.DEVOLUCION)
                subtotal -= lineaSubtotal;
            else
                subtotal += lineaSubtotal;
        }

        var pedido = await _context.Pedidos.FindAsync(new object[] { pedidoId }, ct);
        if (pedido == null) return;

        pedido.Subtotal = subtotal;
        pedido.Total = subtotal - (subtotal * pedido.Descuento / 100m);
        pedido.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
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

    private static TipoLinea ParseTipoLinea(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TipoLinea.VENTA;
        return Enum.TryParse<TipoLinea>(value, ignoreCase: true, out var parsed)
            ? parsed
            : TipoLinea.VENTA;
    }
}
