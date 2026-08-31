```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:37fcfead8c82cd76251d4c7f332bcf1662e0efaa2bbd3a486dee43db03d5fc4d
verdict: pass
blockers: 0
critical_findings: 0
warnings: 1
suggestions: 1
requirements: 2/4
scenarios: 3/7
test_command: dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --nologo --no-build /p:CollectCoverage=true /p:CoverletOutput=TestResults/cov.xml /p:CoverletOutputFormat=cobertura --settings /tmp/cov.runsettings
test_exit_code: 0
test_output_hash: sha256:26009b54acdc91b4fab67156d8c9df2ef78367fc54d376833735ce7c724d30ce
build_command: dotnet build src/ExtraGasMVC --nologo
build_exit_code: 0
build_output_hash: sha256:107dfa76ea8a1d1ae09ddff2667a6a87f7507a22b844c93e9a440adb139e2176
```

## Verification Report

**Change**: issue-145-productos-brechas — Slice 1 (DB foundation)
**Version**: spec v1 (4 requirements, 7 scenarios)
**Mode**: Strict TDD
**Slice**: 1 of 4 (DB foundation only)
**Branch**: feat/issue-145-slice-1-db-foundation
**PR**: #148

### Completeness
| Metric | Value |
|--------|-------|
| Tasks total (Phase 1) | 5 |
| Tasks complete | 5 |
| Tasks incomplete | 0 |

All 5 Phase 1 tasks (`1.1`–`1.5`) are checked in `openspec/changes/issue-145-productos-brechas/tasks.md`. No unchecked tasks.

### Build & Tests Execution

**Build**: ✅ Passed
```text
$ dotnet build src/ExtraGasMVC --nologo
  Determining projects to restore...
  All projects are up-to-date for restore.
  ExtraGasMVC -> .../ExtraGasMVC/bin/Debug/net10.0/ExtraGasMVC.dll

Build succeeded.
    0 Error(s)
    3 Warning(s):
      NU1903 AutoMapper 12.0.1 vulnerability (×2, same warning repeated)
      CS8602 Views/Recepciones/Create.cshtml(62,37) nullable dereference (pre-existing, NOT in slice 1 scope)
```
**No warnings introduced by Slice 1 files.** The two NU1903 are package-level advisories unrelated to this slice; the CS8602 is in a Razor file outside Slice 1 scope (already noted in apply-progress as pre-existing).

**Tests**: ✅ 327/327 passed
```text
$ dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --nologo --no-build --settings /tmp/cov.runsettings
  Total tests: 327
       Passed: 327
       Failed: 0
       Skipped: 0
  Duration: 12 s

Slice 1 subset (9 tests):
  ✅ ProductoPrecioHistoricoEntityTests.DbSet_ProductoPreciosHistorico_ExpuestoEnExtraGasDbContext
  ✅ ProductoPrecioHistoricoEntityTests.AddAsync_PersisteFilaYReleeaConMismasPropiedades
  ✅ ProductoPrecioHistoricoEntityTests.AddAsync_MotivoCambioPrecioNullYChangedByNull_SonValidos
  ✅ ProductoPrecioHistoricoEntityTests.Entity_NoExponeDeletedAtNiUpdatedAt_AppendOnly
  ✅ ProductoPrecioHistoricoIntegrationTests.Migracion_CreaTabla_ConTodasLasColumnasEsperadas
  ✅ ProductoPrecioHistoricoIntegrationTests.Migracion_CreaIndiceIdxPphProductoChanged
  ✅ ProductoPrecioHistoricoIntegrationTests.Migracion_ReEjecutarEsNoOp_NoProduceError
  ✅ ProductoPrecioHistoricoIntegrationTests.InsertConChangedByInexistente_FallaConError1452
  ✅ ProductoPrecioHistoricoIntegrationTests.InsertConChangedByNull_PersisteCorrectamente
```

**Coverage**: ✅ Above threshold

Per-file coverage on Slice 1 production code (from cobertura.xml at `tests/ExtraGasMVC.Tests/TestResults/163ff51a-…/coverage.cobertura.xml`):

