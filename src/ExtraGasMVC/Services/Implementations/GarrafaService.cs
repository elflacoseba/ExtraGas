using System.Linq.Expressions;
using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ExtraGasMVC.Services.Implementations;

public class GarrafaService : IGarrafaService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<GarrafaService> _logger;

    public GarrafaService(ExtraGasDbContext context, IMapper mapper, ILogger<GarrafaService> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        // Issue #47: cargamos EstadoGarrafa, Cliente y Proveedor para que la UI
        // muestre nombres en lugar de IDs (el mapping proyecta las navegaciones
        // a EstadoNombre/EstadoColor/ClienteNombre/ProveedorNombre).
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        // Issue #47: ver GetByIdAsync — mismas navegaciones para los nombres de UI.
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor)
            .FirstOrDefaultAsync(g => g.Codigo == codigo, ct);

        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default)
    {
        // Issue #47: navegaciones cargadas para que Index muestre nombres.
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        // Issue #47: Cliente se filtra por FK; igual cargamos la navegación para
        // que ClienteNombre llegue poblado sin importar filtros del EF.
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor)
            .Where(g => g.ClienteId == clienteId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        // Issue #47: EstadoGarrafa se filtra por FK; igual se incluye para
        // consistencia con el resto de los Get*.
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor)
            .Where(g => g.EstadoGarrafaId == estadoId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<PagedResult<GarrafaDto>> GetPagedAsync(
        string? codigo, byte? capacidad, int page = 1, int pageSize = 20,
        string sortBy = "codigo", string sortDir = "asc",
        CancellationToken ct = default)
    {
        // Normalización defensiva: page y pageSize llegan del query string
        // (no son confiables). Si el usuario manda pageSize=10000 o page=-3,
        // la query no debería explotar ni devolver el universo entero.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // Issue #52: filtrado y conteo en SQL, no en memoria como hacía
        // GetAllAsync + LINQ-to-Objects. Las navegaciones se cargan aquí
        // mismo (mismo patrón que el resto de los Get*) para que la UI
        // muestre nombres sin joins adicionales.
        IQueryable<Garrafa> query = _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Include(g => g.Cliente)
            .Include(g => g.Proveedor);

        if (!string.IsNullOrWhiteSpace(codigo))
        {
            // EF.Functions.Like compila a un LIKE nativo de MySQL. La
            // collation utf8mb4_unicode_ci del schema ya hace la comparación
            // case-insensitive, así que no hace falta lower() en ambos lados.
            var pattern = $"%{codigo.Trim()}%";
            query = query.Where(g => EF.Functions.Like(g.Codigo, pattern));
        }

        if (capacidad.HasValue)
            query = query.Where(g => g.CapacidadKg == capacidad.Value);

        // Total antes de paginar — CountAsync traduce a SELECT COUNT(*)
        // sobre el WHERE aplicado, sin cargar filas.
        var total = await query.CountAsync(ct);

        // Issue #53: ordenar por el campo pedido. Defaults seguros (cualquier
        // sortBy desconocido cae a "codigo", cualquier sortDir != "desc" a
        // "asc"). ThenBy(Id) en todos los casos = tiebreaker estable para paginación.
        var ordered = BuildOrderedQueryable(query, sortBy, sortDir);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<GarrafaDto>
        {
            Items = _mapper.Map<List<GarrafaDto>>(items),
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    /// <summary>
    /// Construye el IOrderedQueryable según los parámetros sortBy/sortDir.
    /// Cada case arma el ordenamiento en su tipo concreto para que EF genere
    /// SQL específico por campo — no usamos reflection ni expression trees
    /// dinámicas porque romperían la traducción a SQL.
    /// </summary>
    private static IOrderedQueryable<Garrafa> BuildOrderedQueryable(
        IQueryable<Garrafa> query, string sortBy, string sortDir)
    {
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "capacidad" => OrderByCampoOrId(query, g => g.CapacidadKg, desc),
            "estado" => OrderByCampoOrId(query, g => g.EstadoGarrafa!.Nombre, desc),
            // Cliente es nullable: ordenar por Apellido, luego Nombre. EF
            // traduce el acceso a navegación como LEFT JOIN; las filas sin
            // cliente quedan con NULL y MySQL las pone al inicio en ASC.
            "cliente" => desc
                ? query.OrderByDescending(g => g.Cliente!.Apellido)
                       .ThenByDescending(g => g.Cliente!.Nombre)
                       .ThenBy(g => g.Id)
                : query.OrderBy(g => g.Cliente!.Apellido)
                       .ThenBy(g => g.Cliente!.Nombre)
                       .ThenBy(g => g.Id),
            "fechacompra" => OrderByCampoOrId(query, g => g.FechaCompra, desc),
            // FechaUltimoMovimiento es DateTime? — los NULL van primero en ASC
            // (semántica de MySQL), lo cual es razonable para "último mov."
            "ultimomov" => OrderByCampoOrId(query, g => g.FechaUltimoMovimiento, desc),
            // "codigo" y default: campo principal + Id como tiebreaker.
            _ => OrderByCampoOrId(query, g => g.Codigo, desc),
        };
    }

    /// <summary>
    /// Helper común: ordena por la key provista y usa Id como tiebreaker
    /// estable. Reduce el switch anterior a una sola expresión por case.
    /// </summary>
    private static IOrderedQueryable<Garrafa> OrderByCampoOrId<TKey>(
        IQueryable<Garrafa> query, System.Linq.Expressions.Expression<Func<Garrafa, TKey>> keySelector, bool desc)
    {
        return desc
            ? query.OrderByDescending(keySelector).ThenBy(g => g.Id)
            : query.OrderBy(keySelector).ThenBy(g => g.Id);
    }

    public async Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default)
    {
        var estados = await _context.EstadosGarrafa
            .AsNoTracking()
            .OrderBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EstadoGarrafaDto>>(estados);
    }

    public async Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default)
    {
        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafa.Codigo, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafa.Codigo}.");

        var entity = _mapper.Map<Garrafa>(garrafa);
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = usuarioId;
        entity.UpdatedBy = usuarioId;

        _context.Garrafas.Add(entity);
        await SaveOrThrowDuplicateAsync(garrafa.Codigo, ct);

        return _mapper.Map<GarrafaDto>(entity);
    }

    public async Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default)
    {
        var entity = await _context.Garrafas.FindAsync(new object[] { garrafa.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Garrafa con Id {garrafa.Id} no encontrada.");

        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafa.Codigo && g.Id != garrafa.Id, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafa.Codigo}.");

        _mapper.Map(garrafa, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;

        await SaveOrThrowDuplicateAsync(garrafa.Codigo, ct);

        return _mapper.Map<GarrafaDto>(entity);
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { id }, ct);
        if (garrafa == null)
            return false;

        // Cargar ambos extremos de la transición (origen y destino) en una sola
        // consulta para poder validar contra GarrafaTransiciones y contra las
        // reglas del catálogo (requiere_cliente, etc.).
        var extremos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId || e.Id == dto.NuevoEstadoId)
            .Select(e => new { e.Id, e.Codigo, e.RequiereCliente, e.Nombre })
            .ToListAsync(ct);

        var origen = extremos.FirstOrDefault(e => e.Id == garrafa.EstadoGarrafaId);
        var destino = extremos.FirstOrDefault(e => e.Id == dto.NuevoEstadoId);

        if (origen is null)
            throw new InvalidOperationException(
                $"El estado actual de la garrafa (id={garrafa.EstadoGarrafaId}) no existe en el catálogo estados_garrafa.");

        if (destino is null)
            throw new InvalidOperationException(
                $"El estado destino solicitado (id={dto.NuevoEstadoId}) no existe en el catálogo estados_garrafa.");

        // Issue #40: validar la transición contra la matriz de estados.
        if (!GarrafaTransiciones.EsValida(origen.Codigo, destino.Codigo))
        {
            throw new InvalidOperationException(
                $"Transición inválida: {origen.Nombre} ({origen.Codigo}) → {destino.Nombre} ({destino.Codigo}). " +
                $"Consulte la matriz de transiciones válidas en la documentación del módulo Garrafas.");
        }

        // Si el estado destino exige un cliente (p.ej. EN_CLIENTE) el DTO debe traerlo.
        // El trigger trg_garrafas_bi_validate sólo cubre INSERT — para los cambios
        // de estado hechos por CAMBIO_ESTADO la validación la hace la app.
        if (destino.RequiereCliente && !dto.ClienteId.HasValue)
        {
            throw new InvalidOperationException(
                $"El estado {destino.Nombre} requiere seleccionar un cliente.");
        }

        var tipoCambioEstadoId = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == "CAMBIO_ESTADO")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tipoCambioEstadoId == 0)
            throw new InvalidOperationException("No se encontró el tipo de movimiento CAMBIO_ESTADO en la base de datos.");

        var estadoOrigen = garrafa.EstadoGarrafaId;

        // Resolver el empleado asociado al usuario autenticado (issue #43 -
        // auditoría completa de CambiarEstadoAsync). Si el usuario no tiene
        // empleado activo vinculado, el movimiento queda con EmpleadoId null
        // pero igual registra CreatedBy.
        ulong? empleadoId = null;
        if (currentUserId.HasValue)
        {
            empleadoId = await _context.Empleados
                .AsNoTracking()
                .Where(e => e.UsuarioId == currentUserId.Value && e.Activo)
                .Select(e => (ulong?)e.Id)
                .FirstOrDefaultAsync(ct);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            // NOTA: estado_garrafa_id y fecha_ultimo_movimiento NO se actualizan acá.
            // El trigger trg_mov_garrafa_ai los setea automáticamente desde el
            // movimiento (estado_destino_id y fecha) para mantener una sola fuente
            // de verdad. La app solo actualiza los campos que el trigger no toca.
            garrafa.ClienteId = dto.ClienteId;
            garrafa.UpdatedAt = DateTime.UtcNow;

            var movimiento = new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = DateTime.UtcNow,
                TipoMovimientoId = tipoCambioEstadoId,
                ClienteId = dto.ClienteId,
                EstadoOrigenId = estadoOrigen,
                EstadoDestinoId = dto.NuevoEstadoId,
                EmpleadoId = empleadoId,
                CreatedBy = currentUserId,
                Observaciones = dto.Observaciones
            };

            _context.MovimientosGarrafa.Add(movimiento);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return true;
        }
        catch (Exception ex)
        {
            // Issue #56: registrar el error antes del rollback para auditoría
            // y diagnóstico — sin este log, un fallo transaccional queda invisible
            // porque la excepción se re-lanza pero la causa queda enterrada en
            // logs internos de MySQL/EF que no llegan al operador.
            _logger.LogError(ex,
                "Error al cambiar estado de la garrafa {GarrafaId} (origen={EstadoOrigenId}, destino={EstadoDestinoId}). Se realiza rollback de la transacción.",
                id, estadoOrigen, dto.NuevoEstadoId);
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default)
    {
        // Solo necesitamos el código del estado actual para consultar la matriz;
        // hacerlo con un join manual evita tener que añadir una navigation
        // property a Garrafa (la entidad lo mantiene plano por convención).
        var estadoCodigo = await (
            from g in _context.Garrafas.AsNoTracking()
            join e in _context.EstadosGarrafa.AsNoTracking()
                on g.EstadoGarrafaId equals e.Id
            where g.Id == garrafaId
            select e.Codigo
        ).FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(estadoCodigo))
            return Array.Empty<EstadoGarrafaDto>();

        var codigosPermitidos = GarrafaTransiciones.DestinosPermitidos(estadoCodigo);
        if (codigosPermitidos.Count == 0)
            return Array.Empty<EstadoGarrafaDto>();

        // Filtra el catálogo por los códigos permitidos y mantiene el orden
        // alfabético que usa GetEstadosAsync para que la UI sea consistente.
        var destinos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => codigosPermitidos.Contains(e.Codigo))
            .OrderBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EstadoGarrafaDto>>(destinos);
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (garrafa == null)
            return false;

        var codigosBloqueados = new[] { "EN_CLIENTE", "EN_TRANSITO" };
        var estadoCodigo = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId)
            .Select(e => e.Codigo)
            .FirstOrDefaultAsync(ct);

        if (estadoCodigo != null && codigosBloqueados.Contains(estadoCodigo))
            throw new InvalidOperationException(
                $"No se puede eliminar una garrafa en estado {estadoCodigo}. Primero cambie su estado.");

        // Issue #54: registrar quién ejecuta la baja para auditoría (mismo
        // criterio que ClienteService.DeleteAsync).
        garrafa.DeletedAt = DateTime.UtcNow;
        garrafa.Activo = false;
        garrafa.UpdatedAt = DateTime.UtcNow;
        garrafa.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default)
    {
        // Primero verificamos que la garrafa exista (incluso soft-deleted) para
        // devolver 404 coherente desde el controller. Si no existe, enumerable vacío.
        var garrafaExiste = await _context.Garrafas
            .IgnoreQueryFilters()
            .AnyAsync(g => g.Id == garrafaId, ct);

        if (!garrafaExiste)
            return Array.Empty<MovimientoGarrafaDto>();

        // Joins manuales a las tablas de lookup para traer los nombres legibles.
        // Sigue el mismo patrón que GetTransicionesDisponiblesAsync (no hay navigation
        // properties confiables en la entidad, así que se hace el join a mano).
        // Pero como en este caso sí agregamos navigation properties a MovimientoGarrafa,
        // usamos Include para mantener la consistencia con GetByIdAsync/GetAllAsync.
        var movimientos = await _context.MovimientosGarrafa
            .AsNoTracking()
            .Include(m => m.TipoMovimiento)
            .Include(m => m.EstadoOrigen)
            .Include(m => m.EstadoDestino)
            .Include(m => m.Empleado)
            .Where(m => m.GarrafaId == garrafaId)
            .OrderByDescending(m => m.Fecha)
            .ThenByDescending(m => m.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<MovimientoGarrafaDto>>(movimientos);
    }

    public async Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default)
    {
        // Sin filtro por soft-delete: movimientos_garrafa no tiene deleted_at
        // (es log append-only). Si el pedido no existe o no tiene canje, enumerable vacío.
        var movimientos = await _context.MovimientosGarrafa
            .AsNoTracking()
            .Include(m => m.TipoMovimiento)
            .Include(m => m.EstadoOrigen)
            .Include(m => m.EstadoDestino)
            .Include(m => m.Empleado)
            .Include(m => m.Garrafa)
            .Where(m => m.PedidoId == pedidoId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<MovimientoGarrafaDto>>(movimientos);
    }

    public async Task RegistrarMovimientoPorCanjeAsync(
        ulong garrafaId,
        ulong estadoDestinoId,
        ulong? clienteId,
        ulong pedidoId,
        string tipoMovimientoCodigo,
        ulong? usuarioId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tipoMovimientoCodigo))
            throw new InvalidOperationException("El código de tipo de movimiento es obligatorio para registrar un canje.");

        // Lookup puntual de la garrafa (sin AsNoTracking — vamos a mutar ClienteId).
        var garrafa = await _context.Garrafas
            .FirstOrDefaultAsync(g => g.Id == garrafaId, ct)
            ?? throw new KeyNotFoundException($"Garrafa con Id {garrafaId} no encontrada.");

        // Cargamos origen, destino y tipo de movimiento en una sola query para
        // validar la transición contra GarrafaTransiciones y resolver el id
        // del tipo de movimiento. Mantiene el patrón de CambiarEstadoAsync.
        var catalogos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId || e.Id == estadoDestinoId)
            .Select(e => new { e.Id, e.Codigo, e.Nombre })
            .ToListAsync(ct);

        var origen = catalogos.FirstOrDefault(e => e.Id == garrafa.EstadoGarrafaId)
            ?? throw new InvalidOperationException(
                $"El estado actual de la garrafa (id={garrafa.EstadoGarrafaId}) no existe en el catálogo estados_garrafa.");

        var destino = catalogos.FirstOrDefault(e => e.Id == estadoDestinoId)
            ?? throw new InvalidOperationException(
                $"El estado destino solicitado (id={estadoDestinoId}) no existe en el catálogo estados_garrafa.");

        if (!GarrafaTransiciones.EsValida(origen.Codigo, destino.Codigo))
        {
            throw new InvalidOperationException(
                $"Transición inválida en canje: {origen.Nombre} ({origen.Codigo}) → {destino.Nombre} ({destino.Codigo}).");
        }

        var tipoMovimientoId = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == tipoMovimientoCodigo)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tipoMovimientoId == 0)
            throw new InvalidOperationException(
                $"No se encontró el tipo de movimiento {tipoMovimientoCodigo} en la base de datos.");

        // El trigger trg_mov_garrafa_ai se encarga de estado_garrafa_id y
        // fecha_ultimo_movimiento al hacer INSERT en movimientos_garrafa.
        // Acá solo actualizamos lo que el trigger no toca: cliente_id en la
        // garrafa (ENTREGA → cliente del pedido, DEVOLUCION → NULL).
        garrafa.ClienteId = clienteId;
        garrafa.UpdatedAt = DateTime.UtcNow;
        garrafa.UpdatedBy = usuarioId;

        var estadoOrigen = garrafa.EstadoGarrafaId;

        var movimiento = new MovimientoGarrafa
        {
            GarrafaId = garrafa.Id,
            Fecha = DateTime.UtcNow,
            TipoMovimientoId = tipoMovimientoId,
            PedidoId = pedidoId,
            RecepcionId = null,
            ClienteId = clienteId,
            EstadoOrigenId = estadoOrigen,
            EstadoDestinoId = estadoDestinoId,
            EmpleadoId = null,
            Observaciones = null,
            CreatedBy = usuarioId
        };

        _context.MovimientosGarrafa.Add(movimiento);

        // NO abrimos transacción propia: dependemos de la transacción ambiente
        // que abrió PedidoService.RegistrarCanjePedidoAsync. Si no hay una, EF
        // usa su SaveChanges implícito — suficiente para un solo movimiento.
        await _context.SaveChangesAsync(ct);
    }

    private async Task SaveOrThrowDuplicateAsync(string codigo, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbex) when (dbex.InnerException is MySqlException my && my.Number == 1062)
        {
            throw new InvalidOperationException($"Ya existe una garrafa con el código {codigo}.");
        }
    }

    public async Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default)
    {
        // Issue #51: leemos la vista v_stock_garrafas en vez de agrupar en
        // memoria. La vista ya excluye soft-deleted, agrupa por capacidad y
        // estado, y proyecta los nombres/colores del catálogo estados_garrafa,
        // por lo que el Controller puede renderizar badges sin joins extra.
        var query = _context.VStockGarrafas.AsNoTracking();

        var rows = await query.ToListAsync(ct);
        // La vista ordena por capacidad / estado_nombre, pero asegurar el orden
        // en la app para que la UI sea estable si la vista se redefine.
        return rows
            .OrderBy(r => r.CapacidadKg)
            .ThenBy(r => r.EstadoNombre);
    }

    public async Task<IEnumerable<VGarrafaEnCliente>> GetEnClientesAsync(ulong? clienteId, CancellationToken ct = default)
    {
        // Issue #51: leemos v_garrafas_en_clientes (que ya filtra por estado
        // EN_CLIENTE y calcula dias_en_cliente en SQL). Pasamos el filtro
        // opcional de cliente al WHERE para respetar el comportamiento previo
        // del Controller (sin parámetro = todos; con parámetro = uno).
        var query = _context.VGarrafasEnClientes.AsNoTracking();

        if (clienteId.HasValue)
            query = query.Where(v => v.ClienteId == clienteId.Value);

        return await query.ToListAsync(ct);
    }
}
