# Delta Spec: productos

## Purpose
Eight enhancements to Productos: `Codigo` normalization, visible audit fields, lookup caching, delete-impact confirmation, generic `audit_log`, missing tests, `unidades_venta` lookup, and a closed-catalog ADR for `tipos_producto`. All `## ADDED` — nothing modified.

## ADDED Requirements

### Requirement: Cache de catálogo de tipos_producto
`GetTiposProductoAsync` MUST serve `tipos_producto` from `IMemoryCache` (key `"tipos_producto"`, TTL 1 h).

#### Scenario: 2nd call within 1 h is cached
- WHEN invoked twice within 1 h, only the first call hits EF Core; the second is served from cache.

#### Scenario: forward-looking invalidation hook
- WHEN a future TipoProducto CRUD writes, the implementation MUST evict `"tipos_producto"` (documented; not shipped now).

#### Scenario: seed-only catalog tolerates 1 h staleness
- WHEN the cached list ages up to 1 h, staleness is acceptable (seed-only, no UI writer).

### Requirement: Conteo de dependencias antes de Delete
The system MUST count historical dependencies before soft-delete and require type-to-confirm when any count > 0.

#### Scenario: 0 dependencies → direct confirm
- WHEN `CountDependenciesAsync` returns `(0,0,0)`, the Delete GET MUST allow direct confirmation (no type-to-confirm).

#### Scenario: any dependency > 0 → type-to-confirm
- WHEN any of the three counters is > 0, the Delete GET MUST render all three counters AND require typing the exact `codigo` to enable SweetAlert2 confirm.

#### Scenario: count MUST NOT filter by `deleted_at`
- WHEN `CountDependenciesAsync` runs, the SQL MUST NOT include `WHERE deleted_at IS NULL` (those tables have no `deleted_at`).

#### Scenario: mismatch blocks Delete
- WHEN the typed confirmation ≠ `codigo`, the Delete POST is rejected client-side and never reaches the controller.

### Requirement: Auditoría de cambios por campo (audit_log)
`ProductoService.UpdateAsync` MUST persist one `audit_log` row per changed auditable field.

#### Scenario: precio change emits one row
- WHEN `PrecioActual` goes `1000 → 1500` with `currentUserId=U`, exactly one `audit_log` row exists: `entidad='Producto'`, `registro_id=P`, `campo='PrecioActual'`, `valor_anterior='1000'`, `valor_nuevo='1500'`, `changed_by=U`.

#### Scenario: no-op update emits zero rows
- WHEN the DTO equals the entity, zero `audit_log` rows are inserted.

#### Scenario: composite index exists
- WHEN the migration creates `audit_log`, an index on `(entidad, registro_id, changed_at)` MUST exist.

### Requirement: Auditoría visible en Producto Details/Edit
`ProductoDto` MUST expose `CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName`; Details/Edit MUST render them.

#### Scenario: ProductoDto populates 4 audit fields
- WHEN `GetByIdAsync` returns a product, the `ProductoDto` MUST populate the four audit fields without breaking the existing `MappingProfile` contract.

#### Scenario: Details.cshtml renders audit card
- WHEN `Details.cshtml` is rendered, it MUST include an AdminLTE card with `<dl>` rows for the four audit fields.

#### Scenario: Edit.cshtml renders audit fields read-only
- WHEN `Edit.cshtml` is rendered, the four audit fields MUST be read-only (not bound to form submit).

#### Scenario: AutoMapper MUST NOT overwrite usernames
- WHEN AutoMapper maps `Producto → ProductoDto`, the four audit fields MUST be sourced from explicit service-level resolution, not the entity directly.

### Requirement: Cobertura de tests de ProductoService (≥ 80% branches)
Unit tests MUST cover seven previously untested branches in `ProductoService` plus `StringNormalizer.TrimAndUpper`.

#### Scenario: GetByCodigoAsync missing → null
- WHEN no product matches, the call returns `null`.

#### Scenario: GetByCodigoAsync soft-deleted → null
- WHEN a soft-deleted product has `Codigo='GAS-10'`, the call returns `null` (QueryFilter).

#### Scenario: GetByTipoAsync empty list
- WHEN no products match the type, the call returns an empty list.

#### Scenario: GetActivosAsync filters inactives
- WHEN the table mixes active + inactive (non-deleted), only `Activo=true AND DeletedAt IS NULL` are returned.

#### Scenario: UpdateAsync unknown Id → KeyNotFoundException
- WHEN the Id has no match, the call throws `KeyNotFoundException`.

#### Scenario: DeleteAsync unknown Id → false
- WHEN the Id has no match, the call returns `false` (no exception).

#### Scenario: CreateAsync null userId → no crash
- WHEN `currentUserId=null`, the product is persisted with `CreatedBy=NULL`.

### Requirement: Normalización de Codigo (trim + upper)
The system MUST trim + uppercase `Producto.Codigo` via a new `StringNormalizer.TrimAndUpper` before persisting and before searching.

#### Scenario: Create persists normalized
- WHEN `Codigo=" gas-10 "`, the persisted value is `"GAS-10"`.

#### Scenario: GetByCodigoAsync matches normalized input
- WHEN stored `"GAS-10"` and query `"gas-10"`, the lookup returns the product.

#### Scenario: Index search normalizes input
- WHEN search input is `" gas "`, the LIKE query runs against `"GAS"`.

#### Scenario: TrimAndUpper(null) → empty
- WHEN `TrimAndUpper(null)` runs, it returns `string.Empty`.

### Requirement: Catálogo cerrado de unidades_venta
`Producto.UnidadVenta` (free-text) MUST become a FK to a new `unidades_venta` lookup seeded with UNIDAD, GARRAFA, BOLSA, KG; Create/Edit MUST render a `<select>`.

#### Scenario: seed contains 4 values
- WHEN the seed migration runs, `unidades_venta` contains UNIDAD, GARRAFA, BOLSA, KG.

#### Scenario: FK to unidades_venta.id
- WHEN `ProductoConfiguration` is applied, `UnidadVentaId` is a FK to `unidades_venta.id`.

#### Scenario: GetUnidadesVentaAsync ordered list
- WHEN called, it returns rows ordered by `Nombre`.

#### Scenario: Create/Edit uses <select>
- WHEN the unidad_venta field renders, it MUST be a `<select>` from `GetUnidadesVentaAsync`, not a free-text `<input>`.

#### Scenario: migration order: seed BEFORE ALTER
- WHEN the migration runs, `INSERT IGNORE` runs first; the FK `ALTER TABLE` runs after.

### Requirement: Catálogo de tipos_producto intencionalmente cerrado
`tipos_producto` MUST be a closed catalog: no UI CRUD; new types require a SQL migration. Documented in an ADR.

#### Scenario: no UI CRUD exists
- WHEN the app is inspected, there MUST be no `TiposProductoController`, `/TiposProducto` route, or ABM views.

#### Scenario: adding a type needs SQL migration
- WHEN a new type is needed, the dev MUST add a SQL migration under `db/migrations/`.

#### Scenario: ADR documents closure
- WHEN archived, `db/docs/DECISIONES.md` MUST contain an ADR "Catálogos cerrados: `tipos_producto` y `unidades_venta`" with rationale and the AdminOnly escape hatch.
