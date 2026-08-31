# Tasks: issue-147-productos-mejoras

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines total | ~1060 |
| 400-line budget risk | Low (each slice ≤400) |
| Chained PRs recommended | Yes |
| Suggested split | PR #A (Slice 1, ~350) → PR #B (Slice 2, ~330) → PR #C (Slice 3, ~380) |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main (user-locked) |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Low

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Normalize + audit fields + tests + cache (no schema) | PR #A | base = main |
| 2 | audit_log infra + per-field logging in UpdateAsync | PR #B | base = main (stacked on #A) |
| 3 | unidades_venta catalog + delete-impact UI + ADR | PR #C | base = main (stacked on #B) |

**Resolved design open questions:** (1) Slice 3 unit `unidad_venta` column DROP **deferred** to follow-up cleanup migration. (2) Slice 3 delete JS lives in `wwwroot/js/productos-delete.js` (separate file). (3) Slice 2 uses one `LogChangeAsync` call per changed field (no batch overload).

## Phase 1: Slice 1 — Items 6 + 4 + 5 + 1 (~350 LOC, PR #A → main)

- [x] 1.1 **Task 1.1**: Add `StringNormalizer.TrimAndUpper(string?) → string` (returns `string.Empty` for null/whitespace). Tests: `StringNormalizerTests.TrimAndUpper_*` (5 cases incl. `TrimAndUpper_NullReturnsEmpty`). GREEN: pure helper. Acceptance: spec "TrimAndUpper(null) → empty".
- [x] 1.2 **Task 1.2**: `ProductoService.CreateAsync/UpdateAsync/GetByCodigoAsync/GetPagedAsync` apply `TrimAndUpper` to `Codigo` before persist/query. RED: `ProductoServiceTests.CreateAsync_NormalizesCodigo_BeforePersisting` + `GetByCodigoAsync_LowercaseInput_MatchesStoredUppercase`. GREEN: call `StringNormalizer.TrimAndUpper(dto.Codigo)` at service boundary. Acceptance: spec "Create persists normalized", "GetByCodigoAsync matches normalized input", "Index search normalizes input".
- [x] 1.3 **Task 1.3**: Add 4 audit fields to `ProductoDto` (`CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName`). `MappingProfile.ConfigureProducto` adds explicit `.ForMember` for usernames. `ProductoService.GetByIdAsync` resolves usernames via `LoadAuditUsersAsync` (mirror `UsuarioService:554-621`). NEW test file `MappingProfileProductoTests.cs`: `AssertConfigurationIsValid` + `CreatedByUserName_NotOverwrittenByEntityFK`. Acceptance: spec "ProductoDto populates 4 audit fields", "AutoMapper MUST NOT overwrite usernames".
- [x] 1.4 **Task 1.4**: `Views/Productos/Details.cshtml` renders AdminLTE audit card with `<dl>` (4 rows). `Views/Productos/Edit.cshtml` adds read-only audit info row (not bound). Acceptance: spec "Details.cshtml renders audit card", "Edit.cshtml renders audit fields read-only".
- [x] 1.5 **Task 1.5**: Add 7 missing-branch tests to `ProductoServiceTests.cs`: `GetByCodigoAsync_NotFound_ReturnsNull`, `GetByCodigoAsync_SoftDeleted_ReturnsNull`, `GetByTipoAsync_UnknownTipo_ReturnsEmpty`, `GetActivosAsync_MixedStatus_ReturnsOnlyActive`, `UpdateAsync_UnknownId_ThrowsKeyNotFoundException`, `DeleteAsync_UnknownId_ReturnsFalse`, `CreateAsync_NullUser_Succeeds`. RED → GREEN (existing helper, no production change). Acceptance: spec scenarios for all 7.
- [x] 1.6 **Task 1.6**: Inject `IMemoryCache` into `ProductoService` (ctor). Wrap `GetTiposProductoAsync` body with `_cache.GetOrCreateAsync("tipos_producto", entry => { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); ... })`. Add `ProductoServiceTests.GetTiposProductoAsync_SecondCall_HitsCache` (counts `_context.TiposProducto` invocations via InMemory provider). GREEN: cache layer wraps existing query. Acceptance: spec "2nd call within 1 h is cached", "seed-only catalog tolerates 1 h staleness".
- [x] 1.7 **Task 1.7**: Verification — `dotnet build src/ExtraGasMVC` clean, `dotnet test tests/ExtraGasMVC.Tests` all green. Rollback: revert commit (DTO/service/views/test changes are reversible, no schema touched). Out of scope: cache eviction hook (forward-looking, documented in code comment per design).

## Phase 2: Slice 2 — Item 3 (~330 LOC, PR #B → main, stacked on #A)

