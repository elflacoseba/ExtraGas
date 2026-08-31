using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Exceptions;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExtraGasMVC.Services.Implementations;

public class ProductoService : IProductoService
{
    // Issue #147 item 1: clave de cache para GetTiposProductoAsync. El
    // catálogo tipos_producto es seed-only (decisión documentada en el
    // design #147 ADR #20 — pendiente de escritura en slice 3): no hay UI
    // CRUD, nadie lo modifica desde la app. La lista no cambia durante la
    // vida del proceso → cachear en memoria con TTL 1h es seguro y
    // elimina 1 query por request.
    private const string TiposProductoCacheKey = "tipos_producto";
    private static readonly TimeSpan TiposProductoCacheTtl = TimeSpan.FromHours(1);

    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    // Issue #145 Slice 2: ILogger<ProductoService> inyectado para trazabilidad
    // del restore (operación privilegiada, AdminOnly). Issue #146.7 lo extiende
    // a las 4 operaciones de escritura (Create/Update/Delete/Restore).
    private readonly ILogger<ProductoService> _logger;
    // Issue #147 item 1: IMemoryCache inyectado para envolver
    // GetTiposProductoAsync. AddMemoryCache() ya está registrado en
    // Program.cs:16, solo faltaba inyectar.
    private readonly IMemoryCache _cache;

