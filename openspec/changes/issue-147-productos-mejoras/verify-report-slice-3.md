# Verify Report — issue-147-productos-mejoras (Slice 3)

> **Date**: 2026-08-31
> **Verifier**: `sdd-verify` sub-agent
> **Branch verified**: `feat/issue-147-slice-3-delete-unidadventa-adr`
> **Base**: `feat/issue-147-slice-2-audit-log` (slice-2 HEAD)
> **Tracker**: `feat/issue-147-productos-mejoras`
> **Mode**: Strict TDD (RED → GREEN → REFACTOR) — per slice 3 apply-progress
> **Artifacts in scope**: items 2, 7, 8 from the spec; tasks 3.1–3.14

## Verdict

**PASS** — All 3 slice-3 requirements validated; 427/427 tests green; build clean; ADR appended; migration is idempotent and verified via Testcontainers. The critical correction about the implicit `movimientos_garrafa` FK is correctly implemented and well documented.

## Summary

| Field | Value |
|-------|-------|
| Status | success |
| Verdict | pass |
| Requirements validated | 3/3 |
| Spec scenarios validated | 10/10 |
| Build clean | true |
| Tests passing | 427/427 |
| Critical correction validated | true |
| Migration status | validated_via_testcontainers |
| ADR number | #20 |
| Skill resolution | paths-injected |
| next_recommended | ship (awaiting PR merge) |

## Build & Tests Evidence

```
dotnet build src/ExtraGasMVC
  → Build succeeded. 0 Error(s). 2 Warning(s) — both are pre-existing
    NU1903 AutoMapper vulnerability (advisory GHSA-rvv3-g6hj-g44x), already
    noted in slice 2 as acceptable per AGENTS.md.

dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj
  → Passed! Failed: 0, Passed: 427, Skipped: 0, Total: 427, Duration: 16 s
```

Slice 3 added 18 new tests (8 service + 6 controller + 4 integration), bringing the cumulative total from 409 (slice 2) to 427. No regressions.

## Diff Stat (Slice 3 only — feat/issue-147-slice-2-audit-log..HEAD)

```
33 files changed, 2302 insertions(+), 50 deletions(-)
```

12 commits, all `(#147)` conventional commits, stacked on slice-2:

| #  | SHA       | Subject (Spanish, conventional)                                            |
|----|-----------|--------------------------------------------------------------------------|
| 14 | `028237a` | feat(db): tabla unidades_venta + FK + backfill desde unidad_venta legacy |
| 15 | `fb75cd8` | feat(data): Producto FK a unidades_venta (UnidadVentaId + UnidadVentaRef nav) |
| 16 | `f5fc246` | feat(data): UnidadVenta entity + EF config + DbContext DbSet             |
| 17 | `0e8b1f6` | feat(dto): ProductoDto+CreateDto+UpdateDto+MappingProfile para UnidadVentaId |
| 18 | `ddcf7d0` | feat(services): GetUnidadesVentaAsync + GetDeleteImpactAsync en ProductoService |
| 19 | `58c2133` | feat(controller): ProductosController.Delete GET/POST con GetDeleteImpactAsync + confirmCode |
| 20 | `d427153` | feat(views): Create/Edit usan select para UnidadVentaId + Delete con type-to-confirm |
| 21 | `8a00ddf` | test(productos): 8 service tests + 6 controller tests para slice 3      |
| 22 | `96fa77e` | test(integration): unidades_venta migration Testcontainers (4 tests)     |
| 23 | `9cce302` | docs(decisiones): ADR #20 — catalogos cerrados tipos_producto y unidades_venta |
| 24 | `91161d1` | test(productos): actualizar fixtures con UnidadVentaId + unidades_venta schema |
| (25)| `21d6974` | docs(slice-3): apply-progress merged con slice 1+2 |

---

## Requirement #7 — `Catálogo cerrado de unidades_venta`

### Spec scenarios validated

