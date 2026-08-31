# Exploration: issue-147-productos-mejoras

> **Fecha:** 2026-08-31  
> **Proyecto:** ExtraGas (ASP.NET Core MVC + EF Core + MySQL 8.4)  
> **Cambio:** GitHub issue #147 — "Mejoras: cache, auditoría, tests faltantes, normalización y catálogos cerrados"  
> **Tipo:** Enhancement (8 items independientes)

---

## Resumen Ejecutivo

Ocho mejoras incrementales al módulo Productos. La mayoría de los patrones ya existen en el codebase — solo hay que adoptarlos. Hallazgos críticos: (1) las tablas `pedido_items`, `recepcion_items`, `movimientos_garrafa` NO tienen `deleted_at` (el issue asume lo contrario), (2) `Cliente/Details` y `Cliente/Edit` NO muestran campos de auditoría (el issue asume que sí), (3) `StringNormalizer.TrimAndUpper` no existe — hay que agregarla.

---

## Hallazgos por Ítem

### Ítem 1 — Cache de `tipos_producto` en memoria

**Estado actual:**
- `Program.cs:16` registra `AddMemoryCache()` pero `ProductoService` no lo inyecta ni lo usa.
- `GetTiposProductoAsync` (`ProductoService.cs:87–95`) hace `AsNoTracking().OrderBy().ToListAsync()` en cada request — sin cache.

**Patrón a seguir:**  
No hay ningún ejemplo de `IMemoryCache` en los servicios existentes del codebase. Es zona verde — seguir la receta del skill `dotnet-backend-patterns` con `GetOrCreateAsync`.

**Archivos afectados:**
- `Services/Implementations/ProductoService.cs` — inyectar `IMemoryCache`, envolver `GetTiposProductoAsync`
- `Services/Interfaces/IProductoService.cs` — sin cambios de firma

**Unknowns:** Ninguno. El issue provee el código exacto a usar.

---

### Ítem 2 — UI para impacto de Delete

**⚠️ PREMISA ERRÓNEA EN EL ISSUE — CRITICAL**

El issue asume que `pedido_items`, `recepcion_items` y `movimientos_garrafa` tienen columna `deleted_at` y propone queries con `WHERE deleted_at IS NULL`. La investigación muestra:

| Tabla | ¿Tiene `deleted_at`? | Entidad (`Data/Entities/`) | Config (`Configurations/`) |
|-------|----------------------|---------------------------|----------------------------|
| `pedido_items` | **NO** — solo `CreatedAt`, `UpdatedAt` | `PedidoItem.cs:15–16` | `PedidoItemConfiguration.cs` sin DeletedAt |
| `recepcion_items` | **NO** — solo `CreatedAt`, `UpdatedAt` | `RecepcionItem.cs:11–12` | `RecepcionItemConfiguration.cs` sin DeletedAt |
| `movimientos_garrafa` | **NO** — solo `CreatedAt`, `CreatedBy` | `MovimientoGarrafa.cs:16–17` | `MovimientoGarrafaConfiguration.cs` sin DeletedAt |

**Implicancia:** el conteo de impacto debe hacer `COUNT(*)` sobre estas tres tablas SIN filtro `deleted_at`. Si el issue autor sabía esto y lo expresó con "verificar", la palabra "verificar" era literal.

**Lo que se necesita implementar:**
- Nuevo método `ProductoService.CountDependenciesAsync(ulong productoId)` que cuente filas en las 3 tablas (sin filtro de soft-delete porque no existe).
- `ProductosController.Delete` GET (hoy es POST-only sin vista de confirmación) — mostrar los 3 contadores y pedir tipear el código si alguno > 0.
- SweetAlert2 con "type-to-confirm" pattern (GitHub-style): input que exige escribir el código exacto para habilitar el botón de confirmar.

