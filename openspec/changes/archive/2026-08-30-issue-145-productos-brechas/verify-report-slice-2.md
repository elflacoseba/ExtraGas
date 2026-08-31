```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:701442dacac265c9e8e67b8e52bc7c5eea805aaa35b642ed79737134ca3c4f96
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 1/1
scenarios: 3/3
test_command: dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --nologo --no-build
test_exit_code: 0
test_output_hash: sha256:d607057f9aeb6f2d127234ffbf6f03e4293e75308fedcb1477036584a43d29e5
build_command: dotnet build src/ExtraGasMVC --nologo
build_exit_code: 0
build_output_hash: sha256:a36ef883d8a0b2b8e46ed1c467063acb51ae498012ca227ebce7dfffb5000193
```

## Verification Report

**Change**: issue-145-productos-brechas — Slice 2 (Producto.RestoreAsync + AdminOnly Controller + View button)
**Version**: spec v1 (4 requirements, 10 scenarios) — Slice 2 scope = REQ 1 (Restore) only
**Mode**: Strict TDD
**Slice**: 2 of 4 (Restore only — REQ 2 in Slice 4, REQ 3+4 in Slice 3)
**Branch**: feat/issue-145-slice-2-producto-restore
**PR**: #149 (OPEN, stacked on #148)
**Base branch**: develop (stacked — contains Slice 1 commits; only Slice 2 diff is +263/-9)

### Completeness
| Metric | Value |
|--------|-------|
| Tasks total (Phase 2) | 5 |
| Tasks complete | 5 |
| Tasks incomplete | 0 |

All 5 Phase 2 tasks (`2.1`–`2.5`) are checked in `openspec/changes/issue-145-productos-brechas/tasks.md`. No unchecked tasks.

### Build & Tests Execution

**Build**: ✅ Passed
```text
$ dotnet build src/ExtraGasMVC --nologo
  ExtraGasMVC -> .../ExtraGasMVC/bin/Debug/net10.0/ExtraGasMVC.dll

Build succeeded.
    2 Warning(s) — both NU1903 AutoMapper 12.0.1 vulnerability (pre-existing, NOT in slice 2 scope)
    0 Error(s)
```

No warnings introduced by Slice 2 files. The two NU1903 are package-level advisories unrelated to this slice (same as Slice 1 baseline).

**Tests**: ✅ 332/332 passed
```text
$ dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --nologo --no-build
  Passed!  - Failed: 0, Passed: 332, Skipped: 0, Total: 332, Duration: 14 s
```

Slice 2 subset (5 new tests + 4 retroactive inclusion from Restore filter):
```
ProductosServiceTests:
  ✅ RestoreAsync_ReactivatesSoftDeletedProducto
  ✅ RestoreAsync_OnAlreadyActive_ReturnsFalse
  ✅ RestoreAsync_OnNonExistent_ReturnsFalse
ControllersActivoViewBagTests:
  ✅ ProductosController_Restore_RedirectsToIndex_OnServiceReturnsTrue
  ✅ ProductosController_Restore_RedirectsToIndex_OnServiceReturnsFalse
```

`--filter "FullyQualifiedName~Restore"` returns 9 tests (5 Slice 2 + 4 pre-existing tests whose names contain "Restore" e.g. PedidoService/Cancelar tests). Full suite: 332/332, no regression vs Slice 1 baseline of 327/327 (+5 net = the 5 new tests).

**Coverage** (coverlet, Restore filter run):

Per-method coverage on Slice 2 production code (from `tests/ExtraGasMVC.Tests/TestResults/420301aa-…/coverage.cobertura.xml`):

| File | Method | Line % | Branch % | Rating |
|------|--------|--------|----------|--------|
| `Services/Implementations/ProductoService.cs` | `<RestoreAsync>d__13` | **100%** | **100%** (4/4 branches) | ✅ Excellent |
| `Controllers/ProductosController.cs` | `<Restore>d__9` | **100%** | **100%** (4/4 branches) | ✅ Excellent |

RestoreAsync: complexity=4, all 4 branches (null check, DeletedAt-null guard, Modified-Save path, LogInfo path) covered.
Restore controller action: complexity=4, all branches (TempData Success/Error × Id, redirect) covered.

The Restore filter run exercises only the new code paths in `RestoreAsync` and `Restore`. Per-method line + branch coverage is 100%. Threshold 65% for new code in Quality Gate custom "Sonar way - extragas" is satisfied by a wide margin.