| File | Line % | Branch % | Rating |
|------|--------|----------|--------|
| `Data/Entities/ProductoPrecioHistorico.cs` | 66.66% | 100% | ⚠️ Acceptable (above gate) |
| `Data/Configurations/ProductoPrecioHistoricoConfiguration.cs` | 100% | 100% | ✅ Excellent |
| `Data/Context/ExtraGasDbContext.cs` (line 35 only — `get_ProductoPreciosHistorico`) | 100% (4/4 hits) | 100% | ✅ Excellent |

**SonarQube Quality Gate** (`new_coverage` ≥ 65%): ✅ **PASS** — `new_coverage = 67.4%`, `new_duplicated_lines_density = 0.0%`, `new_violations = 0`. Period `local-20260829-154501` corresponds to PR #148 analysis (2026-08-30T22:44:15Z).

### Spec Compliance Matrix

Slice 1 scope is **Req 1 (Schema)** + **Req 2 (Append-only)**. Req 3 (Hook) and Req 4 (Audit queries) are explicitly Slice 3 work per `design.md` and `tasks.md`.

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| REQ-1 Schema | Migración idempotente crea tabla si no existe | `ProductoPrecioHistoricoIntegrationTests.Migracion_CreaTabla_ConTodasLasColumnasEsperadas` (also: index + FK errno 1452 + null FK) | ✅ COMPLIANT |
| REQ-1 Schema | Re-correr migración es no-op | `ProductoPrecioHistoricoIntegrationTests.Migracion_ReEjecutarEsNoOp_NoProduceError` | ✅ COMPLIANT |
| REQ-2 Append-only | No existe operación de edición | `ProductoPrecioHistoricoEntityTests.Entity_NoExponeDeletedAtNiUpdatedAt_AppendOnly` (+ 2 round-trip tests prove no UPDATE/DELETE surface is exposed) | ✅ COMPLIANT |
| REQ-3 Hook | Cambio real registra fila | *(Slice 3 — `ProductoService.UpdateAsync` price-change hook, NOT in Slice 1)* | ➖ DEFERRED |
| REQ-3 Hook | Sin cambio real no registra fila | *(Slice 3)* | ➖ DEFERRED |
| REQ-4 Audit queries | Última fila por producto | *(Slice 3)* | ➖ DEFERRED |
| REQ-4 Audit queries | Histórico completo ordenado | *(Slice 3)* | ➖ DEFERRED |

**Slice 1 compliance summary**: 3/3 in-scope scenarios COMPLIANT. No UNTESTED, no FAILING, no PARTIAL.

### Correctness (Static Evidence)

**Homelab schema check** (`192.168.0.216:3306`, `extragas` database, MySQL 8.4.11):

`information_schema.COLUMNS` for `producto_precios_historico`:
```
id                   bigint unsigned NO  auto_increment
producto_id          bigint unsigned NO
precio_anterior      decimal(12,2)   NO
precio_nuevo         decimal(12,2)   NO
motivo_cambio_precio varchar(255)    YES (NULL allowed — operator may skip motive)
changed_by           bigint unsigned YES (NULL allowed — system changes)
changed_at           datetime        NO  DEFAULT CURRENT_TIMESTAMP
```
✅ Matches spec exactly (7 columns, correct types/nullability/defaults).

`information_schema.STATISTICS`:
```
idx_pph_producto_changed | producto_id (A, seq 1)
idx_pph_producto_changed | changed_at  (D, seq 2)  ← DESC collation, mandatory per spec
fk_pph_changed_by        | changed_by  (A)
PRIMARY                  | id          (A)
```
✅ Index `(producto_id, changed_at DESC)` exists with correct DESC ordering on `changed_at`. MySQL stores `Collation = D` (= DESC) for the second column.

