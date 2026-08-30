using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Exceptions;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

namespace ExtraGasMVC.Services.Implementations;

public class ClienteService : IClienteService
{
    private const string ProvinciasCacheKey = "provincias_all";
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;
    // Issue #116: ILogger para trazabilidad de operaciones de escritura y de
    // errores no esperados (ej. DbUpdateException por DNI duplicado que el
    // mapeo de errno 1062 cubre, o cualquier otra falla de BD). ASP.NET Core
    // registra ILogger<T> por convencion; no hace falta tocar Program.cs.
    private readonly ILogger<ClienteService> _logger;

    public ClienteService(
        ExtraGasDbContext context,
        IMapper mapper,
        IMemoryCache cache,
        ILogger<ClienteService> logger)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return cliente is null ? null : _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default)
    {
        var clientes = await _context.Clientes
            .AsNoTracking()
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ClienteDto>>(clientes);
    }

    public async Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Dni == dni, ct);

        return cliente is null ? null : _mapper.Map<ClienteDto>(cliente);
    }

    public async Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default)
    {
        // Issue #115: el flag `Activo` se eliminó. El DbSet `Clientes` ya
        // viene filtrado por el QueryFilter global (`DeletedAt == null`), así
        // que un `Where(c => c.Activo)` adicional sería redundante. El nombre
        // del método se conserva por compatibilidad con los callers
        // (Home/Pagos/Garrafas/Pedidos) — "clientes activos" = "no
        // soft-deleted".
        var clientes = await _context.Clientes
            .AsNoTracking()
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ClienteDto>>(clientes);
    }

    public async Task<PagedResult<ClienteDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Clientes
            .AsNoTracking()
            .AsQueryable();

        // Issue #115: el flag `Activo` se eliminó. El QueryFilter global ya
        // excluye soft-deleted, así que `soloActivos=true` es el estado
        // natural de la query. Mantenemos el parámetro por compatibilidad
        // con la firma pública y la query del Index (default true), pero
        // no se traduce a ningún filtro SQL — es un no-op legacy.
        _ = soloActivos;

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            // Issue #113: normalizamos query para que el operador pueda tipear
            // " 12345678 " o "+54 11 4455-6677" y matchear contra valores canónicos.
            // Asumimos que los DNIs/teléfonos en BD están normalizados (garantizado
            // por CreateAsync/UpdateAsync a partir de este fix). Datos viejos con
            // separadores en BD NO matchearán; por criterio de aceptación de la issue
            // esos registros conviven sin migrarse.
            var q = busqueda.Trim();
            var dniNormalizado = StringNormalizer.NormalizarDni(q) ?? q;
            var telNormalizado = StringNormalizer.NormalizarTelefono(q) ?? q;
            query = query.Where(c =>
                c.Nombre.Contains(q)
                || c.Apellido.Contains(q)
                || (c.Dni != null && c.Dni.Contains(dniNormalizado))
                || (c.CuitCuil != null && c.CuitCuil.Contains(q))
                || c.TelefonoPrincipal.Contains(telNormalizado));
        }

        var total = await query.CountAsync(ct);

        var clientes = await query
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new PagedResult<ClienteDto>
        {
            Items = _mapper.Map<List<ClienteDto>>(clientes),
            Total = total,
            Page = pagina,
            PageSize = tamanio
        };
    }

    public async Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default)
    {
        // Issue #113: normalizamos DNI y teléfono para que unicidad y storage
        // operen sobre el valor canónico (sin espacios, puntos ni guiones).
        var dniNormalizado = StringNormalizer.NormalizarDni(cliente.Dni);
        var telNormalizado = StringNormalizer.NormalizarTelefono(cliente.TelefonoPrincipal);

        if (!await IsDniUniqueAsync(dniNormalizado))
        {
            // Issue #116: pre-check rechazo por DNI duplicado. El nivel Warning
            // es el correcto: no es un error de sistema, es un input inválido
            // que el operador puede corregir. Sirve para detectar patrones
            // (ej. un script cargando duplicados) sin contaminar el alerting.
            _logger.LogWarning("CreateAsync rechazo por DNI duplicado {Dni}", dniNormalizado);
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");
        }

        var entity = _mapper.Map<Cliente>(cliente);
        // Issue #114 + #115: el DTO ya no expone FechaAlta ni Activo. El
        // Service setea FechaAlta con la fecha del alta (audit trail). El
        // flag `Activo` se eliminó de la entity: un cliente recién creado
        // está implícitamente "activo" porque `DeletedAt = null`.
        entity.FechaAlta = DateOnly.FromDateTime(DateTime.UtcNow);
        entity.Dni = dniNormalizado;                // Issue #113
        entity.TelefonoPrincipal = telNormalizado!; // Issue #113 (es requerido, no null)
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = createdBy;
        entity.UpdatedBy = createdBy;

        _context.Clientes.Add(entity);
        await SaveOrThrowDuplicateDniAsync(ct);

        // Issue #116: trazabilidad del alta. Loggeamos el Id (no el DNI) porque
        // el DNI puede ser null y ademas ya quedo auditado en la entity.
        _logger.LogInformation(
            "Cliente {ClienteId} creado por {CreatedBy}",
            entity.Id, createdBy);

        return _mapper.Map<ClienteDto>(entity);
    }

    public async Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default)
    {
        // Issue #136 (S6964): UpdateClienteDto.Id es nullable para evitar
        // under-posting silencioso desde forms manipulados. El Controller
        // ya devuelve 400 si Id == null, pero defendemos en profundidad
        // porque el Service puede invocarse desde tests o desde otros
        // callers que no pasaron por la validación del Controller.
        if (cliente.Id is null)
            throw new ArgumentException("UpdateClienteDto.Id es obligatorio.", nameof(cliente));
        var clienteId = cliente.Id.Value;

        // Issue #108: usamos IgnoreQueryFilters() para distinguir "no existe"
        // (KeyNotFoundException) de "existe pero está soft-deleted"
        // (ClienteSoftDeletedException). Antes, FindAsync respetaba el
        // QueryFilter global y devolvía null para los dos casos, lo que hacía
        // que el Controller no pudiera mostrarle al operador el mensaje
        // correcto.
        var entity = await _context.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == clienteId, ct);

        if (entity == null)
            throw new KeyNotFoundException($"Cliente con Id {clienteId} no encontrado.");

        if (entity.DeletedAt != null)
            throw new ClienteSoftDeletedException(clienteId);

        // Issue #113: normalizamos el DNI antes de validar unicidad y antes
        // de pisar el entity. Si el operador tipea " 12.345.678 " debe matchear
        // con el cliente cuyo DNI canónico es "12345678".
        var dniNormalizado = StringNormalizer.NormalizarDni(cliente.Dni);
        if (!await IsDniUniqueAsync(dniNormalizado, clienteId))
        {
            // Issue #116: ver CreateAsync. Mismo criterio: input invalido, no
            // falla de sistema. Loggeamos clienteId ademas del DNI para que el
            // operador entienda que estaba editando un cliente existente.
            _logger.LogWarning(
                "UpdateAsync rechazo por DNI duplicado {Dni} sobre cliente {ClienteId}",
                dniNormalizado, clienteId);
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");
        }

        // Snapshot de FechaAlta ANTES del AutoMapper: el formulario de Edit
        // no debe poder modificarlo. Issue #114. Si el operador lo manda
        // distinto (sea por bug del DTO, por curl o por form antiguo en
        // cache), lo restauramos silenciosamente. Issue #115: el flag
        // `Activo` ya no se preserva porque dejó de existir — el estado
        // del cliente se deriva de `DeletedAt`, que el Edit no toca.
        var fechaAltaOriginal = entity.FechaAlta;

        _mapper.Map(cliente, entity);
        entity.Dni = dniNormalizado;                                                     // Issue #113
        entity.TelefonoPrincipal = StringNormalizer.NormalizarTelefono(cliente.TelefonoPrincipal)!; // Issue #113
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;
        ClienteEditRules.PreservarFechaAlta(entity, fechaAltaOriginal);

        await SaveOrThrowDuplicateDniAsync(ct);

        // Issue #116: trazabilidad de la modificacion.
        _logger.LogInformation(
            "Cliente {ClienteId} actualizado por {UpdatedBy}",
            entity.Id, updatedBy);

        return _mapper.Map<ClienteDto>(entity);
    }

    /// <summary>
    /// Helper testeable: indica si el DNI es único entre los clientes que la query
    /// entregue. La query se construye fuera (típicamente con QueryFilter global
    /// activo, que filtra soft-deleted) y el helper solo evalúa la proyección.
    /// Marcado <c>internal</c> para que los tests lo consuman vía InternalsVisibleTo
    /// sin exponerlo al resto de la app.
    /// Issue #105: este helper es la pieza de lógica que el bug rompía a nivel BD
    /// (la app pasaba la validación pero el UNIQUE INDEX rechazaba el INSERT).
    /// </summary>
    internal static bool DniEsUnicoSobre(IQueryable<Cliente> clientes, string? dni)
    {
        // Issue #113: normalizamos el DNI recibido para que el chequeo de
        // unicidad evalúe contra el valor canónico almacenado en BD.
        var dniNormalizado = StringNormalizer.NormalizarDni(dni);
        if (string.IsNullOrWhiteSpace(dniNormalizado))
            return true;

        return !clientes.Any(c => c.Dni == dniNormalizado);
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null)
            return false;

        // Issue #115: solo se persiste `DeletedAt`. El flag `Activo` se
        // deriva de `DeletedAt IS NULL`, así que no hace falta tocarlo.
        cliente.DeletedAt = DateTime.UtcNow;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        // Issue #116: trazabilidad del soft-delete. No loggeamos el caso
        // "no encontrado" porque es un flujo esperado (404 de la papelera
        // cuando el operador hace doble click), no requiere investigación.
        _logger.LogInformation(
            "Cliente {ClienteId} soft-deleted por {UpdatedBy}",
            cliente.Id, updatedBy);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente == null)
            return false;

        // Issue #115: limpiar `DeletedAt` alcanza para reactivar — el flag
        // `Activo` se deriva de `DeletedAt IS NULL`. Mantener un set
        // explícito de `Activo = true` (como antes) sería sincronizar dos
        // fuentes de verdad, justo lo que este refactor elimina.
        cliente.DeletedAt = null;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        // Issue #116: trazabilidad del restore desde la papelera.
        _logger.LogInformation(
            "Cliente {ClienteId} reactivado por {UpdatedBy}",
            cliente.Id, updatedBy);

        return true;
    }

    /// <summary>
    /// Lista clientes soft-deleted para la pantalla /Clientes/Papelera.
    /// Issue #111: usa IgnoreQueryFilters() porque el QueryFilter global oculta
    /// los DeletedAt != null. Filtra adicionalmente por DeletedAt != null para
    /// quedarse solo con soft-deleted. Soporta busqueda con la misma logica que
    /// <see cref="SearchAsync"/> (Issue #113: normaliza DNI/telefono) para que
    /// el operador pueda encontrar un cliente especifico dentro de la papelera.
    /// </summary>
    public async Task<PagedResult<ClienteDto>> GetDeletedAsync(
        string? busqueda, int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Clientes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            // Issue #113: normalizamos query para que el operador pueda tipear
            // " 12345678 " o "+54 11 4455-6677" y matchear contra valores canónicos.
            var q = busqueda.Trim();
            var dniNormalizado = StringNormalizer.NormalizarDni(q) ?? q;
            var telNormalizado = StringNormalizer.NormalizarTelefono(q) ?? q;
            query = query.Where(c =>
                c.Nombre.Contains(q)
                || c.Apellido.Contains(q)
                || (c.Dni != null && c.Dni.Contains(dniNormalizado))
                || (c.CuitCuil != null && c.CuitCuil.Contains(q))
                || c.TelefonoPrincipal.Contains(telNormalizado));
        }

        var total = await query.CountAsync(ct);

        var clientes = await query
            .OrderBy(c => c.Apellido)
            .ThenBy(c => c.Nombre)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        return new PagedResult<ClienteDto>
        {
            Items = _mapper.Map<List<ClienteDto>>(clientes),
            Total = total,
            Page = pagina,
            PageSize = tamanio
        };
    }

    public async Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(ProvinciasCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            entry.SlidingExpiration = TimeSpan.FromMinutes(15);

            var provincias = await _context.Provincias
                .AsNoTracking()
                .OrderBy(p => p.Nombre)
                .ToListAsync(ct);

            return _mapper.Map<List<ProvinciaDto>>(provincias);
        }) ?? [];
    }

    /// <summary>
    /// Saldos agregados por cliente para /Clientes/CuentasCorrientes.
    /// Issue #109: la vista SQL <c>v_saldo_clientes</c> ya devuelve cliente +
    /// teléfono + pedidos pendientes + saldo en una sola fila agregada, así
    /// que acá solo proyectamos a DTO y ordenamos. AsNoTracking porque es
    /// read-only. El OrderByDescending es defensivo: la vista declara
    /// ORDER BY saldo_total DESC pero EF no garantiza preservarlo.
    /// </summary>
    public async Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default)
    {
        var saldos = await _context.VSaldosClientes
            .AsNoTracking()
            .OrderByDescending(v => v.SaldoTotal)
            .ThenBy(v => v.Cliente)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<VSaldoClienteDto>>(saldos);
    }

    private Task<bool> IsDniUniqueAsync(string? dni)
    {
        var query = _context.Clientes.AsNoTracking();
        return Task.FromResult(DniEsUnicoSobre(query, dni));
    }

    private Task<bool> IsDniUniqueAsync(string? dni, ulong excludeId)
    {
        var query = _context.Clientes.AsNoTracking().Where(c => c.Id != excludeId);
        return Task.FromResult(DniEsUnicoSobre(query, dni));
    }

    /// <summary>
    /// Helper testeable: si la <see cref="DbUpdateException"/> que envolvió
    /// SaveChangesAsync fue un duplicate entry de MySQL (errno 1062) sobre el
    /// índice único de DNI, devuelve la <see cref="InvalidOperationException"/>
    /// de dominio con el mismo mensaje que el check previo de unicidad para
    /// que la UX sea consistente. Si NO es 1062 (o no es MySqlException), devuelve
    /// <c>null</c> y el caller re-lanza la excepción original.
    ///
    /// Marcado <c>internal</c> para que los tests lo consuman vía InternalsVisibleTo
    /// sin exponer la lógica al resto de la app.
    ///
    /// Issue #107: defensa contra race condition en validación de unicidad de DNI.
    /// El check previo (IsDniUniqueAsync) es best-effort: dos requests concurrentes
    /// pueden pasarlo y la BD rechaza el segundo INSERT/UPDATE con 1062. Sin este
    /// mapeo, el Controller mostraría al usuario un error SQL crudo.
    /// </summary>
    internal static InvalidOperationException? MapDuplicateDniException(DbUpdateException ex)
    {
        if (ex.InnerException is MySqlException my && my.Number == 1062)
            return new InvalidOperationException("El DNI ingresado ya está registrado.");
        return null;
    }

    /// <summary>
    /// Envuelve <c>SaveChangesAsync</c> con la traducción de duplicate-DNI a
    /// <see cref="InvalidOperationException"/>. Cualquier otro error burbujea
    /// sin cambios para que el Controller lo registre como error genérico.
    /// Issue #116: loggea ambos paths (race condition de DNI duplicado y
    /// errores no esperados) para que un fallo intermitente quede trazado.
    /// </summary>
    private async Task SaveOrThrowDuplicateDniAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var mapped = MapDuplicateDniException(ex);
            if (mapped is not null)
            {
                // Race condition: dos requests pasaron el pre-check, el UNIQUE
                // INDEX rechazo al segundo. No es error de sistema pero queremos
                // medir frecuencia para detectar picos anómalos (ej. un script
                // cargando registros en paralelo).
                _logger.LogWarning(
                    ex,
                    "Race condition de DNI duplicado (errno 1062) al persistir cambios.");
                throw mapped;
            }
            // No es duplicate-DNI: error inesperado de BD. Lo loggeamos con el
            // stack completo antes de re-throw para que el caller pueda
            // registrar el error y nosotros tengamos el detalle abajo.
            _logger.LogError(
                ex,
                "DbUpdateException no esperada al persistir cliente.");
            throw;
        }
    }
}