| Scenario | Evidence | Status |
|----------|----------|--------|
| **seed contains 4 values** (UNIDAD, GARRAFA, BOLSA, KG) | `UnidadesVentaMigrationIntegrationTests.Migracion_SembraryCreaTablaUnidadesVenta_ConCuatroValores` (lines 44-78) reads `codigo` from `unidades_venta` post-migration and asserts the set equals `{BOLSA, GARRAFA, KG, UNIDAD}` with strict ordering. Test runs against a real MySQL 8.0 container via Testcontainers. | ✅ |
| **FK to `unidades_venta.id`** | `Migracion_CreaColumnaUnidadVentaId_YFkHaciaUnidadesVenta` (lines 80-100) asserts (a) `information_schema.columns` has `unidad_venta_id` and (b) `information_schema.table_constraints` has `fk_productos_unidad_venta` of type `FOREIGN KEY`. | ✅ |
| **`GetUnidadesVentaAsync` ordered list** | `ProductoSlice3ServiceTests.GetUnidadesVentaAsync_ReturnsOrderedListByNombre` (lines 78-93) seeds 4 units out of order (UNIDAD/GARRAFA/BOLSA/KG by id), then asserts the returned list comes out alphabetically by Nombre: `Bolsa, Garrafa, Kilogramo, Unidad`. Implementation at `ProductoService.cs:178-191` uses `.OrderBy(u => u.Nombre)`. | ✅ |
| **Create/Edit uses `<select>`** | `Views/Productos/Create.cshtml:50-56` and `Views/Productos/Edit.cshtml:54-60` both render `<select asp-for="UnidadVentaId">` populated from `ViewBag.UnidadesVenta` set in `ProductosController.LoadViewBagsAsync:252`. No `<input asp-for="UnidadVenta">` is exposed to the operator — only a hidden `<input type="hidden" asp-for="UnidadVenta" value="" />` placeholder for the legacy column during the transition window. | ✅ |
| **migration order: seed BEFORE ALTER** | `Migracion_Backfill_ResuelveUnidadVentaStringAFkId` (lines 102-161) inserts a pre-existing product with `unidad_venta='GARRAFA'` (legacy VARCHAR), THEN applies the migration, THEN asserts that `unidad_venta_id` was populated with the id of the seeded `GARRAFA` row. The migration order in the SQL file is enforced: Step 1 (CREATE TABLE) → Step 2 (INSERT IGNORE seed) → Step 3 (ADD COLUMN) → Step 4 (UPDATE backfill) → Step 5 (FK) → Step 6 (index). | ✅ |

### Migration SQL: `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql`

Idempotency check — each step uses the proper guard pattern:

| Step | Pattern | Lines | Verdict |
|------|---------|-------|---------|
| 1. CREATE TABLE `unidades_venta` | `CREATE TABLE IF NOT EXISTS` | 48-62 | ✅ Idempotent (table-level IF NOT EXISTS) |
| 2. INSERT seed (UNIDAD, GARRAFA, BOLSA, KG) | `INSERT IGNORE` | 70-74 | ✅ Idempotent (INSERT IGNORE skips duplicate keys only) |
| 3. ADD COLUMN `unidad_venta_id` | `information_schema.COLUMNS` count + `PREPARE/EXECUTE` | 82-87 | ✅ Idempotent (column guard before ALTER) |
| 4. UPDATE backfill `productos JOIN unidades_venta` | Plain UPDATE with `WHERE unidad_venta_id IS NULL` | 93-96 | ✅ Idempotent (only updates unpopulated rows) |
| 5. ADD CONSTRAINT FK `fk_productos_unidad_venta` | `information_schema.TABLE_CONSTRAINTS` count + DROP+ADD via PREPARE/EXECUTE | 104-113 | ✅ Idempotent (drops existing FK before recreating) |
| 6. CREATE INDEX `idx_productos_unidad_venta_id` | `information_schema.STATISTICS` count + PREPARE/EXECUTE | 119-124 | ✅ Idempotent (index guard before CREATE) |

Additional verifications:
- FK is `ON DELETE RESTRICT` (matches `fk_productos_tipo` pattern) — protects against deleting a `unidad_venta` referenced by a product.
- Final `SELECT 'Tabla unidades_venta creada y FK aplicada' AS status;` (line 134) is unconditional, but the per-step guards already made each step a no-op on re-run, so the final status is informational only.
- The legacy `unidad_venta` VARCHAR column is **not** dropped — deferred to a cleanup migration per design Open Question #1. The column coexistence is safe because the Service reads `UnidadVentaId` (FK) first and falls back to `UnidadVenta` (string).
- MySQL 8.x specific: requires `AllowUserVariables=true` in the connection string for the PREPARE/EXECUTE pattern. Documented in the migration header (lines 34-37) and handled by the Testcontainers fixture (`UnidadesVentaMySqlFixture.GetConnectionString`, lines 275-282).

Re-run idempotency is explicitly covered by `Migracion_ReEjecutarEsNoOp_NoProduceError` (lines 216-240), which applies the migration twice and asserts the `unidades_venta` table still has exactly 4 rows.