**Patrones existentes:**
- SweetAlert2 ya está en `package.json` (`sweetalert2 ^11.26.25`).
- No hay vista `Delete.cshtml` en `Views/Productos/` — el DELETE actual es POST directo sin confirmación de UI. El módulo `Clientes` tampoco tiene `Delete.cshtml` (paper flow similar).

**Archivos afectados:**
- `Services/Implementations/ProductoService.cs` — agregar `CountDependenciesAsync`
- `Services/Interfaces/IProductoService.cs` — agregar firma
- `Controllers/ProductosController.cs` — agregar `[HttpGet] Delete(id)` para la vista de confirmación
- `Views/Productos/` — nueva vista de confirmación (o inline en `Index.cshtml` via modal SweetAlert2)

---

### Ítem 3 — Tabla `audit_log` genérica + emisión de eventos

**Estado actual:**
- No existe tabla `audit_log` genérica.
- `ProductoService` ya tiene `DetectarCambiosProducto` (`ProductoService.cs:472–494`) que lista los campos que cambiaron — pero solo lo emite al log de ILogger, no a una tabla BD.
- Existe `auditoria_logins` (`db/migrations/20260828_000002_create_auditoria_logins.sql`) como ejemplo de tabla de auditoría.
- `ProductoService.UpdateAsync:303–315` ya loggea cambios en `ILogger`.

**Patrón a seguir para la tabla:** usar `auditoria_logins` como template:
- Entidad `AuditLogEntry` en `Data/Entities/`
- Config `AuditLogEntryConfiguration` en `Data/Configurations/`
- Interface `IAuditLogger` en `Services/Interfaces/`
- Implementación `AuditLogger` en `Services/Implementations/`
- Hook en `ProductoService.UpdateAsync` comparando entity vs DTO (ya tiene `DetectarCambiosProducto`).

**Schema propuesto (siguiente al issue):**
```sql
CREATE TABLE audit_log (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  entidad VARCHAR(50) NOT NULL,
  registro_id BIGINT UNSIGNED NOT NULL,
  campo VARCHAR(100) NOT NULL,
  valor_anterior TEXT NULL,
  valor_nuevo TEXT NULL,
  changed_by BIGINT UNSIGNED NULL,
  changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_audit_entidad (entidad, registro_id, changed_at)
);
```

**Nota:** el hook de `ProductoService.UpdateAsync` compara la entity ANTES y DESPUÉS del `AutoMapper.Map` (patrón ya establecido con `DetectarCambiosProducto`). La comparación de valores es `StringComparison.Ordinal` para strings y `!=` para numéricos.

**Archivos afectados:**
- `Data/Entities/AuditLogEntry.cs` (nuevo)
- `Data/Configurations/AuditLogEntryConfiguration.cs` (nuevo)
- `Services/Interfaces/IAuditLogger.cs` (nuevo)
- `Services/Implementations/AuditLogger.cs` (nuevo)
- `Services/Implementations/ProductoService.cs` — integrar `IAuditLogger` en `UpdateAsync`
- `db/migrations/` — nueva migración `audit_log`

---

### Ítem 4 — Auditoría visible en Details/Edit

**⚠️ PREMISA ERRÓNEA EN EL ISSUE — CRITICAL**

El issue dice: "Cliente/Details muestra CreatedAt/UpdatedAt/UpdatedBy. Producto no — inconsistente."  
**Investigación:** `Cliente/Details.cshtml` y `Cliente/Edit.cshtml` **NO muestran** campos de auditoría. Los archivos solo contienen datos personales, contacto y dirección. No hay bloque de auditoría visible en ninguno de los dos.

Esto significa que item 4 debe construir el patrón desde cero, no replicar algo existente.

**Lo que existe:**
- `Producto` entity tiene: `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` (`Producto.cs:15–18`)
- `ProductoDto` NO expone estos campos — hay que agregarlos
- `MappingProfile.ConfigureProducto` (`MappingProfile.cs:125–142`) hace mapping de Producto→ProductoDto pero no incluye auditoría