- [ ] 2.1 **Task 2.1**: Create `db/migrations/20260901_000001_create_audit_log.sql` — `CREATE TABLE IF NOT EXISTS audit_log (...)` with columns per design table + `KEY idx_audit_entidad_registro (entidad, registro_id, changed_at)`. Idempotent header style (issue link, `IF NOT EXISTS`). Acceptance: spec "composite index exists".
- [ ] 2.2 **Task 2.2**: Apply migration locally via `./db/scripts/install.sh` with migrator user; verify `schema_migrations` row inserted with SHA256 checksum. Manual idempotent step. Acceptance: `SELECT * FROM schema_migrations WHERE filename LIKE '%audit_log%'` returns 1 row.
- [ ] 2.3 **Task 2.3**: Create `Data/Entities/AuditLogEntry.cs` POCO (id, entidad, registroId, campo, valorAnterior, valorNuevo, changedBy, changedAt). Create `Data/Configurations/AuditLogEntryConfiguration.cs` mapping columns + index. Add `DbSet<AuditLogEntry> AuditLog` to `ExtraGasDbContext.cs`. GREEN: build clean, EF model recognizes entity. Acceptance: design §"audit_log table design".
- [ ] 2.4 **Task 2.4**: Create `Services/Interfaces/IAuditLogger.cs` with `LogChangeAsync(entidad, registroId, campo, valorAnterior, valorNuevo, changedBy, ct)`. Create `Services/Implementations/AuditLogger.cs` (Scoped, try/catch + swallow like `AuditoriaLoginService`). Register in `Program.cs` line ~73. GREEN: DI resolves. Acceptance: design §"IAuditLogger interface shape".
- [ ] 2.5 **Task 2.5**: Inject `IAuditLogger _audit` into `ProductoService`. Add private `DetectarCambiosAuditables(Producto before, Producto after)` helper (parallel to existing `DetectarCambiosProducto` at lines 472-494). Returns `List<(string campo, string? oldStr, string? newStr)>` for `Codigo, Nombre, Descripcion, TipoProductoId, CapacidadKg, UnidadVenta, PrecioActual, ManejaGarrafaIndividual, Activo`. NEW test `ProductoServiceAuditLogTests.UpdateAsync_PriceChange_EmitsOneRow` (RED → GREEN). Acceptance: spec "precio change emits one row".
- [ ] 2.6 **Task 2.6**: `ProductoService.UpdateAsync` calls `_audit.LogChangeAsync("Producto", id, campo, old, new, usuarioId, ct)` for each diff tuple BEFORE its own `SaveChangesAsync` (atomic via shared `DbContext`). NEW test `ProductoServiceAuditLogTests.UpdateAsync_NoChange_EmitsZeroRows`. Acceptance: spec "no-op update emits zero rows".
- [ ] 2.7 **Task 2.7**: NEW `ProductoAuditLogIntegrationTests.cs` — Testcontainers.MySql fixture, full `UpdateAsync` flow, assert `audit_log` rows after `SaveChangesAsync`. Acceptance: spec scenarios verified end-to-end.
- [ ] 2.8 **Task 2.8**: Verification — `dotnet build` + full suite green. Rollback: drop `audit_log` table + revert service hook (audit failure never blocks writes per design). Out of scope: cleanup cron (suggested follow-up issue).

## Phase 3: Slice 3 — Items 2 + 7 + 8 (~380 LOC, PR #C → main, stacked on #B)