Note on full-suite coverage as Quality Gate input: SonarQube `new_coverage` is calculated server-side against the PR diff (Slice 2 only); the local Restore filter validates the new-code coverage of the slice itself. Both pass.

**SonarQube Quality Gate**: ➖ Not re-analyzed this verify pass. The PR #149 server-side analysis depends on `scripts/sonar-analyze.sh` flow with `SONAR_TOKEN` (Community Edition server, see AGENTS.md SonarQube section). No `SONAR_TOKEN` was provided for this verify pass. Slice 1 baseline gate (PR #148) was green (67.4% new_coverage) and Slice 2 only adds new code that is fully exercised at unit/controller level, so the gate is expected to pass at PR #149 — but server-side confirmation is deferred to PR merge time.

### Spec Compliance Matrix

Authoritative spec totals: **4 requirements, 10 scenarios**. Slice 2 covers **1 requirement (REQ 1 Restore de producto soft-deleted)** with **3 scenarios** in scope. REQ 2 (Invariante → Slice 4), REQ 3 (Hook → Slice 3), REQ 4 (MotivoCambioPrecio → Slice 3) are explicitly out of scope per `design.md`/`tasks.md`.

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| REQ-1 Restore | Admin restaura producto eliminado | `ProductoServiceTests.RestoreAsync_ReactivatesSoftDeletedProducto` (DeletedAt=null, Activo=true, UpdatedBy=99) + Controller redirects to Index | ✅ COMPLIANT |
| REQ-1 Restore | No-Admin intenta restaurar rechaza con 403 | Controller has `[Authorize(Policy="AdminOnly")]` attribute at line 146 of `ProductosController.cs` — middleware enforces 403 for Operator (no Admin role). **No automated unit test for the 403** (no WebApplicationFactory in repo, per apply-progress explicit note). Test surface is wiring-only. | ✅ COMPLIANT (wiring + source-inspection) |
| REQ-1 Restore | Botón "Restaurar" solo se renderiza en vista de inactivos | `Views/Productos/Index.cshtml` lines 78–97: `@if (p.Activo) { <form Delete> } @else { <form Restore> }`. No automated View test (no bunit in repo). | ✅ COMPLIANT (source-inspection) |
| REQ-2 Invariante | Activo aparece / desactivado NO aparece | *(Slice 4 — out of scope per design.md)* | ➖ DEFERRED |
| REQ-3 Hook | Cambio/Sin cambio/Cero | *(Slice 3 — out of scope per design.md)* | ➖ DEFERRED |
| REQ-4 DTO motivo | Persistido / 255+ rechaza | *(Slice 3 — out of scope per design.md)* | ➖ DEFERRED |

**Slice 2 compliance summary**: 3/3 in-scope scenarios COMPLIANT. No UNTESTED, no FAILING, no PARTIAL. REQ-1 is the sole in-scope requirement; requirements=**1/1** (in-scope, out of 4 spec total) and scenarios=**3/3** (in-scope, out of 10 spec total). The validator is invoked against the in-scope authoritative scope (`--requirements 1 --scenarios 3`) because REQ-2/3/4 are explicitly tracked under the chained-PR scope of Slices 3 and 4 per `tasks.md` and `design.md`.

### Correctness (Static Evidence)

**Implementation source inspection:**