**Lo que se necesita:**
1. `ProductoDto` — agregar `CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName` (tipados como en `UsuarioService:567–620` para resolver usernames)
2. `MappingProfile.ConfigureProducto` — agregar `.ForMember` para los usernames de auditoría (mismo patrón que `UsuarioService.AplicarAudit`)
3. `Views/Productos/Details.cshtml` — agregar bloque de auditoría (tarjeta `<dl>` similar a los otros bloques de datos)
4. `Views/Productos/Edit.cshtml` — agregar campos de auditoría como read-only

**Patrón de username desde `UsuarioService:609–621`:**
```csharp
// Cargar usernames de auditores
var auditUsers = await LoadAuditUsersAsync(usuarios, ct);
// En el mapping
AplicarAudit(dto, usuario, auditUsers);
```

**Archivos afectados:**
- `DTOs/ProductoDto.cs` — agregar 4 propiedades de auditoría
- `Mappings/MappingProfile.cs` — agregar mapping de CreatedByUserName/UpdatedByUserName
- `Services/Implementations/ProductoService.cs` — cargar usernames de auditores en GetByIdAsync
- `Views/Productos/Details.cshtml` — sección de auditoría
- `Views/Productos/Edit.cshtml` — campos read-only de auditoría

---

### Ítem 5 — Tests de integración faltantes en `ProductoServiceTests`

**Estado actual (`ProductoServiceTests.cs`, 364 líneas):**

Casos cubiertos:
| Test | Método bajo prueba | Qué cubre |
|------|-------------------|-----------|
| `CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga` | CreateAsync | Activo=true |
| `UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga` | UpdateAsync | Activo preservado |
| `DeleteAsync_SeteaDeletedAtYActivoFalse_SoftDeleteCompleto` | DeleteAsync | Soft-delete completo |
| `RestoreAsync_ReactivatesSoftDeletedProducto` | RestoreAsync | Reactivación |
| `RestoreAsync_OnAlreadyActive_ReturnsFalse` | RestoreAsync | Ya activo |
| `RestoreAsync_OnNonExistent_ReturnsFalse` | RestoreAsync | No existe |
| `UpdateAsync_PriceChange_CreatesHistoryRow` | UpdateAsync | Histórico de precio |
| `UpdateAsync_PriceUnchanged_NoHistoryRow` | UpdateAsync | Sin cambio de precio |
| `UpdateAsync_PriorZero_NoHistoryRow` | UpdateAsync | Precio anterior cero |
| `UpdateAsync_PriceChange_StoresMotivoCambioPrecio` | UpdateAsync | Motivo de cambio |
| `UpdateAsync_PriceChange_LogsInformation` | UpdateAsync | Logging |
| `UpdateProductoDto_MotivoCambioPrecio_RechazaMasDe255Chars` | DTO validation | Validación DTO |

**Casos faltantes (7 branches):**

| Caso faltante | Método | Razón de importancia |
|---|---|---|
| `GetByCodigoAsync` con producto inexistente → null | GetByCodigoAsync | Branch no testeado |
| `GetByCodigoAsync` con soft-deleted → null | GetByCodigoAsync | QueryFilter filtra; контракт dice null |
| `GetByTipoAsync` con tipo inexistente → lista vacía | GetByTipoAsync | Branch no testeado |
| `GetActivosAsync` con mix activos/inactivos → solo activos | GetActivosAsync | Contrato del método |
| `UpdateAsync` con Id inexistente → KeyNotFoundException | UpdateAsync | Branch no testeado |
| `DeleteAsync` con Id inexistente → false | DeleteAsync | Branch no testeado |
| `CreateAsync` sin usuario (usuarioId=null) → funciona | CreateAsync | Caso de tests automáticos |

**Test naming convention del repo:** `Metodo_ResultadoEsperado_Condicion` (ej. `CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga`).