- [ ] 3.1 **Task 3.1**: Create `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql` with 6 statements in correct order: (1) `CREATE TABLE IF NOT EXISTS unidades_venta` (mirror `tipos_producto`), (2) `INSERT IGNORE` seed (UNIDAD, GARRAFA, BOLSA, KG), (3) `ADD COLUMN unidad_venta_id` (guarded via `information_schema`+`PREPARE`/`EXECUTE`), (4) backfill `UPDATE productos JOIN unidades_venta SET unidad_venta_id = u.id`, (5) `ADD CONSTRAINT fk_productos_unidad_venta` (guarded), (6) **DEFER** `DROP COLUMN unidad_venta` to follow-up. Acceptance: spec "seed contains 4 values", "FK to unidades_venta.id", "migration order: seed BEFORE ALTER".
- [ ] 3.2 **Task 3.2**: Apply migration locally; smoke-test `SELECT p.id, p.unidad_venta, p.unidad_venta_id FROM productos p` shows FK populated. Acceptance: existing products with `unidad_venta='GARRAFA'` resolve FK id correctly (spec implied).
- [ ] 3.3 **Task 3.3**: Create `Data/Entities/UnidadVenta.cs` POCO (mirror `TipoProducto.cs`). Create `Data/Configurations/UnidadVentaConfiguration.cs` (`HasIndex(u => u.Codigo).IsUnique()`). Add `DbSet<UnidadVenta> UnidadesVenta` to DbContext. GREEN: build clean. Acceptance: design §"unidades_venta catalog".
- [ ] 3.4 **Task 3.4**: Modify `Producto.cs`: add `UnidadVentaId` (ulong?) + `UnidadVenta` navigation. Keep `UnidadVentaString` (legacy column read-only). Modify `ProductoConfiguration.cs`: FK `HasOne(p => p.UnidadVenta).WithMany().HasForeignKey(p => p.UnidadVentaId).OnDelete(DeleteBehavior.Restrict)`. NEW test `ProductoServiceTests.GetByIdAsync_LoadsUnidadVentaNombre`. Acceptance: design §"DTO changes".
- [ ] 3.5 **Task 3.5**: Modify `ProductoDto.cs`: replace `UnidadVenta` string with `UnidadVentaId` (ulong?) + `UnidadVentaNombre` (string?). Create `UnidadVentaDto.cs`. Modify `CreateProductoDto.cs`/`UpdateProductoDto.cs`: `UnidadVentaId` (ulong, `[Range(1, ulong.MaxValue)]`). Modify `MappingProfile.cs`: `.ForMember(d => d.UnidadVentaNombre, o => o.MapFrom(s => s.UnidadVenta != null ? s.UnidadVenta.Nombre : null))`. Acceptance: design §"DTO changes".
- [ ] 3.6 **Task 3.6**: Add `IProductoService.GetUnidadesVentaAsync(ct)` + `CountDependenciesAsync(ulong id, ct)`. Implement with `_cache.GetOrCreateAsync("unidades_venta", entry => { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); ... })` ordered by `Nombre`. NEW test `ProductoServiceTests.GetUnidadesVentaAsync_ReturnsOrderedList`. Acceptance: spec "GetUnidadesVentaAsync ordered list".
- [ ] 3.7 **Task 3.7**: `ProductosController.LoadViewBagsAsync` adds `ViewBag.UnidadesVenta = await _productoService.GetUnidadesVentaAsync(ct)`. Acceptance: design §"Controller".
- [ ] 3.8 **Task 3.8**: `Views/Productos/Create.cshtml` + `Edit.cshtml`: replace `<input asp-for="UnidadVenta" />` with `<select asp-for="UnidadVentaId" asp-items="@(new SelectList(ViewBag.UnidadesVenta, "Id", "Nombre"))"><option value="">(seleccione)</option></select>`. Acceptance: spec "Create/Edit uses <select>".
- [ ] 3.9 **Task 3.9**: `ProductoService.CountDependenciesAsync(ulong id, ct)` — 3 `AsNoTracking().CountAsync()` queries against `PedidoItems`/`RecepcionItems`/`MovimientosGarrafa`. **CRITICAL: NO `deleted_at` filter** (those tables have no such column per exploration #43-45). Returns `ProductoDeleteImpactDto(int PedidoItems, int RecepcionItems, int MovimientosGarrafa)` with `Total`/`HasDependencies`. NEW test `ProductoServiceCountDependenciesTests.CountDependenciesAsync_MixedRows_ReturnsAllCounts_NoDeletedAtFilter` (RED → GREEN). Acceptance: spec "count MUST NOT filter by deleted_at", "0 dependencies → direct confirm", "any dependency > 0 → type-to-confirm".
- [ ] 3.10 **Task 3.10**: Add `[HttpGet] Delete(ulong id, ct)` to `ProductosController` (calls `GetByIdAsync` + `CountDependenciesAsync`, passes both to view). Modify existing `[HttpPost] Delete` to verify `confirmCode` matches `producto.Codigo` (case-sensitive, exact). NEW test `ProductosControllerDeleteTests.Delete_GET_PassesImpactToView` + `Delete_POST_MismatchedConfirmCode_Returns400`. Acceptance: spec "mismatch blocks Delete".
- [ ] 3.11 **Task 3.11**: Create `Views/Productos/Delete.cshtml`: if `impact.Total == 0` → simple confirm button; else render `<dl>` with 3 counters + `<input name="confirmCode" />` type-to-confirm pattern. Create `wwwroot/js/productos-delete.js`: SweetAlert2 wiring, enables confirm only when input matches `codigo`. Acceptance: design §"Delete-impact flow".
- [ ] 3.12 **Task 3.12**: Verification — `dotnet build` + full suite green. Rollback: views revert (delete POST unchanged for `Total=0`); column drop deferred so revert is safe. Acceptance: design §"Per-PR checklist".
- [ ] 3.13 **Task 3.13**: Append **ADR #20** to `db/docs/DECISIONES.md`: "Catálogos cerrados: `tipos_producto` y `unidades_venta`" (Context, Decision, Consequences, When to revisit per design). Acceptance: spec "ADR documents closure", "no UI CRUD exists", "adding a type needs SQL migration".
- [ ] 3.14 **Task 3.14**: Final verification — `dotnet build src/ExtraGasMVC` clean, `dotnet test tests/ExtraGasMVC.Tests` all green, SonarQube `new_coverage ≥ 65%`. Rollback per slice boundary. Acceptance: AC1-AC8 all met.

## Implementation Order

Phase 1 first (no DB schema, pure code/tests/cache), Phase 2 (schema migration first within slice, then code), Phase 3 (schema+code+view+ADR last; most surface area). Each phase merges cleanly via stacked PRs. Tests travel with the behavior they verify (work-unit-commits rule).
