# Apply Progress — issue-147-productos-mejoras (Slices 1 + 2 + 3)

> **Date**: 2026-08-31
> **Branch**: `feat/issue-147-slice-3-delete-unidadventa-adr` (slice 3 work; branch already includes slice 1 + 2 commits)
> **Tracker**: `feat/issue-147-productos-mejoras`
> **Mode**: Strict TDD (RED → GREEN → REFACTOR for every task)
> **Chain strategy**: feature-branch-chain — Slice 3 PR targets `feat/issue-147-slice-2-audit-log`

## Slices 1 + 2 + 3 Status

**All slices complete.** 24 atomic work-unit commits across the three slices, 427/427 tests green.

### Commits (24 atomic work units, last 11 are Slice 3)

| # | SHA | Subject | Slice |
|---|------|---------|-------|
| 1 | `3591e62` | feat(extensions): StringNormalizer.TrimAndUpper + tests (#147) | 1 |
| 2 | `75bc3ae` | feat(productos): normalizar Codigo en Create/Update/Get/Search + tests (#147) | 1 |
| 3 | `ddc1258` | feat(productos): exponer audit fields en ProductoDto + MappingProfile explicito (#147) | 1 |
| 4 | `efe1728` | feat(productos): audit enrichment en GetByIdAsync + LoadAuditUsersAsync + tests (#147) | 1 |
| 5 | `4f8437d` | feat(productos): audit fields visibles en Details/Edit views (#147) | 1 |
| 6 | `1cb8ecc` | test(productos): cubrir 7 branches faltantes en ProductoService (#147) | 1 |
| 7 | `6453953` | feat(productos): IMemoryCache en GetTiposProductoAsync + cache hit test (#147) | 1 |
| 8 | `84989a0` | feat(db): tabla audit_log para auditoría genérica por campo (#147) | 2 |
| 9 | `09b6588` | feat(data): AuditLogEntry entity + EF config + DbContext DbSet (#147) | 2 |
| 10 | `2001a32` | feat(services): IAuditLogger + AuditLogger Scoped + 6 tests RED→GREEN (#147) | 2 |
| 11 | `17b6d3f` | feat(productos): UpdateAsync emite per-field audit events + 5 tests (#147) | 2 |
| 12 | `cf3fe0c` | test(productos): integración audit_log con Testcontainers (4 tests) (#147) | 2 |
| 13 | `f3ceafc` | test(productos): añadir audit_log al schema mínimo de fixture pre-existente (#147) | 2 |
| 14 | `028237a` | feat(db): tabla unidades_venta + FK + backfill desde unidad_venta legacy (#147) | **3** |
| 15 | `fb75cd8` | feat(data): Producto FK a unidades_venta (UnidadVentaId + UnidadVentaRef nav) (#147) | **3** |
| 16 | `f5fc246` | feat(data): UnidadVenta entity + EF config + DbContext DbSet (#147) | **3** |
| 17 | `0e8b1f6` | feat(dto): ProductoDto+CreateDto+UpdateDto+MappingProfile para UnidadVentaId (#147) | **3** |
| 18 | `ddcf7d0` | feat(services): GetUnidadesVentaAsync + GetDeleteImpactAsync en ProductoService (#147) | **3** |
| 19 | `58c2133` | feat(controller): ProductosController.Delete GET/POST con GetDeleteImpactAsync + confirmCode (#147) | **3** |
| 20 | `d427153` | feat(views): Create/Edit usan select para UnidadVentaId + Delete con type-to-confirm (#147) | **3** |
| 21 | `8a00ddf` | test(productos): 8 service tests + 6 controller tests para slice 3 (#147) | **3** |
| 22 | `96fa77e` | test(integration): unidades_venta migration Testcontainers (4 tests) (#147) | **3** |
| 23 | `9cce302` | docs(decisiones): ADR #20 — catalogos cerrados tipos_producto y unidades_venta (#147) | **3** |
| 24 | `91161d1` | test(productos): actualizar fixtures con UnidadVentaId + unidades_venta schema (#147) | **3** |

### Slice 3 Files Changed

| File | Action | LOC |
|------|--------|-----|
| `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql` | Created | +134 |
| `src/ExtraGasMVC/Data/Entities/UnidadVenta.cs` | Created | +27 |
| `src/ExtraGasMVC/Data/Configurations/UnidadVentaConfiguration.cs` | Created | +68 |
| `src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs` | Modified | +6 |
| `src/ExtraGasMVC/Data/Entities/Producto.cs` | Modified | +14 |
| `src/ExtraGasMVC/Data/Configurations/ProductoConfiguration.cs` | Modified | +14 |
| `src/ExtraGasMVC/DTOs/UnidadVentaDto.cs` | Created | +10 |
| `src/ExtraGasMVC/DTOs/ProductoDto.cs` | Modified | +12/-3 |
| `src/ExtraGasMVC/DTOs/ProductoDeleteImpactDto.cs` | Created | +37 |
| `src/ExtraGasMVC/Mappings/MappingProfile.cs` | Modified | +8 |
| `src/ExtraGasMVC/Services/Interfaces/IProductoService.cs` | Modified | +20 |
| `src/ExtraGasMVC/Services/Implementations/ProductoService.cs` | Modified | +200/-5 |
| `src/ExtraGasMVC/Controllers/ProductosController.cs` | Modified | +72/-23 |
| `src/ExtraGasMVC/Views/Productos/Create.cshtml` | Modified | +17/-3 |
| `src/ExtraGasMVC/Views/Productos/Edit.cshtml` | Modified | +17/-3 |
| `src/ExtraGasMVC/Views/Productos/Delete.cshtml` | Created | +125 |
| `src/ExtraGasMVC/wwwroot/js/productos-delete.js` | Created | +24 |
| `db/docs/DECISIONES.md` | Modified | +38 |
| `tests/ExtraGasMVC.Tests/ProductoSlice3ServiceTests.cs` | Created | +285 |
| `tests/ExtraGasMVC.Tests/ProductosControllerDeleteTests.cs` | Created | +310 |
| `tests/ExtraGasMVC.Tests/Integration/UnidadesVentaMigrationIntegrationTests.cs` | Created | +455 |
| `tests/ExtraGasMVC.Tests/*` (11 files cross-slice fixture updates) | Modified | +158/-18 |

**Total slice 3**: ~1500 insertions, ~70 deletions across 30 files (incl. cross-slice fixture adjustments).

### Test Delta

- After Slice 1: 394 tests passing (24 new)
- After Slice 2: 409 tests passing (15 new since slice 1)
- **After Slice 3: 427 tests passing, 0 failed (18 new since slice 2)**
  - 8 ProductoSlice3ServiceTests (3 GetUnidadesVentaAsync + 5 GetDeleteImpactAsync)
  - 6 ProductosControllerDeleteTests (GET + POST + 404)
  - 4 UnidadesVentaMigrationIntegrationTests (Testcontainers MySQL 8.0)
- **Total tests added in slice 3: 18** (14 unit + 4 integration)
- **Total tests added in slices 1+2+3: 57** (24 + 15 + 18)

### TDD Cycle Evidence (Slice 3 — Strict TDD Mode)

| Task | Test File | Layer | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|-----|-------|-------------|----------|
| 3.1 | `Integration/UnidadesVentaMigrationIntegrationTests.cs` | Integration | ✅ 4 written, RED (FileNotFoundException) | ✅ 4/4 passed | ✅ 4 cases | ➖ Idempotency guards |
| 3.3 | (entity structural) | — | N/A (new) | ➖ Structural | ➖ Single | ➖ None |
| 3.4 | (entity/config structural) | — | N/A (new) | ➖ Structural | ➖ Single | ➖ None |
| 3.5 | (DTO structural + MappingProfile) | — | N/A (new) | ➖ Structural | ➖ Single | ➖ None |
| 3.6 | `ProductoSlice3ServiceTests.GetUnidadesVentaAsync_*` | Unit | ✅ 3 written, RED (CS0535 — method missing) | ✅ 3/3 passed | ✅ 3 cases | ✅ cache helper |
| 3.9 | `ProductoSlice3ServiceTests.GetDeleteImpactAsync_*` | Unit | ✅ 5 written, RED (CS0535) | ✅ 5/5 passed | ✅ 5 cases | ✅ ulong param |
| 3.10 | `ProductosControllerDeleteTests` | Unit | ✅ 6 written, RED (NotImplemented) | ✅ 6/6 passed | ✅ 6 cases | ✅ overload-aware reflection |
| 3.11 | (view + JS) | — | N/A | ✅ manual render verified | ➖ Single view | ✅ JS standalone |

### Coverage on new code (Slice 3)

| Class | Line rate | Branch rate |
|-------|-----------|-------------|
| `ExtraGasMVC.Data.Entities.UnidadVenta` | 100% | 100% |
| `ExtraGasMVC.Data.Configurations.UnidadVentaConfiguration` | 100% | 100% |
| `ExtraGasMVC.DTOs.UnidadVentaDto` | 100% | — (no logic) |
| `ExtraGasMVC.DTOs.ProductoDeleteImpactDto` | 100% | — (record w/ computed props) |
| `ExtraGasMVC.Services.Implementations.ProductoService.GetUnidadesVentaAsync` | 100% | 100% |
| `ExtraGasMVC.Services.Implementations.ProductoService.GetDeleteImpactAsync` | 100% | 100% |
| `ExtraGasMVC.Services.Implementations.ProductoService.ValidarUnidadVentaExisteAsync` | 100% | 100% |
| `ExtraGasMVC.Services.Implementations.ProductoService.ResolverCodigoUnidadVentaAsync` | 100% | 100% |
| `ExtraGasMVC.Controllers.ProductosController.Delete` (GET + POST) | 100% | 100% |

All well above the **65% `new_coverage` gate** (SonarQube custom Quality Gate per AGENTS.md).

## Acceptance Criteria (Slice 3)

- [x] `unidades_venta` lookup table created via Testcontainers migration test (live MySQL application deferred to `install.sh` at merge; homelab unreachable in this session).
- [x] 4 seed rows (UNIDAD, GARRAFA, BOLSA, KG) verified by integration test.
- [x] `Producto.UnidadVentaId` FK + `UnidadVentaRef` navigation property.
- [x] `ProductoDto` exposes `UnidadVentaId` + `UnidadVentaNombre`.
- [x] `GetUnidadesVentaAsync` returns ordered list of active unidades (3 tests + cache hit test).
- [x] `Create.cshtml` + `Edit.cshtml` use `<select>` populated from service (NOT free input).
- [x] `GetDeleteImpactAsync` returns counts without `deleted_at` filter on the 3 dependency tables (5 tests including explicit "no filter" test).
- [x] `ProductosController.Delete` GET passes impact to view (1 test).
- [x] `ProductosController.Delete` POST validates `confirmCode == producto.Codigo`, returns error on mismatch (3 tests including empty/null edge cases).
- [x] `Delete.cshtml` shows warning + type-to-confirm input when `TotalCount > 0` (SweetAlert2 already loaded via `_Scripts.cshtml`).
- [x] Tests: 8 ProductoServiceTests + 6 ProductosControllerTests + 4 integration = **18 new tests**.
- [x] ADR #20 in `db/docs/DECISIONES.md` documenting `tipos_producto` and `unidades_venta` as intentionally closed catalogs.
- [x] Full test suite green: **427/427** (was 409 after slice 2, +18).
- [x] `dotnet build` clean: 0 new warnings (only pre-existing CS8602 in `Recepciones/Create.cshtml` and NU1903 AutoMapper vulnerability).

## Deviations from Design

1. **`MovimientoGarrafa` NO tiene FK a `Producto`** — el orchestrator asumió que sí (basado en el exploration #43-45 que solo mencionó la falta de `deleted_at`). En realidad el vínculo es implícito: `MovimientoGarrafa.Garrafa.CapacidadKg` (byte) = `Producto.CapacidadKg` (decimal). El Service cuenta via JOIN a `garrafas` filtrando por capacidad, y solo para productos con `ManejaGarrafaIndividual=true`. Documentado en `ProductoService.GetDeleteImpactAsync` y en el test `GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts`.

2. **Navigation `UnidadVentaRef` en lugar de `UnidadVenta`** — colisión con la columna legacy `UnidadVenta` (VARCHAR) que sigue en la entity durante la ventana de transición. C# no permite dos members con el mismo nombre. La configuration EF lo mapea explícitamente vía `HasOne(p => p.UnidadVentaRef)`. Cuando se haga el DROP COLUMN cleanup, el legacy string desaparece y la navigation se renombra a `UnidadVenta`.

3. **Tipo `int` vs `ulong` en `GetDeleteImpactAsync`** — el orchestrator especificó `int` en el prompt; lo cambié a `ulong` para consistencia con `Producto.Id` (la entity usa `ulong`). El `ProductoDeleteImpactDto.ProductoId` quedó como `int` por compatibilidad con la vista (ViewBag dynamic consume int), pero es un cast explícito `(int)producto.Id`.

4. **Excluido `UnidadVentaId` del `DetectarCambiosAuditables` cuando no cambió** — el helper emite un row solo si el FK cambió. Si el operador reenvía el form sin tocar la unidad, no hay fila de audit (correcto).

5. **Sincronización legacy `UnidadVenta` (VARCHAR) en CreateAsync/UpdateAsync** — el Service actualiza la columna legacy con el `Codigo` correspondiente al FK después del Map. Esto preserva el contrato de la columna durante la ventana de transición (queries que aún lean `unidad_venta` siguen funcionando). El DROP COLUMN queda deferido a la migración cleanup.

## Issues Found

1. **Cross-slice coupling en fixtures de integración** — la nueva columna `unidad_venta_id` + el sync del VARCHAR legacy con el FK rompe 6 integration tests pre-existentes (`PedidoCanjeIntegrationTests`, `ProductoActivoRaceIntegrationTests`, `ProductoPrecioHistoricoIntegrationTests`, `Integration/ProductoAuditLogIntegrationTests`). Resuelto agregando la columna a cada schema minimal y sembrando `unidades_venta` con id=1.

2. **Reflection-based test `Robustez146_6_ProductosControllerDelete_TieneAuthorizeAdminOnly`** rompía con `AmbiguousMatchException` porque ahora hay dos overloads de `Delete` (GET y POST). Resuelto discriminando por `GetParameters().Length`.

3. **InMemory provider: query filter `DeletedAt == null` en `UnidadVentaConfiguration`** oculta todas las unidades si el seed no setea explícitamente `DeletedAt = null`. Como las properties default ya tienen `DeletedAt = null`, no rompe los tests InMemory, pero requiere que las migration schemas mínimas sí tengan la columna (los `?` nullable son válidos).

4. **`Map.AllowUserVariables=true`** en connection string (ya documentado en slice 2) — la nueva migración `20260901_000002` usa el mismo patrón PREPARE/EXECUTE, por lo que los integration tests deben seguir seteándolo en la connection string.

## TDD Reflection (Slice 3)

Strict TDD funcionó muy bien para los métodos con lógica:
- `GetUnidadesVentaAsync` y `GetDeleteImpactAsync`: tests escritos ANTES que el método existiera → `CS0535: 'IProductoService' does not contain a definition for ...`. Mínimo impl para GREEN (3 líneas para GetUnidadesVenta con cache + helper privado; 30 líneas para GetDeleteImpact con 3 COUNT + ValidarExistencia).
- `ProductosController.Delete` GET/POST: tests con `FakeProductoService` configurable, derivados de `PedidosControllerCommandTests.ConfigurablePedidoService` pattern. Override de `Delete` signature y reflection-aware attribute check.

Para las partes estructurales (entities, configs, DTOs, views), marqué ➖ Structural en la evidencia — el TDD strict pierde valor cuando no hay lógica testeable (un POCO con 11 properties o un EF mapping).

## Slice 3 Rollback

Cada commit es independiente. El orden de revert es:
1. `91161d1` revertir fixtures cross-slice
2. `9cce302` quitar ADR #20
3. `96fa77e` borrar integration test
4. `8a00ddf` borrar tests service+controller
5. `d427153` revertir views (Create/Edit <select> → <input>, borrar Delete.cshtml + JS)
6. `58c2133` revertir Controller (Delete sin confirmCode)
7. `ddcf7d0` quitar GetUnidadesVentaAsync + GetDeleteImpactAsync, revertir ProductoService.UpdateAsync
8. `0e8b1f6` revertir DTOs + MappingProfile
9. `f5fc246` borrar entity+config+DbContext
10. `fb75cd8` revertir Producto entity + config (quitar FK)
11. `028237a` DROP TABLE unidades_venta + FK + column

La migración es no-op al re-ejecutarla (`information_schema` guards), y la columna `unidad_venta_id` puede quedar en la BD indefinidamente (no molesta — nadie la lee). Safe rollback boundary.

## Workload / PR Boundary (Slice 3)

- **Mode**: Chained PR slice (`feature-branch-chain`) → `feat/issue-147-slice-2-audit-log`
- **PR target**: `feat/issue-147-slice-2-audit-log` (per orchestrator's `feature-branch-chain` strategy — child PRs target the immediate previous PR's branch, NOT the tracker)
- **Changed lines (this slice only)**: ~1500 insertions, ~70 deletions across 30 files
- **Review budget impact**: Above 400-line threshold (intentional for `feature-branch-chain` — each slice is a reviewable unit despite the size; the diff is coherent: one new feature with tests, one schema migration, one ADR)
- **Risk**: medium-low. La migración tiene guards idempotentes (PREPARE/EXECUTE + CREATE IF NOT EXISTS); el FK es opcional durante la ventana de transición; el sync legacy es no-op si el id no cambia. El delete-impact UI exige type-to-confirm para hacer el delete seguro.

## Next Steps

- `git push -u origin feat/issue-147-slice-3-delete-unidadventa-adr`
- Open PR: `feat/issue-147-slice-3-delete-unidadventa-adr` → `feat/issue-147-slice-2-audit-log` (per `feature-branch-chain` — PR #C in the chain)
- `sdd-verify` after all 3 PRs merge to the tracker `feat/issue-147-productos-mejoras`
- Apply all 3 SQL migrations to homelab via `./db/scripts/install.sh` at merge

## Relevant Files (paths only — see commits for diffs)

- `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql`
- `src/ExtraGasMVC/Data/Entities/UnidadVenta.cs`
- `src/ExtraGasMVC/Data/Configurations/UnidadVentaConfiguration.cs`
- `src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs`
- `src/ExtraGasMVC/Data/Entities/Producto.cs`
- `src/ExtraGasMVC/Data/Configurations/ProductoConfiguration.cs`
- `src/ExtraGasMVC/DTOs/UnidadVentaDto.cs`
- `src/ExtraGasMVC/DTOs/ProductoDto.cs`
- `src/ExtraGasMVC/DTOs/ProductoDeleteImpactDto.cs`
- `src/ExtraGasMVC/Mappings/MappingProfile.cs`
- `src/ExtraGasMVC/Services/Interfaces/IProductoService.cs`
- `src/ExtraGasMVC/Services/Implementations/ProductoService.cs`
- `src/ExtraGasMVC/Controllers/ProductosController.cs`
- `src/ExtraGasMVC/Views/Productos/Create.cshtml`
- `src/ExtraGasMVC/Views/Productos/Edit.cshtml`
- `src/ExtraGasMVC/Views/Productos/Delete.cshtml`
- `src/ExtraGasMVC/wwwroot/js/productos-delete.js`
- `db/docs/DECISIONES.md` (ADR #20)
- `tests/ExtraGasMVC.Tests/ProductoSlice3ServiceTests.cs`
- `tests/ExtraGasMVC.Tests/ProductosControllerDeleteTests.cs`
- `tests/ExtraGasMVC.Tests/Integration/UnidadesVentaMigrationIntegrationTests.cs`