**Test pattern a seguir:** `NewService(nameof(...))` helper con `DbContext InMemory`, seed de `TipoProducto` si aplica, `NullLogger<ProductoService>.Instance`.

**Archivos afectados:**
- `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` — agregar 7 nuevos tests

---

### Ítem 6 — Normalización de Codigo al guardar

**⚠️ MÉTODO INEXISTENTE — CRITICAL**

El issue propone usar `StringNormalizer.TrimAndUpper(dto.Codigo)` pero `Extensions/StringNormalizer.cs` **NO tiene este método**. Solo existe:
- `NormalizarDni(string?)` — trim + remueve `.`, `-`, espacios → solo dígitos
- `NormalizarTelefono(string?)` — trim + remueve separadores → solo `+` y dígitos

Para normalizar códigos de producto se necesita un método nuevo:

```csharp
public static string TrimAndUpper(string? input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;
    return input.Trim().ToUpperInvariant();
}
```

**Lo que se necesita:**
1. Agregar `TrimAndUpper` a `StringNormalizer.cs`
2. En `ProductoService.CreateAsync` y `UpdateAsync`: `entity.Codigo = StringNormalizer.TrimAndUpper(dto.Codigo)`
3. En `Index` (búsqueda): normalizar el input de búsqueda también con `TrimAndUpper` para que matchee consistentemente
4. Tests: `CreateAsync` con `" gas-10 "` → persiste `"GAS-10"`; `GetByCodigoAsync("GAS-10")` matchea

**Note:** La búsqueda en `Index` (`ProductoService.GetPagedAsync:127–139`) usa `EF.Functions.Like` con el `busqueda` directamente. Debería hacer `TrimAndUpper(trimmed)` antes del LIKE para que buscar "gas" matchee "GAS-10".

**Archivos afectados:**
- `Extensions/StringNormalizer.cs` — agregar `TrimAndUpper`
- `Services/Implementations/ProductoService.cs` — usar en CreateAsync/UpdateAsync
- `Services/Implementations/ProductoService.cs` — normalizar búsqueda en GetPagedAsync
- `tests/ExtraGasMVC.Tests/StringNormalizerTests.cs` — tests para `TrimAndUpper`
- `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` — tests de normalización en Create/Update

---

### Ítem 7 — Catálogo para `UnidadVenta`

**Decisión del maintainer:** Option B — lookup table `unidades_venta` con migración + FK.

**Patrón exacto a seguir:** `tipos_producto` (look-up table con entity + config + seed).

**Entidad actual:**
- `Producto.unidad_venta` → `VARCHAR(20)` libre en `Producto.cs:11`
- `ProductoConfiguration.cs:36–40` → mapeo simple sin FK

**Estructura a crear:**
1. **Entity** `UnidadVenta` en `Data/Entities/UnidadVenta.cs` (mismo shape que `TipoProducto`)
2. **Configuration** `UnidadVentaConfiguration` en `Data/Configurations/`
3. **Seed** en `db/migrations/` + `db/seed/unidades_venta.sql` (patrón de `db/seed/provincias_argentina.sql`)
4. **FK en Producto**: `Producto.UnidadVentaId` (ULONG, FK a `unidades_venta.id`) + mantener `codigo` como columna libre para retrocompatibilidad o migrar datos existentes
5. **DTOs**: `UnidadVentaDto`, agregar `UnidadVentaId` a `CreateProductoDto`/`UpdateProductoDto`
6. **Views**: `Create.cshtml` y `Edit.cshtml` — cambiar `<input>` por `<select>` con opciones del catálogo

**Valores seed:**
```sql
INSERT IGNORE INTO unidades_venta (codigo, nombre) VALUES
('UNIDAD', 'Unidad'),
('GARRAFA', 'Garrafa'),
('BOLSA', 'Bolsa'),
('KG', 'Kilogramo');
```

**Migración:** archivo nuevo en `db/migrations/` con `CREATE TABLE IF NOT EXISTS unidades_venta (...)`.