`information_schema.REFERENTIAL_CONSTRAINTS`:
```
fk_pph_producto    producto_id → productos(id)    ON DELETE RESTRICT
fk_pph_changed_by  changed_by  → usuarios(id)     ON DELETE RESTRICT
```
✅ Both FKs RESTRICT (spec §Schema, design §Architecture Decisions #2). Audit trail is preserved even if product/user is deleted.

`schema_migrations` row:
```
filename = 20260830_000001_producto_precios_historico.sql
checksum = fab0fc68f261e186aa394af28d9f9de66a701e684428d384f3008380d79c6834
applied_at = 2026-08-30 22:41:41 UTC
```
✅ File SHA256 (`shasum -a 256`) = `fab0fc68f261e186aa394af28d9f9de66a701e684428d384f3008380d79c6834` — **exact match** with the DB row. No drift.

**Entity-to-schema mapping** (`ProductoPrecioHistorico.cs` ↔ `ProductoPrecioHistoricoConfiguration.cs`):

| C# property | SQL column | Type/nullability | Configured |
|-------------|-----------|------------------|------------|
| `ulong Id` | `id` | `bigint unsigned NOT NULL AUTO_INCREMENT` | ✅ `HasKey`, `HasColumnName("id")` |
| `ulong ProductoId` | `producto_id` | `bigint unsigned NOT NULL` | ✅ `HasColumnName("producto_id")` |
| `decimal PrecioAnterior` | `precio_anterior` | `decimal(12,2) NOT NULL` | ✅ `HasColumnName`, `HasPrecision(12,2)` |
| `decimal PrecioNuevo` | `precio_nuevo` | `decimal(12,2) NOT NULL` | ✅ `HasColumnName`, `HasPrecision(12,2)` |
| `string? MotivoCambioPrecio` | `motivo_cambio_precio` | `varchar(255) NULL` | ✅ `HasColumnName`, `HasMaxLength(255)` |
| `ulong? ChangedBy` | `changed_by` | `bigint unsigned NULL` | ✅ `HasColumnName("changed_by")` |
| `DateTime ChangedAt` | `changed_at` | `datetime NOT NULL DEFAULT CURRENT_TIMESTAMP` | ✅ `HasColumnName`, `HasColumnType("datetime")`, `ValueGeneratedOnAdd`, `HasDefaultValueSql("CURRENT_TIMESTAMP")` |
| `Producto? Producto` (nav) | — | FK via `producto_id` | ✅ `HasOne(...).WithMany().HasForeignKey(p => p.ProductoId).OnDelete(Restrict).HasConstraintName("fk_pph_producto")` |
| `Usuario? ChangedByUsuario` (nav) | — | FK via `changed_by` | ✅ `HasOne(...).WithMany().HasForeignKey(p => p.ChangedBy).OnDelete(Restrict).HasConstraintName("fk_pph_changed_by")` |
| Index | `idx_pph_producto_changed (producto_id, changed_at DESC)` | — | ⚠️ `HasIndex` declared but EF does NOT emit `DESC` (Pomelo limitation). Migration SQL creates the index with DESC. See WARNING 1 below. |

**DbContext integration** (`Data/Context/ExtraGasDbContext.cs`):
- Line 35: `public DbSet<ProductoPrecioHistorico> ProductoPreciosHistorico => Set<ProductoPrecioHistorico>();` ✅ Declared in the "Productos y catálogo" group (next to `Productos`).
- Line 66: `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExtraGasDbContext).Assembly);` ✅ Picks up `ProductoPrecioHistoricoConfiguration` automatically. No `OnModelCreating` changes needed.

### Coherence (Design)

| Design Decision | Followed? | Notes |
|-----------------|-----------|-------|
| #1 RestoreAsync reference (Slice 2 — N/A for Slice 1) | ➖ | Out of scope for Slice 1 |
| #2 Price-history persistence: new entity + config + DbSet | ✅ Yes | Matches exactly: `ProductoPrecioHistorico`, `ProductoPrecioHistoricoConfiguration`, `DbSet` declared |
| #3 Price-change detection: snapshot `PrecioActual` BEFORE `_mapper.Map` (Slice 3 — N/A for Slice 1) | ➖ | Out of scope |
| #4 `ChangedBy` semantics: `ulong?` FK to `usuarios(id)` | ✅ Yes | `ChangedBy { get; set; }` is `ulong?`; FK + null-path tested in `InsertConChangedByNull_PersisteCorrectamente` |
| #5 Pedido Activo validation (Slice 4 — N/A) | ➖ | Out of scope |
| #6 Authorize on Restore (Slice 2 — N/A) | ➖ | Out of scope |
| #7 Test strategy: EFC.InMemory + Testcontainers.MySql | ✅ Yes | 4 InMemory unit tests + 5 Testcontainers integration tests, mirroring existing patterns |
| #8 Migration style: `CREATE TABLE IF NOT EXISTS` (idempotent native) | ✅ Yes | Migration uses native IF NOT EXISTS (no PREPARE/EXECUTE needed for CREATE). `schema_migrations` skip-by-checksum is the authoritative gate. |
| #9 Repository for history table: direct DbContext use | ✅ Yes | No repository layer introduced |
| Append-only enforcement: no `HasQueryFilter`, no `DeletedAt`/`UpdatedAt` POCO properties | ✅ Yes | Verified by `Entity_NoExponeDeletedAtNiUpdatedAt_AppendOnly` (reflects on type) |
| Index `idx_pph_producto_changed (producto_id, changed_at DESC)` | ✅ Partial | Index exists in DB with DESC, but `IEntityTypeConfiguration.HasIndex` cannot express DESC (Pomelo/MySQL provider limitation). DESC ordering lives in the SQL migration; EF-side configuration is `(producto_id, changed_at)`. Documented in WARNING 1. |

### TDD Compliance (Strict TDD mode)

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | ✅ | `TDD Cycle Evidence` table present in apply-progress (mem #2023) |
| All tasks have tests | ✅ | 5/5 Phase 1 tasks have covering test files (1.1 → integration, 1.3 → unit, 1.4 → unit-via-1.3, 1.5 → integration) |
| RED confirmed (tests exist) | ✅ | 9/9 test files exist in `tests/ExtraGasMVC.Tests/` |
| GREEN confirmed (tests pass) | ✅ | 9/9 pass on re-execution. Full suite 327/327 — no regression |
| Triangulation adequate | ✅ | Task 1.1: 4 scenarios (happy + error 1452 + null + idempotente). Task 1.3: 4 cases (DbSet + round-trip + null + shape). Task 1.5: 1 scenario (single FK errno 1452 — appropriate, single constraint) |
| Safety Net for modified files | ✅ N/A | All files were NEW (entity, config, tests, migration). DbContext is the only modified file (1 line added) — `ApplyConfigurationsFromAssembly` already exercised by every prior test, so safety net is implicitly the existing 318 tests |

**TDD Compliance**: 6/6 checks passed.

### Test Layer Distribution

| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit | 4 | `ProductoPrecioHistoricoEntityTests.cs` | EFC.InMemory + FluentAssertions |
| Integration | 5 | `ProductoPrecioHistoricoIntegrationTests.cs` + `ProductoPrecioHistoricoMySqlFixture` | Testcontainers.MySql 4.8.1 + MySqlConnector + xUnit |
| E2E | 0 | — | N/A for slice 1 (no controller/UI) |
| **Total** | **9** | **2** | |

Layer mix is appropriate for DB foundation: schema + FK constraints require a real MySQL (InMemory would mask the FK behavior). Entity POCO + DbContext registration is well-suited to InMemory.

### Assertion Quality Audit

| File | Line | Assertion | Issue | Severity |
|------|------|-----------|-------|----------|
| `ProductoPrecioHistoricoEntityTests.cs` | 39 | `prop.Should().NotBeNull("...")` + `.PropertyType.Should().Be<DbSet<...>>()` | Type-only-ish BUT combined with NotBeNull on a real DbContext instance — behavioral | ✅ OK |
| `ProductoPrecioHistoricoEntityTests.cs` | 68–72 | `leida.PrecioAnterior.Should().Be(1000m)` × 5 properties | Behavioral assertions on real round-trip | ✅ OK |
| `ProductoPrecioHistoricoEntityTests.cs` | 97–99 | `filas.Should().HaveCount(1)` + `filas[0].MotivoCambioPrecio.Should().BeNull()` × 2 | Count + value — non-empty companion present | ✅ OK |
| `ProductoPrecioHistoricoEntityTests.cs` | 109–114 | `tipo.GetProperty("DeletedAt").Should().BeNull(...)` × 3 | Meta-test (anti-regression) — valid use of type-only assertion | ✅ OK |
| `ProductoPrecioHistoricoIntegrationTests.cs` | 47–58 | `columnas.Should().BeEquivalentTo(new[] {...}, WithStrictOrdering())` | Strict ordering + exact match — strong behavioral | ✅ OK |
| `ProductoPrecioHistoricoIntegrationTests.cs` | 78 | `existe.Should().BeTrue("...")` | Value + message — behavioral on information_schema | ✅ OK |
| `ProductoPrecioHistoricoIntegrationTests.cs` | 99 | `act.Should().NotThrowAsync("...")` | Behavioral on idempotence | ✅ OK |
| `ProductoPrecioHistoricoIntegrationTests.cs` | 140–143 | `ex.Which.Number.Should().Be(1452, "...")` | Error-number assertion on real MySqlException — strong | ✅ OK |
| `ProductoPrecioHistoricoIntegrationTests.cs` | 175 | `count.Should().Be(1, "...")` | Count + behavioral on persisted row | ✅ OK |

**Assertion quality**: ✅ All assertions verify real behavior. **No trivial assertions found** (no tautologies, no ghost loops, no mock-heavy tests).

### Quality Metrics

**Build warnings**: 3 (NU1903 ×2 AutoMapper advisory + CS8602 pre-existing nullable dereference in `Views/Recepciones/Create.cshtml:62`). None in Slice 1 files.

**SonarQube Quality Gate**: ✅ **OK** — `new_coverage=67.4%` (≥ 65%), `new_duplicated_lines_density=0.0%` (≤ 3%), `new_violations=0` (≤ 0). CAYC status compliant.

**SonarQube issues (leak period)**: 20 code smells (INFO=1, MINOR=11, MAJOR=8). **Zero issues in Slice 1 files** (`ProductoPrecioHistorico*`, migration). All issues are pre-existing in other modules.

### Issues Found

**CRITICAL**: None.

**WARNING** (1):

1. **[INFO] EF `IEntityTypeConfiguration.HasIndex` cannot express `DESC`** — the index is declared via `builder.HasIndex(p => new { p.ProductoId, p.ChangedAt }).HasDatabaseName("idx_pph_producto_changed")` in `ProductoPrecioHistoricoConfiguration.cs` (line 59–60), but Pomelo/MySQL provider does NOT translate `ChangedAt` direction to `DESC` in the emitted migration. The actual `DESC` ordering lives in the raw SQL migration (`db/migrations/20260830_000001_producto_precios_historico.sql` line 38: `KEY idx_pph_producto_changed (producto_id, changed_at DESC)`). The homelab confirms the DESC is in place. **Impact**: if a future developer runs `Add-Migration` and reapplies via EF, the index could be recreated WITHOUT DESC. **Mitigation in scope**: the migration file is the source of truth; EF is only used for the runtime model. Document this in a comment or add an EF-side `migration:HasIndex` annotation that Pomelo 9.0.0 supports? **Decision deferred to next apply if/when `Add-Migration` is introduced** (current repo uses raw SQL migrations — no EF migration generator risk today).

**SUGGESTION** (1):

1. **Entity POCO coverage 66.66%** — the `Id` auto-property getter (line 16) and navigation properties `Producto` (line 24) and `ChangedByUsuario` (line 25) are not covered by any test. Not blocking — passes the 65% gate by 1.66 pp. The navigation properties are not exercised because InMemory cannot validate FK navigation semantics (only Testcontainers can). Optional improvement: add a small Testcontainers test that creates the row, then queries with `.Include(p => p.Producto)` to assert the nav property resolves. Defer to Slice 3 if navigation usage emerges.

### Verdict

**PASS WITH WARNINGS**

All Slice 1 deliverables match the design and spec exactly. Build/tests/coverage/SonarQube gate are green. The single WARNING is a known Pomelo/MySQL provider limitation with a documented mitigation in place; it does not block archive of Slice 1.

**Next recommended**: `sdd-apply` for **Slice 2 (ProductoService.RestoreAsync + Controller action + View button)** on branch `feat/issue-145-slice-2-producto-restore`, once PR #148 is merged.