### Entity / EF configuration

| Check | File | Lines | Verdict |
|-------|------|-------|---------|
| `UnidadVenta` POCO with `Id`, `Codigo`, `Nombre`, `Activo`, audit cols, soft-delete | `Data/Entities/UnidadVenta.cs` | 10-21 | ✅ Matches schema |
| `UnidadVentaConfiguration` mapping snake_case columns + unique index on `Codigo` | `Data/Configurations/UnidadVentaConfiguration.cs` | 18-67 | ✅ `HasIndex(u => u.Codigo).IsUnique().HasDatabaseName("uk_unidades_venta_codigo")` at line 59-61 |
| `DbSet<UnidadVenta> UnidadesVenta` on DbContext | `Data/Context/ExtraGasDbContext.cs` | 17 | ✅ Public DbSet registered |
| `Producto.UnidadVentaId` (ulong?) + navigation `UnidadVentaRef` (renamed to avoid collision with the legacy `UnidadVenta` VARCHAR column during the transition window) | `Data/Entities/Producto.cs` | 13-22, 53-61 | ✅ Both FK and navigation present, with detailed XMLDoc explaining the rename rationale |
| `ProductoConfiguration` FK mapping `HasOne(p => p.UnidadVentaRef)` → `unidades_venta` with `OnDelete(DeleteBehavior.Restrict)` and constraint name `fk_productos_unidad_venta` | `Data/Configurations/ProductoConfiguration.cs` | 107-116 | ✅ Matches migration FK |
| Legacy `UnidadVenta` VARCHAR column retained on `Producto` and `ProductoConfiguration` | `Data/Entities/Producto.cs:11`, `ProductoConfiguration.cs:36-40` | — | ✅ Still present (drop deferred to cleanup migration) |

### DTOs

| Check | File | Lines | Verdict |
|-------|------|-------|---------|
| `ProductoDto.UnidadVentaId` (ulong?) + `UnidadVentaNombre` (string?) | `DTOs/ProductoDto.cs` | 31-32 | ✅ Both fields exposed |
| `CreateProductoDto.UnidadVentaId` (ulong?) with `[Required]` + `[Range(1, ulong.MaxValue)]` | `DTOs/ProductoDto.cs` | 95-98 | ✅ |
| `UpdateProductoDto.UnidadVentaId` (ulong?) with the same validations | `DTOs/ProductoDto.cs` | 149-152 | ✅ |
| `UnidadVentaDto` (Id, Codigo, Nombre) | `DTOs/UnidadVentaDto.cs` | 9-14 | ✅ |
| `MappingProfile.ConfigureUnidadVenta` mapping | `Mappings/MappingProfile.cs` | 18, 185-188 | ✅ `CreateMap<UnidadVenta, UnidadVentaDto>().ReverseMap()` |
| `MappingProfile.ConfigureProducto` explicit mapping for `UnidadVentaNombre` from `UnidadVentaRef.Nombre` | `Mappings/MappingProfile.cs` | 151-152 | ✅ |

### Service & Controller

| Check | File | Lines | Verdict |
|-------|------|-------|---------|
| `IProductoService.GetUnidadesVentaAsync` signature | `Services/Interfaces/IProductoService.cs` | 49 | ✅ |
| Implementation: cached, ordered by `Nombre`, `AsNoTracking()`, TTL 1h | `Services/Implementations/ProductoService.cs` | 178-191 | ✅ Uses `IMemoryCache.GetOrCreateAsync` with `UnidadesVentaCacheKey` constant (line 30) |
| `ProductosController.LoadViewBagsAsync` populates `ViewBag.UnidadesVenta` | `Controllers/ProductosController.cs` | 247-253 | ✅ Line 252 |
| `Create.cshtml` and `Edit.cshtml` render `<select>` populated from ViewBag | `Views/Productos/Create.cshtml:50-56`, `Views/Productos/Edit.cshtml:54-60` | — | ✅ |

---

## Requirement #2 — `Conteo de dependencias antes de Delete`

### Spec scenarios validated