**Archivos afectados:**
- `Data/Entities/UnidadVenta.cs` (nuevo)
- `Data/Configurations/UnidadVentaConfiguration.cs` (nuevo)
- `DTOs/UnidadVentaDto.cs` (nuevo)
- `DTOs/ProductoDto.cs` — cambiar `UnidadVenta` string por `UnidadVentaId` ulong
- `DTOs/CreateProductoDto.cs` — cambiar `UnidadVenta` por `UnidadVentaId`
- `DTOs/UpdateProductoDto.cs` — igual
- `Mappings/MappingProfile.cs` — agregar `ConfigureUnidadVenta`
- `Data/Configurations/ProductoConfiguration.cs` — agregar FK a `UnidadVenta`
- `Services/Implementations/ProductoService.cs` — cambiar `UnidadVenta` a `UnidadVentaId`
- `Controllers/ProductosController.cs` — `LoadViewBagsAsync` cargar `UnidadesVenta`
- `Views/Productos/Create.cshtml` y `Edit.cshtml` — `<select>` en vez de `<input>`
- `db/migrations/` — migración nueva

---

### Ítem 8 — Decisión sobre `TipoProducto` — catalog cerrado

**Decisión del maintainer:** Documentar en ADR como intencionalmente cerrado. NO implementar UI CRUD.

**ADR a crear** (siguiente número libre en `db/docs/DECISIONES.md`):  
Nuevo ADR titled: **"Catálogos cerrados: `tipos_producto` y `unidades_venta`"**

Contenido mínimo:
- Qué: `tipos_producto` es catálogo operacional cerrado — agregar/eliminar tipos requiere migración SQL.
- Por qué: tipos de producto son decisiones de negocio que no deben cambiar en producción sin revisión (equivalente a los "estados" de un documento — no se crean desde la UI).
- Alternativas consideradas: Option A (CRUD completo) — rechazada porque un operador podría crear tipos inconsistentes sin control.
- Futuro: si emerge la necesidad, crear `TiposProductoController` con `[Authorize(Policy = "AdminOnly")]`.

---

## Patrones Confirmados del Codebase

### 1. `IMemoryCache` registration
`Program.cs:16` — `AddMemoryCache()` ya registrado. Solo falta inyectar.

### 2. `ILogger` injection
`ProductoService.cs:20` — `ILogger<ProductoService>` ya inyectado. Patrón confirmado.

### 3. `IAuditLogger` pattern from `auditoria_logins`
`IAuditoriaLoginService` (`Services/Interfaces/IAuditoriaLoginService.cs`) + `AuditoriaLoginService` — existe como patrón de auditoría desacoplada del flujo principal (try/catch en `AccountController:67`).

### 4. Lookup table pattern (`tipos_producto`)
- Entity: `Data/Entities/TipoProducto.cs` — solo Id, Codigo, Nombre, Descripcion, CreatedAt, UpdatedAt
- Config: `Data/Configurations/TipoProductoConfiguration.cs` — `HasIndex(t => t.Codigo).IsUnique()`
- Seed: `db/migrations/20260102_000009_seed_data.sql:49` — `INSERT IGNORE INTO tipos_producto ...`
- Sin `deleted_at` en lookup tables — no hay soft-delete en catálogos

### 5. `DetectarCambiosProducto` pattern
`ProductoService.cs:472–494` — ya detecta cambios campo por campo antes del `Map`. Reutilizable para `audit_log`.

### 6. Username audit resolution
`UsuarioService.cs:554–621` — `LoadAuditUsersAsync` + `AplicarAudit` para resolver CreatedBy/UpdatedBy a usernames legibles. Patrón a replicar en `ProductoService.GetByIdAsync`.