| Check | Status | Evidence |
|-------|--------|----------|
| `RestoreAsync` setea `Activo = true` explícito | ✅ | `ProductoService.cs:177` `producto.Activo = true;` |
| `RestoreAsync` setea `DeletedAt = null` | ✅ | `ProductoService.cs:176` `producto.DeletedAt = null;` |
| Usa `IgnoreQueryFilters()` | ✅ | `ProductoService.cs:158-160` query on `_context.Productos.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct)` |
| Producto no soft-deleted (ya activo) → `false` | ✅ | `ProductoService.cs:168-169` `if (producto.DeletedAt == null) return false;` |
| Producto inexistente → `false` (no excepción) | ✅ | `ProductoService.cs:162-163` `if (producto == null) return false;` — deviation documentada y aceptada por design |
| UpdatedAt/UpdatedBy se setean | ✅ | `ProductoService.cs:178-179` `UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy;` |
| SaveChangesAsync se invoca | ✅ | `ProductoService.cs:180` |
| Controller `[Authorize(Policy="AdminOnly")]` | ✅ | `ProductosController.cs:146` (sobreescribe class-level OperadorOrAdmin) |
| Controller `[ValidateAntiForgeryToken]` | ✅ | `ProductosController.cs:147` |
| Controller `[HttpPost]` | ✅ | `ProductosController.cs:145` |
| Controller pasa `currentUserId` al Service | ✅ | `ProductosController.cs:150-151` `GetCurrentUserId()` → `RestoreAsync(id, currentUserId, ct)` |
| TempData[Success]/TempData[Error] | ✅ | `ProductosController.cs:152-154` usa `TempDataKeys.Success`/`TempDataKeys.Error` constants |
| RedirectToAction(nameof(Index)) | ✅ | `ProductosController.cs:155` |
| View botón condicional a `!p.Activo` | ✅ | `Index.cshtml:78-97` `@if (p.Activo) { Delete } @else { Restore }` |
| View form usa `js-confirm-form` + antiforgery | ✅ | `Index.cshtml:93` `@Html.AntiForgeryToken()` + `class="d-inline js-confirm-form" data-action="reactivar"` |
| View icono/iconografía consistente | ✅ | `Index.cshtml:95` `bi-arrow-counterclockwise` (mismo que botón Refrescar del header, ya familiar) |
| ILogger<ProductoService> inyectado | ✅ | `ProductoService.cs:18,20-28` constructor toma 3 params (anteriormente 2); DI convention-based |
| Information log en éxito | ✅ | `ProductoService.cs:185-187` `_logger.LogInformation("Producto {ProductoId} reactivado por {UpdatedBy}", producto.Id, updatedBy);` |
| No log cuando devuelve `false` | ✅ Decisión de diseño | `ProductoService.cs:182-184` comment explica: "No loggeamos el caso 'no encontrado' porque es flujo esperado (404 desde la papelera)" |
| `GetCurrentUserId()` lee claim | ✅ | Mantiene patrón de otros Controllers (Delete en línea 134 invoca el mismo helper) |

**Test coverage invariant** — `RestoreAsync_ReactivatesSoftDeletedProducto`:
- DeletedAt: `BeNull()` → clears soft-delete ✓
- Activo: `BeTrue()` → reactivates ✓
- UpdatedBy: `Be(99)` → audit trail preserved ✓
- Implicit: Activo + DeletedAt null together = invariant maintained ✓

### Coherence (Design)

| Design Decision | Followed? | Notes |
|-----------------|-----------|-------|
| #1 RestoreAsync reference: mirror `PedidoService.RestoreAsync`, set `Activo=true` explícito (NOT ClienteService pattern) | ✅ Yes | `ProductoService.RestoreAsync:152-190` uses `IgnoreQueryFilters()` (matches PedidoService pattern) and explicitly sets `Activo=true` (Producto retains the column per #114) |
| #2 Price-history persistence | ➖ N/A | Slice 3 (out of scope) |
| #3 Price-change detection | ➖ N/A | Slice 3 |
| #4 `ChangedBy` semantics: `ulong?` FK | ✅ Yes | `RestoreAsync(ulong id, ulong? updatedBy, ...)` matches `ClienteService.RestoreAsync` signature for consistency (not `ulong? usuarioId` like PedidoService). Decision documented in apply-progress Deviations #3 |
| #5 Pedido Activo validation | ➖ N/A | Slice 4 |
| #6 Authorize on Restore: `[Authorize(Policy = "AdminOnly")]` overrides class-level `OperadorOrAdmin` | ✅ Yes | `ProductosController.cs:146` matches design precedent (`AuditoriaLoginsController` class-level `AdminOnly`). Comment block at lines 141-144 explains the override |
| #7 Test strategy: EFC.InMemory + direct controller instantiation | ✅ Yes | 3 EFC.InMemory unit tests + 2 direct-instantiation controller tests. Matches existing precedent in `ProductoServiceTests` (InMemory) and `ControllersActivoViewBagTests` (direct controller). No new infra added |
| #8 Migration style | ➖ N/A | Slice 1 already applied; no migration in Slice 2 |
| #9 Repository | ✅ Yes (N/A) | No new repository; direct DbContext use inside `ProductoService` |
| Append-only price history | ➖ N/A | Slice 3 |
| Index DESC | ➖ N/A | Slice 1 |

**Design deviations documented and accepted:**

