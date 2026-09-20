using System.Linq.Expressions;
using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
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
            // Issue #182 T08: escapar `%` y `_` del input antes de envolver
            // entre wildcards. Sin esto, buscar "%" devuelve TODAS las garrafas
            // porque % se interpreta como wildcard en SQL (y un POST
            // hand-crafted con codigo=% rompe la paginacion). Tercer argumento
            // = caracter de escape, soportado por MySQL (Pomelo) y por el
            // provider InMemory en EF Core 9.
            var sanitized = codigo.Trim()
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
            // EF.Functions.Like compila a un LIKE nativo de MySQL. La
            // collation utf8mb4_unicode_ci del schema ya hace la comparación
            // case-insensitive, así que no hace falta lower() en ambos lados.
            var pattern = $"%{sanitized}%";
            query = query.Where(g => EF.Functions.Like(g.Codigo, pattern, @"\"));
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

        // Issue #182 T02: validar que el estado destino exista en el catálogo
        // estados_garrafa. El trigger trg_garrafas_bi_validate cubre INSERT pero
        // sólo chequea RequiereCliente con un SIGNAL feo; acá damos un mensaje
        // accionable antes de que SaveChanges pueda fallar.
        var estadoDestino = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId)
            .Select(e => new { e.RequiereCliente })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"El estado con Id {garrafa.EstadoGarrafaId} no existe en el catálogo estados_garrafa.");

        if (estadoDestino.RequiereCliente && !garrafa.ClienteId.HasValue)
            throw new InvalidOperationException("El estado seleccionado requiere asignar un cliente.");

        // Issue #182 T05: si el operador especificó un ClienteId, validar que
        // exista y no esté soft-deleted. El query filter global de clientes ya
        // excluye los soft-deleted, así que AnyAsync alcanza.
        await ValidarClienteActivoAsync(garrafa.ClienteId, ct);

        // Issue #182 T10: mismo criterio para ProveedorId. La cobertura por
        // dropdown (que filtra Activos) no alcanza para un POST hand-crafted
        // con un id soft-deleted.
        await ValidarProveedorActivoAsync(garrafa.ProveedorId, ct);

        var entity = _mapper.Map<Garrafa>(garrafa);
        // Issue #114: Activo no viene del DTO. Lo setea el Service en true
        // porque es estado (soft-delete), no dato de carga del operador.
        entity.Activo = true;
        // Issue #182 T04: version arranca en 1 para que el primer UPDATE
        // (despues de la lectura) compare contra 1 e incremente a 2. Si
        // dejamos el default 0 de BD, la primera lectura ve 0 y el primer
        // UPDATE seria WHERE version=0, que matchea — funciona pero deja un
        // piso confuso al debugging. Empezar en 1 es consistente con el
        // backfill de la migration.
        entity.Version = 1;
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

        // Snapshot pre-mapper de los campos que Edit tiene prohibido modificar.
        // - Activo: solo cambia vía Delete (lo preserva GarrafaEditRules abajo).
        // - EstadoGarrafaId y ClienteId: solo cambian vía la acción dedicada
        //   "Cambiar estado", que registra un MovimientoGarrafa. Modificarlos
        //   desde Edit rompería la trazabilidad del módulo (#182 T01).
        //   Si difieren del valor actual, rechazamos el POST hand-crafted que
        //   intentaría esquivar esa validación.
        // - Version (#182 T04): token de concurrencia optimista. Lo
        //   incrementamos manualmente antes de SaveChanges; EF agrega el
        //   valor leido al WHERE del UPDATE.
        var activoOriginal = entity.Activo;
        var estadoAnterior = entity.EstadoGarrafaId;
        var clienteAnterior = entity.ClienteId;
        var versionOriginal = entity.Version;

        _mapper.Map(garrafa, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;
        entity.Version = versionOriginal + 1;
        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal);

        if (entity.EstadoGarrafaId != estadoAnterior || entity.ClienteId != clienteAnterior)
        {
            throw new InvalidOperationException(
                "El estado y el cliente de la garrafa sólo pueden modificarse desde la acción 'Cambiar estado', " +
                "que registra el movimiento en la tabla de trazabilidad.");
        }

        // Issue #182 T05: si por algún motivo válido quedó un ClienteId seteado,
        // validar que exista y no esté soft-deleted. La cobertura por dropdown
        // no alcanza para un POST hand-crafted con un id soft-deleted.
        await ValidarClienteActivoAsync(entity.ClienteId, ct);

        // Issue #182 T10: analogo para ProveedorId. UpdateAsync permite
        // cambiar el ProveedorId (no asi EstadoGarrafaId/ClienteId que estan
        // gateados por el backdoor de T01), asi que la validacion va aqui.
        await ValidarProveedorActivoAsync(entity.ProveedorId, ct);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Issue #182 T04: si dos operadores editan la misma garrafa a la
            // vez, el WHERE id=X AND version=original no matchea -> 0 filas
            // afectadas. Traducimos a InvalidOperationException (mismo canal
            // que las validaciones de T01/T02/T05) para que el Controller
            // renderice un mensaje claro en lugar de un 500.
            _logger.LogWarning(ex,
                "Garrafa {Id} ({Codigo}) — conflicto de concurrencia al actualizar por {UsuarioId}",
                entity.Id, entity.Codigo, usuarioId);
            throw new InvalidOperationException(
                $"La garrafa {entity.Codigo} fue modificada por otro operador mientras editabas. " +
                "Recargá la página y volvé a intentar.", ex);
        }
        catch (DbUpdateException dbex) when (dbex.InnerException is MySqlException my && my.Number == 1062)
        {
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafa.Codigo}.");
        }

        return _mapper.Map<GarrafaDto>(entity);
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, ulong estadoOrigenEsperadoId, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { id }, ct);
        if (garrafa == null)
            return false;

        // Issue #182 T03: deteccion temprana del race entre el read del
        // controller (ViewBag.Garrafa) y nuestro read. Si el estado leido por
        // el controller difiere del que tenemos aca, otro operador cambio la
        // garrafa en el medio. Rechazamos ANTES de cargar catalogos /
        // transicionar / abrir transaccion — sin escrituras, sin
        // MovimientoGarrafa fantasma, sin retry silencioso. El controller
        // muestra el mensaje y le pide al operador recargar.
        if (garrafa.EstadoGarrafaId != estadoOrigenEsperadoId)
        {
            throw new InvalidOperationException(
                $"La garrafa {garrafa.Codigo} fue modificada por otro operador mientras editabas " +
                $"(estado esperado: id={estadoOrigenEsperadoId}, estado actual: id={garrafa.EstadoGarrafaId}). " +
                "Recargá la página y volvé a intentar.");
        }

        // Cargar ambos extremos de la transición (origen y destino) en una sola
        // consulta para poder validar contra GarrafaTransiciones y contra las
        // reglas del catálogo (requiere_cliente, etc.). Usamos el estadoOrigen
        // del snapshot (estadoOrigenEsperadoId == entity.EstadoGarrafaId ya
        // validado arriba) para mantener una sola fuente de verdad entre lo
        // que vio el controller y lo que valida el service.
        var extremos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == estadoOrigenEsperadoId || e.Id == dto.NuevoEstadoId)
            .Select(e => new { e.Id, e.Codigo, e.RequiereCliente, e.Nombre })
            .ToListAsync(ct);

        var origen = extremos.FirstOrDefault(e => e.Id == estadoOrigenEsperadoId);
        var destino = extremos.FirstOrDefault(e => e.Id == dto.NuevoEstadoId);

        if (origen is null)
            throw new InvalidOperationException(
                $"El estado actual de la garrafa (id={estadoOrigenEsperadoId}) no existe en el catálogo estados_garrafa.");

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

        // Issue #182 T05: si se proporcionó un ClienteId en el DTO, validar que
        // exista y no esté soft-deleted. Alinea CambiarEstadoAsync con las
        // validaciones equivalentes de CreateAsync/UpdateAsync.
        await ValidarClienteActivoAsync(dto.ClienteId, ct);

        var tipoCambioEstadoId = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == "CAMBIO_ESTADO")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tipoCambioEstadoId == 0)
            throw new InvalidOperationException("No se encontró el tipo de movimiento CAMBIO_ESTADO en la base de datos.");

        // Issue #182 T04: snapshot del version antes de mutar la entity. Lo
        // incrementamos antes de SaveChanges; EF agrega el original al WHERE
        // del UPDATE. Si la fila fue tocada por otro operador entre el
        // FindAsync de arriba y el SaveChanges, el WHERE no matchea y EF
        // tira DbUpdateConcurrencyException que capturamos abajo.
        var versionOriginal = garrafa.Version;

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
            garrafa.Version = versionOriginal + 1;

            var movimiento = new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = DateTime.UtcNow,
                TipoMovimientoId = tipoCambioEstadoId,
                ClienteId = dto.ClienteId,
                EstadoOrigenId = estadoOrigenEsperadoId,
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
        catch (DbUpdateConcurrencyException ex)
        {
            // Issue #182 T04: concurrencia contra otra escritura concurrente
            // (ej. otro operador haciendo Cambiar estado o Delete entre
            // nuestro FindAsync y SaveChanges). Traducimos a
            // InvalidOperationException con el mismo mensaje claro que usamos
            // para T03 — el operador debe recargar y volver a intentar.
            _logger.LogWarning(ex,
                "Garrafa {Id} ({Codigo}) — conflicto de concurrencia al cambiar estado por {UsuarioId}",
                garrafa.Id, garrafa.Codigo, currentUserId);
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"La garrafa {garrafa.Codigo} fue modificada por otro operador mientras editabas. " +
                "Recargá la página y volvé a intentar.", ex);
        }
        catch (Exception ex)
        {
            // Issue #56: registrar el error antes del rollback para auditoría
            // y diagnóstico — sin este log, un fallo transaccional queda invisible
            // porque la excepción se re-lanza pero la causa queda enterrada en
            // logs internos de MySQL/EF que no llegan al operador.
            _logger.LogError(ex,
                "Error al cambiar estado de la garrafa {GarrafaId} (origen={EstadoOrigenId}, destino={EstadoDestinoId}). Se realiza rollback de la transacción.",
                id, estadoOrigenEsperadoId, dto.NuevoEstadoId);
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
        //
        // Issue #182 T04: incrementamos `version` para que el token de
        // concurrencia optimista detecte escrituras concurrentes entre el
        // FindAsync de arriba y este SaveChanges. El caller
        // (PedidoService.RegistrarCanjePedidoAsync) ya está dentro de su
        // propia transacción, así que no hace falta SaveChanges adicional.
        var versionOriginal = garrafa.Version;
        garrafa.ClienteId = clienteId;
        garrafa.UpdatedAt = DateTime.UtcNow;
        garrafa.UpdatedBy = usuarioId;
        garrafa.Version = versionOriginal + 1;

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

        try
        {
            // NO abrimos transacción propia: dependemos de la transacción ambiente
            // que abrió PedidoService.RegistrarCanjePedidoAsync. Si no hay una, EF
            // usa su SaveChanges implícito — suficiente para un solo movimiento.
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Issue #182 T04: si la garrafa fue modificada por otro flujo
            // entre el FindAsync y este SaveChanges, el WHERE version=original
            // no matchea. Re-lanzamos como InvalidOperationException para que
            // el caller (PedidoService) aborte la registracion del canje del
            // pedido entero — no tiene sentido registrar movimientos de una
            // garrafa que ya está en estado distinto al que asumimos al
            // recolectar los codigos.
            _logger.LogWarning(ex,
                "Garrafa {Id} ({Codigo}) — conflicto de concurrencia al registrar movimiento por canje (pedido {PedidoId})",
                garrafa.Id, garrafa.Codigo, pedidoId);
            throw new InvalidOperationException(
                $"La garrafa {garrafa.Codigo} fue modificada por otro operador mientras se procesaba el canje. " +
                "Recargá la página y volvé a intentar.", ex);
        }
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

    /// <summary>
    /// Valida que el cliente referenciado exista y no esté soft-deleted. La
    /// cobertura por dropdown (<c>GetActivosAsync</c>) no es suficiente porque
    /// un POST hand-crafted podría llegar con un id soft-deleted. El query
    /// filter global de clientes excluye los soft-deleted — <c>AnyAsync</c>
    /// alcanza. Issue #182 T05.
    /// </summary>
    private async Task ValidarClienteActivoAsync(ulong? clienteId, CancellationToken ct)
    {
        if (!clienteId.HasValue) return;

        var existe = await _context.Clientes
            .AsNoTracking()
            .AnyAsync(c => c.Id == clienteId.Value, ct);

        if (!existe)
            throw new InvalidOperationException(
                $"El cliente con Id {clienteId.Value} no existe o fue dado de baja.");
    }

    /// <summary>
    /// Valida que el proveedor referenciado exista y no esté soft-deleted.
    /// Mismo criterio que <see cref="ValidarClienteActivoAsync"/>: el query
    /// filter global de proveedores excluye los soft-deleted, asi que
    /// <c>AnyAsync</c> alcanza. Issue #182 T10.
    /// </summary>
    private async Task ValidarProveedorActivoAsync(ulong? proveedorId, CancellationToken ct)
    {
        if (!proveedorId.HasValue) return;

        var existe = await _context.Proveedores
            .AsNoTracking()
            .AnyAsync(p => p.Id == proveedorId.Value, ct);

        if (!existe)
            throw new InvalidOperationException(
                $"El proveedor con Id {proveedorId.Value} no existe o fue dado de baja.");
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

    public async Task<PagedResult<VGarrafaEnCliente>> GetEnClientesAsync(
        ulong? clienteId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Issue #182 T11: misma normalización defensiva que GetPagedAsync —
        // page y pageSize llegan del query string (no son confiables). Si el
        // usuario manda pageSize=10000 o page=-3, la query no debería
        // explotar ni devolver el universo entero.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // Issue #51: leemos v_garrafas_en_clientes (que ya filtra por estado
        // EN_CLIENTE y calcula dias_en_cliente en SQL). Pasamos el filtro
        // opcional de cliente al WHERE para respetar el comportamiento previo
        // del Controller (sin parámetro = todos; con parámetro = uno).
        var query = _context.VGarrafasEnClientes.AsNoTracking();

        if (clienteId.HasValue)
            query = query.Where(v => v.ClienteId == clienteId.Value);

        // Total antes de paginar — CountAsync traduce a SELECT COUNT(*)
        // sobre el WHERE aplicado, sin cargar filas. La vista ya excluye
        // soft-deleted por lo que el count coincide con lo que la UI muestra.
        var total = await query.CountAsync(ct);

        // Orden estable por GarrafaId para que la paginación sea
        // determinística entre requests. La vista no define un ORDER BY
        // propio, así que lo fijamos acá (el Id correlativo de inserción es
        // buen tiebreaker).
        var items = await query
            .OrderBy(v => v.GarrafaId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<VGarrafaEnCliente>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<ulong> GetEstadoIdByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        // Issue #182 T12: lookup puntual por código canónico. Reemplaza el
        // hardcode EstadoGarrafaId=1 en GarrafasController.Create para que
        // un reorden del seed no rompa el alta en silencio.
        //
        // Nota: si en el futuro hay alta concurrencia de altas, este lookup
        // podría cachearse en memoria (los códigos del catálogo son estáticos).
        // Por ahora no hace falta — el costo es 1 query AsNoTracking con
        // índice unique por codigo, y la cantidad de altas concurrentes es
        // despreciable.
        if (string.IsNullOrWhiteSpace(codigo))
            return 0;

        return await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Codigo == codigo)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(ct);
    }
}