### 7. Soft-delete en Producto
- Entity: `Producto.DeletedAt` + `Producto.Activo` (double flag)
- QueryFilter global: `builder.HasQueryFilter(p => p.DeletedAt == null)` en `ProductoConfiguration.cs:120`
- `RestoreAsync` usa `IgnoreQueryFilters()` para encontrar eliminados

### 8. StringNormalizer existente
`Extensions/StringNormalizer.cs` — existe `NormalizarDni` y `NormalizarTelefono`. Falta `TrimAndUpper`.

### 9. BaseController
`Controllers/BaseController.cs:8–12` — `GetCurrentUserId()` devuelve `ulong?` desde claims.

---

## Arquitectura de Cambios por Slice (sugerida)

El issue sugiere el orden: 6 → 4 → 1 → 5 → 2 → 3 → 7 → 8. Para SDD encadenado con PRs de ≤400 líneas:

| Slice | Items | Líneas estimadas | Rationale |
|-------|-------|-----------------|-----------|
| Slice 1 | 6 (Codigo norm) + 8 (ADR TipoProducto) | ~80 | Mínimo riesgo, establece bases |
| Slice 2 | 4 (audit fields in DTO/views) + 5 (tests) | ~300 | Tests + DTO changes |
| Slice 3 | 1 (cache tipos_producto) | ~60 | Straightforward |
| Slice 4 | 2 (delete impact UI) | ~200 | View + controller + service method |
| Slice 5 | 3 (audit_log) | ~350 | Nueva tabla + interface + hook |
| Slice 6 | 7 (UnidadVenta catalog) | ~400 |Mayor impacto en modelos |

**Alternativa:** combinar slices 1+3+8 (orden de complejidad creciente).

---

## Riesgos

| Severity | Description |
|----------|-------------|
| CRITICAL | Ítem 2: premise wrong — `pedido_items`/`recepcion_items`/`movimientos_garrafa` NO tienen `deleted_at`. El SQL del issue no compilará. |
| CRITICAL | Ítem 4: premise wrong — `Cliente/Details.cshtml` y `Cliente/Edit.cshtml` NO muestran campos de auditoría. El patrón no existe para copiar. |
| CRITICAL | Ítem 6: `StringNormalizer.TrimAndUpper` no existe. Hay que crearla. |
| WARNING | Ítem 7 (UnidadVenta): cambiar `Producto.UnidadVenta` de `VARCHAR(20)` libre a FK requiere migración de datos existentes (los códigos actuales `GARRAFA`/`BOLSA` deben existir en la nueva tabla). |
| WARNING | Ítem 3 (audit_log): la tabla crece con cada UPDATE. Considerar partitioning o cleanup job para evitar que se descontrole. |
| INFO | Ítem 1: cache de 1h para `tipos_producto` es seguro porque es catálogo seed-only (nadie lo modifica desde la UI). |
| INFO | Ítem 8: decisión de no implementar es irreversible en la práctica (si después se quiere, hay que construir desde cero). |

---

## Decisiones Tomadas por el Maintainer (no re-evaluar)

1. **Approach**: SDD cycle con chained PRs (3 slices stacked-to-main)
2. **Item #7 (UnidadVenta)**: Option B — lookup table `unidades_venta` con migración + FK
3. **Item #8 (TipoProducto)**: Documentar en ADR como intencionalmente cerrado — NO UI implementation
4. **Pace**: Automatic
5. **Artifacts**: Both (Engram + OpenSpec)
6. **PR strategy**: Auto-chain cuando >400 líneas
7. **Review budget**: 400 líneas por slice

---

## Preparado para Proposal

**Sí — listo para `sdd-propose`.**

Los 3 hallazgos críticos (ítems 2, 4, 6) requieren corrección del scope antes de implementar, pero el path forward está claro:
- Ítem 2: contar SIN filtro `deleted_at` 
- Ítem 4: construir patrón de auditoría desde cero
- Ítem 6: agregar `TrimAndUpper` a `StringNormalizer`

El resto de los items son variaciones de patrones ya existentes en el codebase.