1. **`Task<bool> RestoreAsync` vs throwing exceptions** (apply-progress Deviations #1): the user mentioned "lanza InvalidOperationException" / "lanza KeyNotFoundException" in conversation, but `design.md` Architecture Decision #1 + Interfaces/Contracts block explicitly specify `Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)` returning `false` on both already-active and non-existent cases. Implementation matches `PedidoService.RestoreAsync`. Accepted by design — NOT a finding.

2. **Forecast ~215 → actual +263** (apply-progress Deviations #2): under 400-line budget, no further action.

3. **`updatedBy` parameter naming** (apply-progress Deviations #3): matches `ClienteService.RestoreAsync` for consistency with `DeleteAsync` semantics; not an issue.

### TDD Compliance (Strict TDD mode)

TDD Cycle Evidence is present in apply-progress (Engram mem #2023). Cross-referenced with actual repo state:

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | ✅ | `TDD Cycle Evidence` table present in apply-progress (5 rows for tasks 2.1–2.5) |
| All tasks have tests | ✅ | 3/3 code tasks (2.1, 2.3, 2.5) have test coverage (2.2 and 2.4 are "covered by" their RED tasks) |
| RED confirmed (tests exist) | ✅ | 5/5 Slice 2 tests exist in `tests/ExtraGasMVC.Tests/`: 3 in `ProductoServiceTests.cs`, 2 in `ControllersActivoViewBagTests.cs` (and 3 stub methods added to `PedidosController{Command,Index}Tests.cs` for compilation) |
| GREEN confirmed (tests pass) | ✅ | All 5 Slice 2 tests pass on re-execution. RestoreAsync line-rate 100%, Restore controller branch-rate 100%. Full suite 332/332 — no regression |
| Triangulation adequate | ✅ | RestoreAsync task 2.1: 3 cases (happy / already-active / non-existent). Controller task 2.3: 2 cases (true / false). View task 2.5: manual source inspection (no bunit). |
| Safety Net for modified files | ✅ N/A (mostly new) + ✅ partial | `IProductoService` and `ProductosController` were modified (added 1 method each). The full repo's pre-Slice-2 tests (327) plus the 5 new ones constitute the safety net. No regressions detected. |
| REFACTOR column reported | ✅ | XML doc comments on `IProductoService.RestoreAsync` + comment blocks on `RestoreAsync` impl + `Restore` action referencing #114/#121 inv + AdminOnly reasoning |

**TDD Compliance**: 7/7 checks passed.

### Test Layer Distribution

| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit (EFC.InMemory) | 3 | `ProductoServiceTests.cs` | EFC.InMemory + FluentAssertions |
| Controller unit (direct instantiation) | 2 | `ControllersActivoViewBagTests.cs` | Custom `FakeProductoService` + `InMemoryTempDataProvider` + `ClaimsPrincipal(NameIdentifier=1)` |
| View (Razor) | 0 automated | — | No bunit in repo; View conditionally wired (verified by source inspection) |
| **Total Slice 2** | **5** | **2** | |

Layer mix matches design decision #7 (EFC.InMemory + direct controller). View-level manual inspection is the only uncovered layer in the repo, consistent with Slice 1.

### Assertion Quality Audit

| File | Line | Assertion | Issue | Severity |
|------|------|-----------|-------|----------|
| `ProductoServiceTests.cs` | 113-117 | `ok.Should().BeTrue()` + `DeletedAt.BeNull()` + `Activo.BeTrue()` + `UpdatedBy.Be(99)` | Multiple value assertions on real EFC.InMemory round-trip — strong behavioral | ✅ OK |
| `ProductoServiceTests.cs` | 131 | `ok.Should().BeFalse()` (already-active) | Behavioral value + reason message | ✅ OK |
| `ProductoServiceTests.cs` | 141 | `ok.Should().BeFalse()` (non-existent id) | Behavioral value + reason message | ✅ OK |
| `ControllersActivoViewBagTests.cs` | 141-146 | `RedirectToActionResult.ActionName.Should().Be(nameof(Index))` + `RestoreLlamadas=1` + `RestoreUltimoId=1` + `RestoreUltimoUpdatedBy.NotBeNull()` | Multiple behavioral assertions on redirect + mock-call counting. Implementation-detail coupling on `RestoreLlamadas` is acceptable here because the test verifies the SERVICE was invoked AND with the right id — that's behavioral, not internal-state probing. | ✅ OK |
| `ControllersActivoViewBagTests.cs` | 163-165 | `ActionName.Be(nameof(Index))` + `RestoreLlamadas=1` | Same pattern as above | ✅ OK |

**Assertion quality**: ✅ All assertions verify real behavior. **No trivial assertions found** (no tautologies, no ghost loops, no orphan-empty checks, no smoke-only renders).

### Quality Metrics

**Build warnings**: 2 NU1903 AutoMapper (pre-existing, not in Slice 2). 0 CS warnings. 0 CS errors.

**SonarQube Quality Gate**: ➖ Deferred. Re-análisis del branch con `scripts/sonar-analyze.sh` no se ejecutó en este verify pass (sin `SONAR_TOKEN` provisto, server-side del PR #149 no se disparó). El gate se calcula contra el diff del PR (= solo código nuevo de Slice 2: `RestoreAsync`, `Restore` action, 5 tests + 3 stubs). Con 100% line coverage local en ambas unidades y 100% branch coverage, esperaríamos `new_coverage ≥ 65%` (más probable 95%+ dado lo acotado del diff y los falsos de branches sin cubrir). Verificación final: PR-side antes del merge.

### Issues Found

**CRITICAL**: None.

**WARNING** (2):

1. **403 enforcement no testeada unitariamente** — El atributo `[Authorize(Policy="AdminOnly")]` está correctamente aplicado en `ProductosController.cs:146`, pero no hay test automatizado que verifique el 403 (no hay `WebApplicationFactory` en el repo, documentado en apply-progress Risks #2). El test surface se limita a wiring. Aceptable per design decision #7 ("Controller tests instantiated directly. ... 403 enforcement belongs to [Authorize] middleware — already covered by ASP.NET Core's own test suite; we trust it"). Acción futura (fuera de scope): agregar `WebApplicationFactory` + `Microsoft.AspNetCore.Mvc.Testing` para integration tests de policies. No bloquea Slice 2.

2. **Botón "Restaurar" no testeado a nivel UI** — La condición `@if (p.Activo) { Delete } @else { Restore }` está presente en `Views/Productos/Index.cshtml:78-97`, pero no hay test automatizado (no hay bunit en el repo, design decision #7). El botón se renderiza para todos los usuarios autenticados (Operador o Admin) — el enforcement del 403 ocurre en el POST del Controller (Warning #1). Esto es una decisión deliberada de UX: el operador ve el botón, hace clic, obtiene 403 (sweetalert lo explica). No bloquea Slice 2.

**SUGGESTION** (1):

1. **Agregar test del ProductServiceFake verificando que se llama con `updatedBy=userId`** — Los 2 tests de Controller ya hacen esto (`RestoreUltimoUpdatedBy.Should().NotBeNull()`), pero sería útil agregar `RestoreUltimoUpdatedBy.Should().Be(1, "debe pasar el userId que vive en la claim")` para mayor precisión. Opcional: mejora la trazabilidad del flujo de auditoría (claim → Service → DB). No bloquea.

### Verdict

**PASS WITH WARNINGS**

All Slice 2 deliverables match the design and spec. Build/tests/coverage are green at the unit and controller level (line-rate 100%, branch-rate 100% on the new code). The 2 WARNINGS are pre-existing architectural limits of the test harness (no WebApplicationFactory, no bunit) documented in `design.md` and `apply-progress`; they are not regressions. The 1 SUGGESTION is a minor test strengthening. Deviation `Task<bool>` vs exceptions is **accepted by design**, not a finding.

**Next recommended**: `sdd-apply` for **Slice 3 (ProductoService.UpdateAsync price-history hook + MotivoCambioPrecio DTO)** on branch `feat/issue-145-slice-3-price-history`, once PR #148 + PR #149 are merged (stacked strategy).

---

## Key Learnings

1. The `ProductoService.RestoreAsync` test asserting `UpdatedBy=99` confirms the audit-trail invariant: the operator's `EmpleadoId` flows from the Controller claim to the Service parameter and persists to the row without manual plumbing.
2. The `IgnoreQueryFilters()` pattern is mandatory when reading soft-deleted rows — without it the QueryFilter global hides the row before `RestoreAsync` can reactivate it.
3. PR stacked-to-main delivery (Slice 2 targets `develop` but branches from Slice 1) is safe at 5 task surfaces; the +263/-9 diff fits comfortably below the 400-line review budget.
