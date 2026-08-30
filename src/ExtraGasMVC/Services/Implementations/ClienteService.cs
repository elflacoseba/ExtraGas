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

    public ClienteService(ExtraGasDbContext context, IMapper mapper, IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _cache = cache;
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
        var clientes = await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Activo)
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

        if (soloActivos)
            query = query.Where(c => c.Activo);

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
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        var entity = _mapper.Map<Cliente>(cliente);
        // Issue #114: Activo y FechaAlta no vienen del DTO. Los setea el Service
        // porque son estado / audit trail, no datos de carga del operador.
        entity.Activo = true;
        entity.FechaAlta = DateOnly.FromDateTime(DateTime.UtcNow);
        entity.Dni = dniNormalizado;                // Issue #113
        entity.TelefonoPrincipal = telNormalizado!; // Issue #113 (es requerido, no null)
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = createdBy;
        entity.UpdatedBy = createdBy;

        _context.Clientes.Add(entity);
        await SaveOrThrowDuplicateDniAsync(ct);

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
            throw new InvalidOperationException("El DNI ingresado ya está registrado.");

        // Snapshot de Activo y FechaAlta ANTES del AutoMapper: el formulario
        // de Edit no debe poder modificar ninguno de los dos. Si el operador
        // los manda distintos (sea por bug del DTO, por curl o por form
        // antiguo en cache), los restauramos silenciosamente.
        var activoOriginal = entity.Activo;
        var fechaAltaOriginal = entity.FechaAlta;

        _mapper.Map(cliente, entity);
        entity.Dni = dniNormalizado;                                                     // Issue #113
        entity.TelefonoPrincipal = StringNormalizer.NormalizarTelefono(cliente.TelefonoPrincipal)!; // Issue #113
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;
        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal, fechaAltaOriginal);

        await SaveOrThrowDuplicateDniAsync(ct);

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

        cliente.DeletedAt = DateTime.UtcNow;
        cliente.Activo = false;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var cliente = await _context.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente == null)
            return false;

        cliente.DeletedAt = null;
        cliente.Activo = true;
        cliente.UpdatedAt = DateTime.UtcNow;
        cliente.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

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
            if (mapped is not null) throw mapped;
            throw;
        }
    }
}
