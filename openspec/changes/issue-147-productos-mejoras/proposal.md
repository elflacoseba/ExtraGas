# Proposal: issue-147-productos-mejoras

## Intent

Eight targeted enhancements to make the Productos module consistent with patterns already adopted by other modules (Clientes, Empleados, Usuarios): normalized codigo storage, visible audit fields in Details/Edit, memory cache for lookup data, delete-impact confirmation, generic audit trail, missing test coverage, a proper UnidadVenta catalog, and a closed-catalog ADR for TipoProducto.

---

## Scope

### In Scope

**Item 1 — Cache `tipos_producto`**
- `ProductoService` injects `IMemoryCache`; `GetTiposProductoAsync` wraps the query with `GetOrCreateAsync`, key `"tipos_producto"`, TTL 1 h.
- `IProductoService` interface unchanged.

**Item 2 — Delete-impact UI (CORRECTED)**
- `pedido_items`, `recepcion_items`, `movimientos_garrafa` have NO `deleted_at` column (exploration findings #43-45). Count query runs WITHOUT any soft-delete filter.
- `ProductoService.CountDependenciesAsync(ulong productoId)` — returns `int` with counts from all 3 tables.
- `ProductosController.Delete GET` — shows 3 counters; if any > 0, renders SweetAlert2 type-to-confirm (user types exact `codigo` to enable confirm button).
- No `Delete.cshtml` file — inline modal in `Index.cshtml`.

**Item 3 — `audit_log` table + `IAuditLogger`**
- New entity `AuditLogEntry`, config `AuditLogEntryConfiguration`, migration.
- New interface `IAuditLogger` + implementation `AuditLogger` (DI Scoped).
- `ProductoService.UpdateAsync` calls `IAuditLogger.LogAsync` per changed field (reuses existing `DetectarCambiosProducto` logic from `ProductoService.cs:472–494`).
- Index on `(entidad, registro_id, changed_at)` only — no cleanup strategy; document growth concern in ADR.

**Item 4 — Audit fields visible in Details/Edit (CORRECTED)**
- `Cliente/Details.cshtml` and `Cliente/Edit.cshtml` do NOT show audit fields (exploration findings #110-113). This pattern must be built from scratch.
- `ProductoDto` — add `CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName`.
- `MappingProfile.ConfigureProducto` — add `.ForMember` for audit usernames (mirror `UsuarioService.AplicarAudit` pattern).
- `ProductoService.GetByIdAsync` — load auditor usernames via `LoadAuditUsersAsync`.
- `Details.cshtml` — Audit card (`<dl>` block, AdminLTE card style).
- `Edit.cshtml` — read-only audit info row.

**Item 5 — Missing `ProductoServiceTests`**
- 7 new test cases covering: `GetByCodigoAsync` (not found, soft-deleted → null), `GetByTipoAsync` (empty list), `GetActivosAsync` (mix filter), `UpdateAsync` (KeyNotFoundException), `DeleteAsync` (not found), `CreateAsync` (null userId → no crash).
- 1 new test file `StringNormalizerTests.cs` for `TrimAndUpper`.

**Item 6 — Codigo normalization (CORRECTED)**
- `StringNormalizer.TrimAndUpper` does not exist in `Extensions/StringNormalizer.cs` (exploration findings #188-202). Must be added first.
- `ProductoService.CreateAsync` and `UpdateAsync` apply `TrimAndUpper` to `dto.Codigo` before persisting.
- `ProductoService.GetPagedAsync` (search/Index) normalizes the search input before the LIKE query.
- Test: `" gas-10 "` → persisted as `"GAS-10"`; `GetByCodigoAsync("GAS-10")` matches it.

**Item 7 — `UnidadVenta` lookup table (Option B)**
- Entity `UnidadVenta`, config `UnidadVentaConfiguration`, seed `db/seed/unidades_venta.sql` (UNIDAD, GARRAFA, BOLSA, KG).
- FK `Producto.UnidadVentaId` → `unidades_venta.id`; data migration uses `INSERT IGNORE` before `ALTER TABLE`.
- `UnidadVentaDto`, `ProductoDto.UnidadVentaId`, `CreateProductoDto.UnidadVentaId`, `UpdateProductoDto.UnidadVentaId`.
- `MappingProfile.ConfigureUnidadVenta`.
- `ProductoService` — replace `UnidadVenta` string with `UnidadVentaId` ulong.
- `ProductosController.LoadViewBagsAsync` — load `UnidadesVenta` list.
- `Create.cshtml` / `Edit.cshtml` — replace `<input>` with `<select>` populated from `GetUnidadesVentaAsync`.

**Item 8 — ADR: `tipos_producto` closed catalog**
- New ADR entry in `db/docs/DECISIONES.md`: "Catálogos cerrados: `tipos_producto` y `unidades_venta`" — documents that adding/removing product types requires a SQL migration; UI CRUD intentionally not built.

---

## Out of Scope

- Any CRUD UI for `tipos_producto` (Item 8 decision: document only, no implementation).
- `audit_log` cleanup job / retention policy (WARNING noted, no scope creep).
- Any other modules beyond Productos.
- Changes to `pedido_items`, `recepcion_items`, `movimientos_garrafa` schemas.

---

## Approach

### SDD cycle with 3 chained PRs stacked-to-main

**Slice 1 — Bajo impacto (~350 LOC): Items 6, 4, 5, 1**
- Pure ProductoService / DTO / Views / Tests changes. No DB schema.
- `TrimAndUpper` addition first (Item 6), then DTO audit fields + view changes (Item 4), then tests (Item 5), then cache (Item 1).
- Files: ~15 files touched, all localized.

**Slice 2 — Infraestructura (~330 LOC): Item 3**
- New `audit_log` table, `IAuditLogger` interface + implementation, integrated into `ProductoService.UpdateAsync`.
- Creates new infra that other modules can reuse later.
- First slice to touch DB schema — isolated to avoid conflicts.

**Slice 3 — Mixed (~380 LOC): Items 2, 7, 8**
- Delete-impact UI with SweetAlert2 type-to-confirm (Item 2).
- `unidades_venta` catalog: lookup table + FK + `<select>` in views (Item 7).
- ADR documenting `tipos_producto` as closed catalog (Item 8).
- Heaviest slice — coordinate DB migration ordering (seed before ALTER).

### Slice size estimates

| Slice | Items | Est. LOC | Risk | Rationale |
|-------|-------|----------|------|-----------|
| 1 | 6, 4, 5, 1 | ~350 | Low | No schema changes; uses existing patterns |
| 2 | 3 | ~330 | Med | New table + interface + hook into existing service |
| 3 | 2, 7, 8 | ~380 | Med-High | Migration ordering risk (seed before FK ALTER); most files |

### Alternatives considered

| Alternative | Rejected理由 |
|-------------|-------------|
| Single PR with `size:exception` | 3 isolated PRs are reviewable in ≤400-line chunks; failures are contained |
| Option A (hardcoded `UnidadVenta` values) | Rejected; Option B (lookup table) follows existing `tipos_producto` pattern |
| TipoProducto CRUD UI | Rejected per user decision: document as intentional closure only |
| Single slice covering all items | Would exceed 400-line review budget; loses isolation benefit |

---

## Risks

| Severity | Description | Mitigation |
|----------|-------------|------------|
| CRITICAL | Item 2: Tables `pedido_items`, `recepcion_items`, `movimientos_garrafa` have NO `deleted_at` — count must run WITHOUT filter. | Proposal specifies no `WHERE deleted_at IS NULL` in `CountDependenciesAsync` |
| CRITICAL | Item 4: `Cliente/Details.cshtml` and `Cliente/Edit.cshtml` do NOT show audit fields — pattern must be built from scratch. | Proposal scopes full pattern build: DTO + mapping + service + views |
| CRITICAL | Item 6: `StringNormalizer.TrimAndUpper` does not exist — must be added before Item 6 can be implemented. | Slice 1 adds `TrimAndUpper` first, then uses it in service methods |
| WARNING | Item 7: data migration ordering — `INSERT IGNORE` for seed values MUST land BEFORE `ALTER TABLE` to add FK. | Migration script uses two separate statements; seed-first ordering enforced |
| WARNING | Item 3: `audit_log` grows unbounded on every UPDATE — no cleanup strategy defined. | ADR documents the concern; index only (no cleanup in scope); follow-up issue suggested |
| INFO | Slice 2 creates `IAuditLogger` for ProductoService only — other modules not updated. | Design notes that pattern is reusable; other modules are out of scope |
| INFO | Item 8 decision is practically irreversible (no going back after ADR is merged). | ADR explicitly notes "si emerge la necesidad, crear `TiposProductoController` con `[Authorize(Policy = "AdminOnly")]`" |

---

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC1 | `IMemoryCache` integrated in `GetTiposProductoAsync` with key `"tipos_producto"`, TTL 1 h. |
| AC2 | `DeleteAsync` counts from `pedido_items`, `recepcion_items`, `movimientos_garrafa` with NO `deleted_at` filter; if any count > 0 → SweetAlert2 type-to-confirm requiring exact `codigo` match. |
| AC3 | `audit_log` table + `IAuditLogger` interface + `ProductoService.UpdateAsync` emits per-field change events. |
| AC4 | `ProductoDto` exposes `CreatedAt`/`UpdatedAt`/`CreatedByUserName`/`UpdatedByUserName`; rendered in Details.cshtml (AdminLTE card) and Edit.cshtml (read-only row). |
| AC5 | `ProductoServiceTests` has 7 new cases covering missing branches; `StringNormalizerTests` covers `TrimAndUpper`. |
| AC6 | `TrimAndUpper` added to `StringNormalizer`; applied in `CreateAsync`, `UpdateAsync`, `GetPagedAsync` search; tests prove `" gas-10 "` → `"GAS-10"`. |
| AC7 | `unidades_venta` lookup table seeded with UNIDAD/GARRAFA/BOLSA/KG; FK from `productos.unidad_venta_id`; `<select>` in Create/Edit populated from `GetUnidadesVentaAsync`. |
| AC8 | ADR in `db/docs/DECISIONES.md`: "Catálogos cerrados: `tipos_producto` y `unidades_venta`" documents intentional closure. |

---

## Suggested Follow-up Issues

1. **TipoProducto CRUD UI** — implement `TiposProductoController` with `AdminOnly` policy when/if business need emerges.
2. **`audit_log` cleanup cron** — add retention policy or archival job for `audit_log` table growth.
3. **Audit trail UI** — make change history visible from `Producto/Details.cshtml` using `audit_log` data.

---

## User Decisions (Locked — Do Not Re-evaluate)

> "Approach: SDD cycle with chained PRs (3 slices stacked-to-main)"  
> "Item #7 (UnidadVenta): Option B — lookup table `unidades_venta` with migration + FK"  
> "Item #8 (TipoProducto): Document in ADR as intentionally closed — NO UI implementation"  
> "Pace: Automatic"  
> "PR strategy: Auto-chain when >400 lines"  
> "Review budget: 400 lines per slice"