| Scenario | Evidence | Status |
|----------|----------|--------|
| **0 dependencies → direct confirm** | `ProductoSlice3ServiceTests.GetDeleteImpactAsync_NoDependencies_ReturnsAllZeros` (lines 141-160) creates a product with no associated items, asserts all 3 counters return 0 and `HasDependencies=false`. View side: `ProductosControllerDeleteTests.Delete_GET_NoDependencies_StillRendersViewWithImpact` (lines 71-99) verifies the DTO passed to the view has `TotalCount=0` and `HasDependencies=false`, so the View renders the simple confirm form (lines 90-96 of `Delete.cshtml`: hidden `<input name="confirmCode" value="@Model.Codigo" />`, button NOT disabled). | ✅ |
| **any dependency > 0 → type-to-confirm** | `GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts` (lines 162-215) seeds 2 pedido_items + 3 recepcion_items + 1 movimiento_garrafa (via garrafa with matching capacidad_kg) → asserts `PedidoItemsCount=2, RecepcionItemsCount=3, MovimientosGarrafaCount=1, TotalCount=6, HasDependencies=true`. View: `Delete.cshtml` lines 42-67 render the 3 counters + the type-to-confirm input (lines 70-87) when `HasDependencies=true`. The submit button is disabled until the input matches `data-expected-code` (line 101 + JS wiring at lines 113-126). | ✅ |
| **count MUST NOT filter by `deleted_at`** | `GetDeleteImpactAsync_DoesNotFilterByDeletedAt` (lines 217-247) seeds a soft-deleted Pedido with a PedidoItem referencing the product, then asserts the count is 1 (not 0). This explicitly verifies that the SQL does NOT include `WHERE deleted_at IS NULL` on `pedido_items`. Implementation at `ProductoService.cs:225-231` confirms: `_context.PedidoItems.AsNoTracking().CountAsync(pi => pi.ProductoId == id, ct)` — no soft-delete filter. | ✅ |
| **mismatch blocks Delete** | `ProductosControllerDeleteTests.Delete_POST_WrongConfirmCode_ReturnsViewWithError` (lines 102-131) POSTs `confirmCode="GAS-99"` while `producto.Codigo="GAS-10"`, asserts the response is `ViewResult` (not redirect), `ViewBag.ConfirmError` contains "Código incorrecto", and `DeleteAsyncLlamadas=0`. Same flow for empty confirmCode at lines 133-159. Controller at `ProductosController.cs:209-220` does `string.Equals(confirmCode, producto.Codigo, StringComparison.Ordinal)` and re-renders the view with `ViewBag.ConfirmError` on mismatch. | ✅ |

### Critical correction: `movimientos_garrafa` implicit FK — VALIDATED

The design originally specified that `GetDeleteImpactAsync` should count `movimientos_garrafa` directly via FK to `Producto`. During apply, the agent discovered that **`MovimientoGarrafa` has NO FK to `Producto`**. The relationship is implicit:

- `productos.capacidad_kg` is `DECIMAL(8,2) NULL` (migration `20260102_000003_create_productos.sql:20`).
- `garrafas.capacidad_kg` is `TINYINT UNSIGNED NOT NULL` with CHECK `IN (10, 15, 45)` (migration `20260102_000006_create_garrafas.sql:18, 33`).
- `movimientos_garrafa` has no `producto_id` column (migration `20260102_000006_create_garrafas.sql:53-81` lists every column — only `garrafa_id`, `pedido_id`, `recepcion_id`, `cliente_id`, `estado_origen_id`, `estado_destino_id`).

**Implementation interpretation at `ProductoService.cs:241-249`:**

```csharp
int movimientosGarrafa = 0;
if (producto.ManejaGarrafaIndividual && producto.CapacidadKg.HasValue)
{
    var capacidad = (byte)producto.CapacidadKg.Value;
    movimientosGarrafa = await _context.MovimientosGarrafa
        .AsNoTracking()
        .Where(mg => mg.Garrafa != null && mg.Garrafa.CapacidadKg == capacidad)
        .CountAsync(ct);
}
```

**Interpretation verdict: CORRECT and well-justified.**

1. For products with `ManejaGarrafaIndividual=false` (carbón, leña, bolsas), the count is 0 — garrafas don't track these products, so there are no relevant movimientos.
2. For products with `ManejaGarrafaIndividual=true` (gas), the count joins `movimientos_garrafa → garrafas` filtered by `capacidad_kg` byte equality with `(byte)producto.CapacidadKg.Value`.
3. The byte cast is safe in practice because the schema restricts `garrafas.capacidad_kg` to `IN (10, 15, 45)` and gas products are exactly those three capacities.
4. NO `deleted_at` filter on either table — `movimientos_garrafa` has no `deleted_at` column; `garrafas` does have one but the spec says to count all history ("count MUST NOT filter by deleted_at").
5. XMLDoc at `ProductoService.cs:200-210` explicitly explains the implicit relationship and why the design's original "direct count" was wrong.