    public ProductoService(
        ExtraGasDbContext context,
        IMapper mapper,
        ILogger<ProductoService> logger,
        IMemoryCache cache)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
        _cache = cache;
    }

    public async Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (producto is null) return null;

        var dto = _mapper.Map<ProductoDto>(producto);
        // Issue #147 item 4: auditoría visible en Details. El MappingProfile
        // deja CreatedByUserName/UpdatedByUserName en null (.Ignore()) y el
        // Service los resuelve explícitamente. Mismo patrón que
        // UsuarioService.LoadAuditUsersAsync (líneas 570-587).
        var auditUsers = await LoadAuditUsersAsync(new[] { producto }, ct);
        AplicarAudit(dto, producto, auditUsers);
        return dto;
    }

    public async Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        // Issue #147 item 6: normalizar el input igual que Create/Update.
        // La columna se persiste canónica (upper, sin espacios) así que el
        // lookup debe llegar canónico. La collation utf8mb4_unicode_ci del
        // schema hace la comparación case-insensitive, pero TrimAndUpper
        // también remueve espacios al borde — defensa en profundidad.
        var codigoNormalizado = StringNormalizer.TrimAndUpper(codigo);
        if (codigoNormalizado.Length == 0) return null;

        var producto = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .FirstOrDefaultAsync(p => p.Codigo == codigoNormalizado, ct);

        return producto is null ? null : _mapper.Map<ProductoDto>(producto);
    }

    public async Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .Where(p => p.Activo)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default)
    {
        var productos = await _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto)
            .Where(p => p.TipoProductoId == tipoProductoId)
            .OrderBy(p => p.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<ProductoDto>>(productos);
    }

    public async Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default)
    {
        // Issue #147 item 1: cache en memoria con TTL 1h. El catálogo
        // tipos_producto es seed-only (ADR #20 pendiente en slice 3) — no
        // hay UI CRUD que pueda invalidar el cache entre requests. TTL
        // absoluto (no sliding) porque la lógica de uso es "cargar al
        // startup y servir idéntico por 1h"; un sliding extendería el TTL
        // indefinidamente bajo uso sostenido.
        //
        // Nota forward-looking (issue #147 slice 3 / follow-up): si en el
        // futuro se agrega UI CRUD para TiposProducto, este cache key debe
        // evacuarse en Create/Update/Delete (escritura → RemoveAsync). Por
        // ahora la API no expone esos verbos — el catálogo es cerrado.
        return await _cache.GetOrCreateAsync(TiposProductoCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TiposProductoCacheTtl;

            var tipos = await _context.TiposProducto
                .AsNoTracking()
                .OrderBy(t => t.Nombre)
                .ToListAsync(ct);

            return (IEnumerable<TipoProductoDto>)_mapper.Map<List<TipoProductoDto>>(tipos);
        }) ?? [];
    }

    /// <summary>
    /// Paginación server-side del listado de Productos (issue #146.5).
    /// Reemplaza el patrón anterior (<c>GetAllAsync</c> + LINQ-to-Objects en
    /// Controller) que escaneaba toda la tabla y cargaba la navegación
    /// <c>TipoProducto</c> para todas las filas, escalando mal con catálogos
    /// grandes.
    /// </summary>
    public async Task<PagedResult<ProductoDto>> GetPagedAsync(
        string? busqueda,
        bool soloActivos,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Normalización defensiva: page y pageSize llegan del query string
        // (no son confiables). Mismo patrón que IGarrafaService.GetPagedAsync.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        IQueryable<Producto> query = _context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProducto);

        // Issue #146.5 + preservación de la UX existente: el checkbox "Solo
        // activos" del formulario filtra en SQL. Si el operador quiere ver
        // desactivados (caso de auditoría), el Controller pasa false.
        if (soloActivos)
            query = query.Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            // EF.Functions.Like compila a un LIKE nativo de MySQL. La
            // collation utf8mb4_unicode_ci del schema ya hace la comparación
            // case-insensitive para los 3 campos, pero normalizar el input
            // (trim + upper) garantiza consistencia con CreateAsync/UpdateAsync:
            // si el operador busca "gas", matchea tanto "GAS-10" como
            // "gas-10" porque el LIKE es bilateral.
            // Issue #147 item 6.
            var busquedaNormalizada = StringNormalizer.TrimAndUpper(busqueda);
            query = query.Where(p =>
                EF.Functions.Like(p.Codigo, $"%{busquedaNormalizada}%")
                || EF.Functions.Like(p.Nombre, $"%{busquedaNormalizada}%")
                || (p.Descripcion != null && EF.Functions.Like(p.Descripcion, $"%{busquedaNormalizada}%")));
        }

        // Total antes de paginar — CountAsync traduce a SELECT COUNT(*)
        // sobre el WHERE aplicado, sin cargar filas (mismo patrón que
        // GarrafaService.GetPagedAsync).
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Nombre)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductoDto>
        {
            Items = _mapper.Map<List<ProductoDto>>(items),
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default)
    {
        // Issue #146.3: validar regla de negocio GARRAFA ⇒ CapacidadKg > 0
        // ANTES de tocar la BD. Antes, un producto GARRAFA con CapacidadKg
        // null rompia tarde en RecepcionService.ValidarCodigosGarrafaAsync
        // con un mensaje opaco. Ahora se rechaza en el Service con un error
        // claro que el Controller traduce a ModelState.
        ProductoEditRules.ValidarGarrafaCapacidad(producto);

        // Issue #146.1: pre-check FK TipoProductoId. Sin esto, un
        // TipoProductoId inválido (form viejo en cache, integración rota,
        // bug de UI) explota a nivel MySQL con un FK error opaco que el
        // Controller envuelve en "No se pudo crear el producto".
        await ValidarTipoProductoExisteAsync(producto.TipoProductoId, ct);

        // Issue #146.2: pre-check de Codigo duplicado. El índice único
        // `uq_productos_codigo` cubre el caso, pero dos requests
        // concurrentes revientan a nivel BD con 500 "Duplicate entry".
        // El AnyAsync da un error legible con el camino del conflicto.
        await ValidarCodigoNoDuplicadoAsync(producto.Codigo, idAExcluir: null, ct);

        var entity = _mapper.Map<Producto>(producto);
        // Issue #147 item 6: normalizar Codigo en el borde del Service
        // (trim + upper). El DTO trae el valor crudo del form; la columna
        // se persiste canónica para cubrir el índice único
        // `uq_productos_codigo` y matchear búsquedas case-insensitive.
        entity.Codigo = StringNormalizer.TrimAndUpper(entity.Codigo);
        // Issue #114: Activo no viene del DTO. Lo setea el Service en true
        // porque es estado, no dato de carga del operador.
        entity.Activo = true;
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = usuarioId;
        entity.UpdatedBy = usuarioId;

        _context.Productos.Add(entity);
        await _context.SaveChangesAsync(ct);

        // Issue #146.7: trazabilidad operativa. Create es una operación
        // normal pero queremos reconstruir después quién dio de alta un
        // producto sensible (ej. GAS-10 en medio de un conflicto de
        // precios). Information, no Warning.
        _logger.LogInformation(
            "Producto {ProductoId} (codigo={Codigo}, nombre={Nombre}) creado por {UsuarioId}",
            entity.Id, entity.Codigo, entity.Nombre, usuarioId);

        return _mapper.Map<ProductoDto>(entity);
    }

    public async Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default)
    {
        // Issue #146.3: igual que CreateAsync, validar GARRAFA ⇒ CapacidadKg
        // > 0 sobre el DTO post-Map. Misma justificación: rechazar al
        // operador en el borde con un mensaje claro, no dejar que el bug
        // explote tarde en RecepcionService.
        ProductoEditRules.ValidarGarrafaCapacidad(producto);

        // Issue #146.1: pre-check FK antes del Update.
        await ValidarTipoProductoExisteAsync(producto.TipoProductoId, ct);

        // Issue #146.2: pre-check de Codigo duplicado. El `idAExcluir = Id`
        // es clave: si el operador está editando y deja su propio Codigo,
        // el AnyAsync no debe chocar contra sí mismo.
        await ValidarCodigoNoDuplicadoAsync(producto.Codigo, idAExcluir: producto.Id, ct);

        var entity = await _context.Productos.FindAsync(new object[] { producto.Id }, ct);
        if (entity == null)
            throw new KeyNotFoundException($"Producto con Id {producto.Id} no encontrado.");

        // Snapshot de Activo ANTES del AutoMapper: el formulario de Edit no
        // debe poder modificarlo. Si el operador lo manda distinto (sea por
        // bug del DTO, por curl o por form antiguo en cache), lo restauramos
        // silenciosamente. ManejaGarrafaIndividual NO se preserva — es config.
        var activoOriginal = entity.Activo;

        // Issue #145 Slice 3: snapshot del precio ANTES del AutoMapper para
        // detectar cambios reales. Se compara contra `entity.PrecioActual`
        // después del Map y se registra una fila append-only en
        // producto_precios_historico cuando hay cambio real. El guardado
        // `precioAnterior != 0` evita phantom rows en el primer update sobre
        // un producto recién creado con precio=0 (caso seed manual / backfill).
        var precioAnterior = entity.PrecioActual;

        // Issue #146.7: snapshot de las propiedades que el AutoMapper va a
        // pisar para emitir un log con los campos efectivamente cambiados.
        // Diferencia entre "el operador reenvió el form sin tocar nada" y
        // "el operador cambió el precio / estado / capacidad". Importante
        // para auditoría (issue #19: cambios concurrentes ya provocaron
        // last-write-wins silenciosos en otros módulos).
        var cambios = DetectarCambiosProducto(entity, producto);

        _mapper.Map(producto, entity);
        // Issue #147 item 6: normalizar Codigo (trim + upper) — mismo
        // tratamiento que CreateAsync. Mantiene invariante "Codigo
        // siempre canónico en BD" para que las búsquedas no dependan
        // de cómo tipeó el operador.
        entity.Codigo = StringNormalizer.TrimAndUpper(entity.Codigo);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = usuarioId;
        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal);

        // Hook de histórico: solo cuando hay cambio real (precioAnterior != 0
        // && precioAnterior != nuevo). Atómico: la fila append-only y el
        // update del producto commitean en el mismo SaveChangesAsync. Si
        // SaveChangesAsync falla, no queda fila huérfana.
        var precioNuevo = entity.PrecioActual;
        if (precioAnterior != precioNuevo && precioAnterior != 0m)
        {
            _context.ProductoPreciosHistorico.Add(new ProductoPrecioHistorico
            {
                ProductoId = entity.Id,
                PrecioAnterior = precioAnterior,
                PrecioNuevo = precioNuevo,
                MotivoCambioPrecio = producto.MotivoCambioPrecio,
                ChangedBy = usuarioId,
                ChangedAt = DateTime.UtcNow,
            });

            var motivoCambioPrecioLog = (producto.MotivoCambioPrecio ?? "<sin motivo>")
                .Replace("\r", " ")
                .Replace("\n", " ");

            _logger.LogInformation(
                "Producto {ProductoId} cambió de precio: {PrecioAnterior} → {PrecioNuevo} (motivo: {Motivo}, operador: {ChangedBy})",
                entity.Id, precioAnterior, precioNuevo, motivoCambioPrecioLog, usuarioId);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Issue #146.4: concurrencia optimista. Si dos operadores
            // editan el mismo producto a la vez, el RowVersion del WHERE
            // no matchea al del UPDATE → 0 filas afectadas. Traducimos a
            // ValidationException (mismo canal que las validaciones de
            // las brechas 1-3) para que el Controller renderice un
            // mensaje claro en lugar de un 500 genérico.
            var codigoLog = (producto.Codigo ?? "<sin código>")
                .Replace("\r", " ")
                .Replace("\n", " ");

            _logger.LogWarning(ex,
                "Producto {ProductoId} ({Codigo}) — conflicto de concurrencia al actualizar por {UsuarioId}",
                producto.Id, codigoLog, usuarioId);
            throw new ValidationException(
                $"El producto {producto.Codigo} fue modificado por otro operador mientras editabas. " +
                "Recargá la página y volvé a intentar.");
        }

        // Issue #146.7: log de los campos efectivamente cambiados.
        // Filtramos la lista para no spammear cuando no hay cambios
        // reales (operador reenvió el form sin tocar nada). El "cambios"
        // también incluye el precio si cambió — Slice 3 ya loggea el
        // evento de histórico; acá solo evita duplicar el item en el
        // log de auditoría de la edición completa.
        if (cambios.Count > 0)
        {
            var cambiosLog = SanitizeForLog(string.Join(", ", cambios));
            _logger.LogInformation(
                "Producto {ProductoId} ({Codigo}) actualizado por {UsuarioId} — cambios: {Cambios}",
                entity.Id, entity.Codigo, usuarioId, cambiosLog);
        }

        return _mapper.Map<ProductoDto>(entity);
    }

    public async Task<bool> DeleteAsync(ulong id, ulong? usuarioId = null, CancellationToken ct = default)
    {
        var producto = await _context.Productos.FindAsync(new object[] { id }, ct);
        if (producto == null)
            return false;

        // Issue #114 (replicado): soft-delete completo — marca DeletedAt Y
        // baja Activo. Mantiene la invariante "Activo=false implica
        // DeletedAt != null" que las vistas y la consulta de activos esperan.
        // Antes solo se seteaba DeletedAt, dejando Activo=true: un zombie.
        producto.DeletedAt = DateTime.UtcNow;
        producto.Activo = false;
        producto.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Mismo patrón que UpdateAsync: el RowVersion protege contra
            // last-write-wins silencioso en la baja. El catálogo de
            // productos no debería perder una fila por esto, pero la
            // auditoría del intento queda registrada.
            _logger.LogWarning(ex,
                "Producto {Id} ({Codigo}) — conflicto de concurrencia al desactivar por {UsuarioId}",
                producto.Id, producto.Codigo, usuarioId);
            throw;
        }

        // Issue #146.7 + #146.6: Delete es AdminOnly (PR #145 Slice 2 lo
        // introdujo; issue #146.6 lo consolida). Loggeamos a nivel Warning
        // — un soft-delete no es un evento crítico, pero el operador
        // debería poder reconstruir qué producto se bajó y cuándo.
        _logger.LogWarning(
            "Producto {Id} ({Codigo}, nombre={Nombre}) desactivado por {UsuarioId}",
            producto.Id, producto.Codigo, producto.Nombre, usuarioId);

        return true;
    }

    public async Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        // Patrón tomado de PedidoService.RestoreAsync (línea 296). Usamos
        // IgnoreQueryFilters() porque el QueryFilter global oculta los
        // registros soft-deleted — sin esto no encontraríamos el producto
        // desde la papelera.
        var producto = await _context.Productos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (producto == null)
            return false;

        // Producto ya activo: nada que restaurar. Devolvemos false para que el
        // Controller mapee TempData[Error] en lugar de un falso Success.
        // Coherente con el spec de task 2.1 (RestoreAsync_OnAlreadyActive_ReturnsFalse).
        if (producto.DeletedAt == null)
            return false;

        // Producto retiene la columna Activo (a diferencia de Cliente post-#115
        // donde se deriva de DeletedAt). Setear explícitamente Activo=true
        // preserva la invariante "Activo=false implica DeletedAt != null"
        // (definida por #114, replicada en Productos por #121). Sin este set
        // quedaría un zombie: DeletedAt=null + Activo=false.
        producto.DeletedAt = null;
        producto.Activo = true;
        producto.UpdatedAt = DateTime.UtcNow;
        producto.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        // Trazabilidad: RestoreAsync es AdminOnly y revierte un soft-delete,
        // operación que el auditor quiere ver en logs. No loggeamos el caso
        // "no encontrado" porque es flujo esperado (404 desde la papelera).
        _logger.LogInformation(
            "Producto {ProductoId} reactivado por {UpdatedBy}",
            producto.Id, updatedBy);

        return true;
    }

    // ========================================================================
    // Helpers privados (issue #146 - validaciones centralizadas en el Service)
    // ========================================================================

    /// <summary>
    /// Verifica que el <c>TipoProductoId</c> recibido exista en el catálogo.
    /// Issue #146.1: sin esto, un TipoProductoId inválido explota a nivel
    /// MySQL con un FK error opaco que el Controller envuelve en "No se pudo
    /// crear el producto". Patrón tomado de PedidoService (líneas donde se
    /// valida <c>cliente_id</c> y <c>empleado_id</c>).
    /// </summary>
    private async Task ValidarTipoProductoExisteAsync(ulong tipoProductoId, CancellationToken ct)
    {
        var existe = await _context.TiposProducto
            .AsNoTracking()
            .AnyAsync(t => t.Id == tipoProductoId, ct);

        if (!existe)
            throw new ValidationException($"Tipo de producto inválido (id={tipoProductoId}).");
    }

    /// <summary>
    /// Verifica que el <c>Codigo</c> no esté usado por otro producto.
    /// Issue #146.2: el índice único <c>uq_productos_codigo</c> cubre el
    /// caso serial, pero dos requests concurrentes revientan a nivel MySQL
    /// con 500 "Duplicate entry". El <c>AnyAsync</c> da un error legible
    /// con el camino del conflicto. Mismo patrón que GarrafaService
    /// (líneas 218 + 242) y ProveedorService (líneas 162 + 172).
    /// </summary>
    /// <param name="codigo">Código a verificar.</param>
    /// <param name="idAExcluir">Id del producto actual; pasar <c>null</c>
    /// en Create. En Update, pasar el Id del producto que se está
    /// editando para no chocar contra sí mismo.</param>
    private async Task ValidarCodigoNoDuplicadoAsync(string codigo, ulong? idAExcluir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return; // DataAnnotations ya lo rechaza en el Controller.

        // Filtro manual `DeletedAt == null` para que un producto soft-deleted
        // con el mismo Codigo NO aparezca como colisión: si se reactiva, el
        // espacio lógico del código está libre. El QueryFilter global hace
        // lo mismo para queries de lectura, pero acá lo explicitamos para
        // que el lector entienda la intención sin saltar al Configuration.
        var query = _context.Productos
            .AsNoTracking()
            .Where(p => p.Codigo == codigo && p.DeletedAt == null);

        if (idAExcluir.HasValue)
            query = query.Where(p => p.Id != idAExcluir.Value);

        var existe = await query.AnyAsync(ct);
        if (existe)
            throw new ValidationException($"Ya existe un producto con el código '{codigo}'.");
    }

    /// <summary>
    /// Sanitiza texto para logging en salidas planas, evitando log forging por CR/LF.
    /// </summary>
    private static string SanitizeForLog(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    /// <summary>
    /// Issue #146.7: detecta las propiedades que el Mapper va a pisar para
    /// poder listarlas en el log de auditoría. Compara los valores del DTO
    /// contra los de la entity ANTES del Map para no contaminarse con el
    /// resultado del mapeo (que ya pisó la entity).
    /// </summary>
    private static List<string> DetectarCambiosProducto(Producto entity, UpdateProductoDto dto)
    {
        var cambios = new List<string>();

        if (!string.Equals(entity.Codigo, dto.Codigo, StringComparison.Ordinal))
            cambios.Add($"Codigo: '{entity.Codigo}' → '{dto.Codigo}'");
        if (!string.Equals(entity.Nombre, dto.Nombre, StringComparison.Ordinal))
            cambios.Add($"Nombre: '{entity.Nombre}' → '{dto.Nombre}'");
        if (!string.Equals(entity.Descripcion ?? null, dto.Descripcion ?? null, StringComparison.Ordinal))
            cambios.Add("Descripcion");
        if (entity.TipoProductoId != dto.TipoProductoId)
            cambios.Add($"TipoProductoId: {entity.TipoProductoId} → {dto.TipoProductoId}");
        if (entity.CapacidadKg != dto.CapacidadKg)
            cambios.Add($"CapacidadKg: {entity.CapacidadKg?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"} → {dto.CapacidadKg?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}");
        if (!string.Equals(entity.UnidadVenta, dto.UnidadVenta, StringComparison.Ordinal))
            cambios.Add($"UnidadVenta: '{entity.UnidadVenta}' → '{dto.UnidadVenta}'");
        if (entity.PrecioActual != dto.PrecioActual)
            cambios.Add($"PrecioActual: {entity.PrecioActual} → {dto.PrecioActual}");
        if (entity.ManejaGarrafaIndividual != dto.ManejaGarrafaIndividual)
            cambios.Add($"ManejaGarrafaIndividual: {entity.ManejaGarrafaIndividual} → {dto.ManejaGarrafaIndividual}");

        return cambios;
    }

    /// <summary>
    /// Recolecta los IDs de CreatedBy/UpdatedBy de los productos y devuelve
    /// un diccionario Id → Username para resolver auditores en una sola query.
    /// Issue #147 item 4: replica <see cref="UsuarioService.LoadAuditUsersAsync"/>
    /// (líneas 570-587) — los usernames NO viven en Producto, son FKs a
    /// usuarios. Devuelve diccionario vacío si no hay IDs para evitar la
    /// query.
    /// </summary>
    private async Task<Dictionary<ulong, string>> LoadAuditUsersAsync(
        IEnumerable<Producto> productos, CancellationToken ct)
    {
        var auditUserIds = new HashSet<ulong>();
        foreach (var producto in productos)
        {
            if (producto.CreatedBy.HasValue) auditUserIds.Add(producto.CreatedBy.Value);
            if (producto.UpdatedBy.HasValue) auditUserIds.Add(producto.UpdatedBy.Value);
        }

        if (auditUserIds.Count == 0) return new Dictionary<ulong, string>();

        return await _context.Usuarios
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => auditUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    /// <summary>
    /// Copia el username del auditor (CreatedBy / UpdatedBy) en el DTO si
    /// el auditor existe. Sin excepción si el auditor fue soft-deleted —
    /// el Diccionario simplemente no tiene la entrada y los campos quedan
    /// en null, que es la representación correcta (auditor desconocido).
    /// </summary>
    private static void AplicarAudit(
        ProductoDto dto, Producto entity, Dictionary<ulong, string> auditUsers)
    {
        if (entity.CreatedBy.HasValue && auditUsers.TryGetValue(entity.CreatedBy.Value, out var creador))
            dto.CreatedByUserName = creador;

        if (entity.UpdatedBy.HasValue && auditUsers.TryGetValue(entity.UpdatedBy.Value, out var actualizador))
            dto.UpdatedByUserName = actualizador;
    }
}