The correction is documented in:
- `ProductoService.cs:200-210` (XMLDoc)
- `apply-progress.md` section "Deviations from Design" #1
- Integration test `GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts` (covers the implicit path via `Garrafa.CapacidadKg`)

### Implementation summary

| Check | File | Lines | Verdict |
|-------|------|-------|---------|
| `IProductoService.GetDeleteImpactAsync` signature | `Services/Interfaces/IProductoService.cs` | 67 | ✅ `Task<ProductoDeleteImpactDto> GetDeleteImpactAsync(ulong id, CancellationToken ct = default)` |
| `ProductoDeleteImpactDto` record with `ProductoId`, `Codigo`, `PedidoItemsCount`, `RecepcionItemsCount`, `MovimientosGarrafaCount` + computed `TotalCount` + `HasDependencies` | `DTOs/ProductoDeleteImpactDto.cs` | 15-32 | ✅ Matches spec (with the additions of `ProductoId` and `Codigo` echoes that improve the view's UX — documented in apply-progress deviation #3) |
| Implementation: existence check + 3 count queries | `ProductoService.cs` | 211-257 | ✅ Existence check uses `FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct) ?? throw new KeyNotFoundException(...)`. Three independent counts on `PedidoItems`, `RecepcionItems`, `MovimientosGarrafa` (with implicit-FK correction as analyzed above). NO `deleted_at` filter on any of them. |
| `ProductosController.Delete` (GET) → `GetByIdAsync` + `GetDeleteImpactAsync` + view | `Controllers/ProductosController.cs` | 176-194 | ✅ Both calls invoked; impact DTO passed to view; `ViewBag.ExpectedCode` set for JS |
| `ProductosController.Delete` (POST) → confirmCode validation | `Controllers/ProductosController.cs` | 196-228 | ✅ `string.Equals(confirmCode, producto.Codigo, StringComparison.Ordinal)` — case-sensitive, exact match. On mismatch: re-render view with `ViewBag.ConfirmError`. On match: call `DeleteAsync(id, GetCurrentUserId(), ct)` and redirect to Index. |
| AdminOnly policy on both Delete actions | `Controllers/ProductosController.cs` | 177, 197 | ✅ `[Authorize(Policy = "AdminOnly")]` — overrides class-level `OperadorOrAdmin` |
| `Views/Productos/Delete.cshtml` shows counts + type-to-confirm input when `HasDependencies=true` | `Views/Productos/Delete.cshtml` | 42-89 | ✅ `<dl>` with 3 counters (lines 50-62) + `<input name="confirmCode">` (lines 74-83). When `HasDependencies=false` (line 90), a hidden `confirmCode` is set to the product code so the simple confirm path works. |
| `wwwroot/js/productos-delete.js` wires the confirm input to the submit button | `wwwroot/js/productos-delete.js` | 13-26 | ✅ Iterates over `.js-producto-delete-form`, finds `.js-producto-confirm-input` + `.js-producto-confirm-submit`, syncs `submit.disabled = (input.value !== expected)` on every `input` event. The pattern is also duplicated inline in `Delete.cshtml:113-126` because the form is on a single page; the file is the canonical reference for future reuse (per its comment at lines 1-12). |

### Test coverage of the Controller layer

`ProductosControllerDeleteTests.cs` covers:

| Test | Lines | Scenario |
|------|-------|----------|
| `Delete_GET_PassesImpactToView` | 27-68 | GET with deps: ViewResult, DTO with TotalCount=3, HasDependencies=true, ViewBag.ExpectedCode="GAS-10" |
| `Delete_GET_NoDependencies_StillRendersViewWithImpact` | 71-99 | GET without deps: DTO.TotalCount=0, HasDependencies=false |
| `Delete_POST_WrongConfirmCode_ReturnsViewWithError` | 102-131 | POST with wrong code: ViewResult (no redirect), ConfirmError set, DeleteAsync NOT called |
| `Delete_POST_EmptyConfirmCode_ReturnsViewWithError` | 133-159 | POST with empty code: same as wrong code |
| `Delete_POST_CorrectConfirmCode_CallsDeleteAsync_AndRedirectsToIndex` | 162-191 | POST with correct code: DeleteAsync called once, redirect to Index |
| `Delete_GET_UnknownId_ReturnsNotFound` | 194-207 | GET on missing product: NotFoundResult |

All 6 controller tests green.

---

## Requirement #8 — `Catálogo de tipos_producto intencionalmente cerrado`

### Spec scenarios validated

| Scenario | Evidence | Status |
|----------|----------|--------|
| **no UI CRUD exists** | `ls src/ExtraGasMVC/Controllers/` shows no `TiposProductoController` or `UnidadesVentaController`. The only `TiposProducto` / `UnidadesVenta` references in controllers/views are the read-only `GetTiposProductoAsync` / `GetUnidadesVentaAsync` consumers in `ProductosController.cs:249, 252` (used to populate dropdowns) and the corresponding `<select>` consumers in `Create.cshtml` / `Edit.cshtml`. No POST/Edit/Delete routes exist. | ✅ |
| **adding a type needs SQL migration** | ADR #20 documents the workflow: new value = new `.sql` migration under `db/migrations/` that does `INSERT IGNORE INTO unidades_venta / tipos_producto`. The `INSERT IGNORE` pattern is already established (migration line 70-74 for `unidades_venta`; analogous for `tipos_producto`). | ✅ |
| **ADR documents closure** | `db/docs/DECISIONES.md:408-442` — ADR #20 appended with sections Context, Decision, Por qué, Consecuencias, When to revisit. Same format as previous ADRs. Title: "Catálogos cerrados: `tipos_producto` y `unidades_venta` (issue #147 slice 3)". | ✅ |

### ADR validation

| Check | Evidence | Verdict |
|-------|----------|---------|
| File appended (not inserted, not replacing existing ADRs) | `DECISIONES.md` last ADR is numbered #19 (line 368-392) — previous one. #20 is appended at the end (line 408-442). | ✅ |
| Sequential numbering | Existing ADRs go up to #19; new ADR is #20. | ✅ |
| Title mentions `tipos_producto` and "intencionalmente cerrado" / equivalent | Line 408: "Catálogos cerrados: `tipos_producto` y `unidades_venta` (issue #147 slice 3)" — "cerrados" = "closed" (equivalent to "intencionalmente cerrado"). | ✅ |
| Format matches existing ADRs (Context / Decision / Consequences / When to revisit) | All four sections present: "Contexto" (410), "Decisión" (417), "Por qué" (421), "Consecuencias" (429), "When to revisit" (435). Matches the structure used in ADRs #11 (tracking), #14 (auth), #15 (DNI), #17 (clientes.activo). | ✅ |
| Mentions the AdminOnly escape hatch | Line 431-433: "La administración escapa al SQL: si un tipo o unidad se vuelve obsoleto, se aplica un soft-delete ... directamente en la BD". Line 437-441 enumerates the conditions under which the ADR should be revisited (multi-tenant, 3+ types/year, admin-only UI). | ✅ |

---

## Cross-cutting validation

### Strict TDD evidence (per apply-progress)

`apply-progress.md` lines 82-94 record the strict-TDD cycle:

- **3.1 (migration)**: 4 integration tests written RED (FileNotFoundException on missing migration file), then GREEN after the migration was created. Idempotency guards in the migration are the triangulation.
- **3.6 (`GetUnidadesVentaAsync`)**: 3 unit tests written RED with `CS0535` (method missing on interface), then GREEN after the impl was added. Cache hit test is the triangulation.
- **3.9 (`GetDeleteImpactAsync`)**: 5 unit tests written RED with `CS0535`, then GREEN after the impl. The implicit-FK correction is covered explicitly by `GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts`.
- **3.10 (Controller Delete)**: 6 unit tests written RED with `NotImplemented` on `FakeProductoService`, then GREEN after the controller was wired.
- **3.3, 3.4, 3.5 (entity/config/DTOs)**: structural — no RED/GREEN needed.

### Deviations from design (all acceptable, all documented)

From `apply-progress.md` section "Deviations from Design":

1. **`MovimientoGarrafa` NO tiene FK a `Producto`** — counted via `Garrafa.CapacidadKg` JOIN. **Correct critical correction**, validated above.
2. **Navigation `UnidadVentaRef` en lugar de `UnidadVenta`** — collision with the legacy `UnidadVenta` VARCHAR column during transition. **Acceptable**: EF mapping is explicit at `ProductoConfiguration.cs:112` (`HasOne(p => p.UnidadVentaRef)`), and the rename is reversible when the legacy column is dropped.
3. **`int` vs `ulong` in `GetDeleteImpactAsync`** — `int` for `ProductoId` (ViewBag compatibility) with explicit `(int)producto.Id` cast; the id parameter itself is `ulong` to match `Producto.Id`. **Acceptable**: ViewBag dynamic consume.
4. **`UnidadVentaId` excluded from `DetectarCambiosAuditables` when unchanged** — no audit row emitted if the FK didn't change. **Correct**: matches the spec's "no-op update emits zero rows".
5. **Legacy `UnidadVenta` VARCHAR sync in CreateAsync/UpdateAsync** — Service updates the legacy column with the resolved `Codigo` after Map. **Acceptable**: preserves the legacy column during the transition window.

### Issues found during apply (all resolved)

From `apply-progress.md` section "Issues Found":

1. Cross-slice coupling in integration fixtures (the new `unidad_venta_id` column broke 6 pre-existing integration tests). **Resolved**: schema minima updated to include the column and seed `unidades_venta` with id=1.
2. `Robustez146_6_ProductosControllerDelete_TieneAuthorizeAdminOnly` reflection test broke on `AmbiguousMatchException` due to the new GET/POST overloads. **Resolved**: discriminated by `GetParameters().Length`.
3. InMemory provider query filter on `UnidadVenta.DeletedAt` requires `DeletedAt = null` explicitly in seed fixtures. **Acceptable**: default value already null in property initializers.
4. `AllowUserVariables=true` already documented in slice 2; integration test fixtures use it. **No action**: already correct.

---

## Behavioral Compliance Matrix

| Spec requirement / scenario | Test that covers it | Status |
|-----------------------------|---------------------|--------|
| 7 — seed contains 4 values | `Migracion_SembraryCreaTablaUnidadesVenta_ConCuatroValores` | ✅ |
| 7 — FK to `unidades_venta.id` | `Migracion_CreaColumnaUnidadVentaId_YFkHaciaUnidadesVenta` | ✅ |
| 7 — `GetUnidadesVentaAsync` ordered list | `GetUnidadesVentaAsync_ReturnsOrderedListByNombre` | ✅ |
| 7 — Create/Edit uses `<select>` | `Views/Productos/Create.cshtml:50-56`, `Views/Productos/Edit.cshtml:54-60` (manual visual check; no automated view test in this codebase) | ✅ (visual + view builds) |
| 7 — migration order: seed BEFORE ALTER | `Migracion_Backfill_ResuelveUnidadVentaStringAFkId` | ✅ |
| 2 — 0 dependencies → direct confirm | `GetDeleteImpactAsync_NoDependencies_ReturnsAllZeros` + `Delete_GET_NoDependencies_StillRendersViewWithImpact` | ✅ |
| 2 — any dependency > 0 → type-to-confirm | `GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts` + `Delete_GET_PassesImpactToView` | ✅ |
| 2 — count MUST NOT filter by `deleted_at` | `GetDeleteImpactAsync_DoesNotFilterByDeletedAt` | ✅ |
| 2 — mismatch blocks Delete | `Delete_POST_WrongConfirmCode_ReturnsViewWithError` + `Delete_POST_EmptyConfirmCode_ReturnsViewWithError` | ✅ |
| 8 — no UI CRUD exists | No `TiposProductoController` / `UnidadesVentaController` found in `src/ExtraGasMVC/Controllers/` | ✅ |
| 8 — adding a type needs SQL migration | ADR #20 documents the workflow | ✅ (documented) |
| 8 — ADR documents closure | ADR #20 appended with Context / Decision / Por qué / Consecuencias / When to revisit | ✅ |

**10/10 spec scenarios covered by passing tests or documented design contracts.**

---

## Warnings

None. The slice is complete and clean.

Notable quality points (not warnings, just observations):

- **Coverage on new code**: per `apply-progress.md`, 100% line/branch coverage on `UnidadVenta`, `UnidadVentaConfiguration`, `UnidadVentaDto`, `ProductoDeleteImpactDto`, `GetUnidadesVentaAsync`, `GetDeleteImpactAsync`, `ValidarUnidadVentaExisteAsync`, `ResolverCodigoUnidadVentaAsync`, and `ProductosController.Delete`. Well above the project's 65% `new_coverage` custom Quality Gate (per AGENTS.md).
- **Build warnings**: 0 new warnings. Only pre-existing NU1903 (AutoMapper 12.0.1 vulnerability) and SonarQube targets file missing (irrelevant for build correctness).
- **The byte cast** `(byte)producto.CapacidadKg.Value` at `ProductoService.cs:244` is safe given the schema constraint `garrafas.capacidad_kg IN (10, 15, 45)` for products with `ManejaGarrafaIndividual=true`. Documented in the implementation comments.

## Blockers

None.

---

## Risks

1. **ApplyProgress not yet on homelab** (`apply-progress.md` line 113): the migration `20260901_000002` has been validated locally via Testcontainers but has not been applied to the production homelab. **Mitigation**: the slice's next step is `git push` + PR merge; the operator must run `./db/scripts/install.sh` on the homelab after merge per the apply-progress "Next Steps" section.
2. **Pre-existing NU1903 AutoMapper vulnerability**: advisory GHSA-rvv3-g6hj-g44x, high severity. **Mitigation**: out of scope for slice 3; tracked as project-wide dependency upgrade follow-up.
3. **`UnidadVentaRef` rename + legacy column sync** (deviations #2, #5): the live column coexistence is safe, but a future dev must remember to remove the legacy sync code when the cleanup migration drops `unidad_venta`. The cleanup migration is not in scope for slice 3.

---

## Next Recommended Action

**Ship (awaiting PR merge).** All acceptance criteria for items 2, 7, and 8 are met. The slice is ready to be merged into `feat/issue-147-slice-2-audit-log` per the chain strategy, then ultimately into `feat/issue-147-productos-mejoras` (the tracker). After the chain merges, run `./db/scripts/install.sh` on the homelab to apply the new migration.

---

## Relevant Files

### Production code (new)
- `src/ExtraGasMVC/Data/Entities/UnidadVenta.cs`
- `src/ExtraGasMVC/Data/Configurations/UnidadVentaConfiguration.cs`
- `src/ExtraGasMVC/DTOs/UnidadVentaDto.cs`
- `src/ExtraGasMVC/DTOs/ProductoDeleteImpactDto.cs`
- `src/ExtraGasMVC/Views/Productos/Delete.cshtml`
- `src/ExtraGasMVC/wwwroot/js/productos-delete.js`

### Production code (modified)
- `src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs`
- `src/ExtraGasMVC/Data/Entities/Producto.cs`
- `src/ExtraGasMVC/Data/Configurations/ProductoConfiguration.cs`
- `src/ExtraGasMVC/DTOs/ProductoDto.cs`
- `src/ExtraGasMVC/Mappings/MappingProfile.cs`
- `src/ExtraGasMVC/Services/Interfaces/IProductoService.cs`
- `src/ExtraGasMVC/Services/Implementations/ProductoService.cs`
- `src/ExtraGasMVC/Controllers/ProductosController.cs`
- `src/ExtraGasMVC/Views/Productos/Create.cshtml`
- `src/ExtraGasMVC/Views/Productos/Edit.cshtml`
- `db/docs/DECISIONES.md` (ADR #20 appended)

### Database
- `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql`

### Tests (new)
- `tests/ExtraGasMVC.Tests/ProductoSlice3ServiceTests.cs` (8 tests)
- `tests/ExtraGasMVC.Tests/ProductosControllerDeleteTests.cs` (6 tests)
- `tests/ExtraGasMVC.Tests/Integration/UnidadesVentaMigrationIntegrationTests.cs` (4 tests + fixture)

### Tests (modified, cross-slice fixture updates)
- `tests/ExtraGasMVC.Tests/Integration/ProductoAuditLogIntegrationTests.cs`
- `tests/ExtraGasMVC.Tests/Integration/PedidoCanjeIntegrationTests.cs`
- `tests/ExtraGasMVC.Tests/Integration/ProductoActivoRaceIntegrationTests.cs`
- `tests/ExtraGasMVC.Tests/Integration/ProductoPrecioHistoricoIntegrationTests.cs`
- `tests/ExtraGasMVC.Tests/PedidosControllerCommandTests.cs`
- `tests/ExtraGasMVC.Tests/PedidosControllerIndexTests.cs`
- `tests/ExtraGasMVC.Tests/ControllersActivoViewBagTests.cs`
- `tests/ExtraGasMVC.Tests/ProductoAuditLogTests.cs`
- `tests/ExtraGasMVC.Tests/ProductoServiceRobustezTests.cs`
- `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs`
- `tests/ExtraGasMVC.Tests/RecepcionServiceTests.cs`

### Spec/design artifacts
- `openspec/changes/issue-147-productos-mejoras/specs/productos/spec.md`
- `openspec/changes/issue-147-productos-mejoras/design.md`
- `openspec/changes/issue-147-productos-mejoras/tasks.md`
- `openspec/changes/issue-147-productos-mejoras/apply-progress.md`
