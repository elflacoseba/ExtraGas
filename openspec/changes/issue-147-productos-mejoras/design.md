# Design: issue-147-productos-mejoras

## Overview

Three chained PRs land the eight Producto enhancements. Slice 1 is pure code (DTO + Service + view + tests + cache) — no DB schema; Slice 2 introduces `audit_log` infrastructure; Slice 3 builds the `unidades_venta` catalog and delete-impact UI. All code follows patterns already adopted by `ClienteService`/`UsuarioService`; corrections from exploration findings #188-202 (`StringNormalizer.TrimAndUpper` missing), #43-45 (no `deleted_at` on dependency tables), and #110-113 (no audit fields in `Cliente/Details`/`Edit`) are baked into the design.

## Architecture Decisions

### Slice 1 — Bajo impacto (~350 LOC): items 6, 4, 5, 1

**Grouping rationale:** all changes are localized to `ProductoService`/DTOs/views/tests, no schema. Lands the audit-field pattern before Slice 2 wires the table behind it, so the views can reference stable fields.

#### Decision: Codigo normalization at the Service boundary (item 6)

| Aspect | Detail |
|--------|--------|
| **Choice** | `StringNormalizer.TrimAndUpper(dto.Codigo)` applied in `CreateAsync`, `UpdateAsync`, and the `GetPagedAsync` search input. `GetByCodigoAsync` also normalizes the lookup argument. |
| **Why at Service** | Matches the existing pattern (`ProductoService.cs:222-251` reads entity, then `AutoMapper.Map`). DataAnnotations already validate `StringLength(30)`. The DTO carries the raw user input; the entity column stores the canonical form. |
| **Rejected** | `IValueConverter` on the EF property — would couple normalization to reads (search for `"gas"` would not match `"GAS"` because the LINQ expression can't translate `ToUpperInvariant` consistently across Pomelo MySQL provider for our collation). Application-layer normalization is the only place we can guarantee the canonical form reaches the index `uq_productos_codigo`. |
| **New API** | `StringNormalizer.TrimAndUpper(string?) → string`. Returns `string.Empty` for null/whitespace to match the spec's `TrimAndUpper(null) → ""` scenario (existing methods return `null`; we diverge deliberately because `Producto.Codigo` is `NOT NULL`). |

#### Decision: Audit fields visible in Details/Edit (item 4)

| Aspect | Detail |
|--------|--------|
| **Choice** | Add `CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName` to `ProductoDto`. `MappingProfile.ConfigureProducto` uses explicit `.ForMember` for the two user-name fields (mirroring `UsuarioService.AplicarAudit` pattern at `UsuarioService.cs:554-621`). `ProductoService.GetByIdAsync` preloads usernames via a private helper. |
| **Why explicit ForMember** | Exploration finding #40 + issue #118: relying on convention lets the Mapper silently overwrite audit fields if someone adds `CreatedBy` to `ProductoDto` later. Explicit `.ForMember` documents the contract. |
| **View rendering** | `Details.cshtml`: AdminLTE card with `<dl class="row">` (mirrors existing structure at lines 19-35). `Edit.cshtml`: read-only `<dl>` block below the form (not bound to submit). |
| **Rejected** | Reading `CreatedBy`/`UpdatedBy` directly from the entity into the DTO — usernames are FKs to `usuarios`, not denormalized on the entity. Need a separate lookup. |

#### Decision: Cache `tipos_producto` with `IMemoryCache.GetOrCreateAsync` (item 1)

| Aspect | Detail |
|--------|--------|
| **Choice** | Inject `IMemoryCache` into `ProductoService`. Wrap `GetTiposProductoAsync` body with `_cache.GetOrCreateAsync("tipos_producto", entry => { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); ... })`. Cache key is the constant string, not per-call — the list never changes during a session. |
| **Why GetOrCreateAsync** | Single-line pattern from skill `dotnet-backend-patterns`. No manual lock/evict needed for seed-only data; the 1-hour TTL is acceptable staleness per spec. |
| **Test strategy** | `GetTiposProductoAsync_ReturnsCachedOnSecondCall` counts `_context.TiposProducto` invocations with a `Mock<ExtraGasDbContext>` or by inspecting `IMemoryCache` directly. The InMemory cache backend keeps the call count deterministic. |
| **Out of scope** | Forward-looking invalidation hook (spec scenario "future TipoProducto CRUD writes evict") — documented in code comment, not implemented. Closed catalog per item 8. |

#### Decision: Test coverage for ProductoService (item 5)

| Aspect | Detail |
|--------|--------|
| **Choice** | Add 7 test methods to `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` (existing file, same `NewService` helper). Add `StringNormalizerTests.TrimAndUpper_*` to the existing file. |
| **Test names** | Match repo convention `Metodo_ResultadoEsperado_Condicion`: `GetByCodigoAsync_NotFound_ReturnsNull`, `GetByCodigoAsync_SoftDeleted_ReturnsNull`, `GetByTipoAsync_UnknownTipo_ReturnsEmpty`, `GetActivosAsync_MixedStatus_ReturnsOnlyActive`, `UpdateAsync_UnknownId_ThrowsKeyNotFoundException`, `DeleteAsync_UnknownId_ReturnsFalse`, `CreateAsync_NullUser_Succeeds`. |
| **StringNormalizer tests** | `TrimAndUpper_NullReturnsEmpty`, `TrimAndUpper_TrimsAndUppercases`, `TrimAndUpper_AlreadyUppercase_StaysSame`, `TrimAndUpper_WithSurroundingSpaces_Trims`, `TrimAndUpper_MixedCase_Uppercases`. |

---

### Slice 2 — Infraestructura (~330 LOC): item 3

**Grouping rationale:** schema migration first, then `IAuditLogger` infra, then integration into `ProductoService.UpdateAsync`. Isolated because the table is reused by future slices (other modules).

#### Decision: `audit_log` table design

| Column | Type | Notes |
|--------|------|-------|
| `id` | `BIGINT UNSIGNED AUTO_INCREMENT PK` | matches house style |
| `entidad` | `VARCHAR(50) NOT NULL` | e.g. `'Producto'` — value, not FK |
| `registro_id` | `BIGINT UNSIGNED NOT NULL` | ID of the changed row |
| `campo` | `VARCHAR(100) NOT NULL` | e.g. `'PrecioActual'` |
| `valor_anterior` | `TEXT NULL` | string-serialized old value |
| `valor_nuevo` | `TEXT NULL` | string-serialized new value |
| `changed_by` | `BIGINT UNSIGNED NULL` | FK to `usuarios.id`, NULL for system-initiated changes |
| `changed_at` | `DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP` | append-only, no updated_at/deleted_at |

**Indexes:** `KEY idx_audit_entidad_registro (entidad, registro_id, changed_at)` — covers "all changes for entity X id Y" queries and "last change for X". No FK to `usuarios` because audit log must survive user deletion (FK `ON DELETE RESTRICT` would block legitimate user removal).

#### Decision: `IAuditLogger` interface shape

```csharp
public interface IAuditLogger
{
    Task LogChangeAsync(
        string entidad,
        long registroId,
        string campo,
        string? valorAnterior,
        string? valorNuevo,
        long? changedBy,
        CancellationToken ct = default);
}
```

| Aspect | Detail |
|--------|--------|
| **Choice** | One method per change. `AuditLogger` (Scoped) appends to `DbSet<AuditLogEntry>`. Calls `SaveChangesAsync` per field (or batch — see below). |
| **Why per-method** | Same shape as `IAuditoriaLoginService.RecordAsync` (which already lives in the codebase). Future readers don't need a new mental model. |
| **Transaction model** | `ProductoService.UpdateAsync` calls `_audit.LogChangeAsync(...)` BEFORE its own `SaveChangesAsync`. Since `IAuditLogger` is Scoped and shares the same `ExtraGasDbContext`, the changes accumulate in the change tracker and commit together — atomic with the product update. |
| **Failure handling** | Same pattern as `AuditoriaLoginService.RecordAsync`: try/catch in `AuditLogger.LogChangeAsync` that logs and swallows. Audit failure should never block a write. |
| **DI registration** | `Program.cs` line ~73: `builder.Services.AddScoped<IAuditLogger, AuditLogger>();` |

#### Decision: Hook into `ProductoService.UpdateAsync`

`UpdateAsync` already snapshots the entity before `AutoMapper.Map` (`ProductoService.cs:246`). Reuse that snapshot — extract a `DetectarCambiosAuditables` helper that returns a list of `(fieldName, oldValueString, newValueString)` tuples for fields: `Codigo`, `Nombre`, `Descripcion`, `TipoProductoId`, `CapacidadKg`, `UnidadVenta`, `PrecioActual`, `ManejaGarrafaIndividual`, `Activo`. Each non-null diff → `_audit.LogChangeAsync("Producto", entity.Id, field, old, new, usuarioId, ct)`.

| Aspect | Detail |
|--------|--------|
| **Why reuse DetectarCambiosProducto** | Existing helper at `ProductoService.cs:472-494` returns a `List<string>` of human-readable diffs. Add a parallel `DetectarCambiosAuditables` that returns structured tuples for the audit logger. Same source data, two consumers. |
| **Excluded fields** | `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`, `DeletedAt`, `RowVersion` — these are infrastructure-managed; changes here are not "user actions". |
| **No-op update** | Empty list → zero calls to `_audit.LogChangeAsync` (verified by spec scenario "no-op update emits zero rows"). |

#### Decision: Migration `20260901_000001_create_audit_log.sql`

Follows existing migration header style (issue link, idempotency notes, schema columns documented). `CREATE TABLE IF NOT EXISTS audit_log (...)` + `KEY idx_audit_entidad_registro (...)`. Idempotent via table-level `IF NOT EXISTS` (no `information_schema` guard needed for `CREATE TABLE`).

---

### Slice 3 — Mixed (~380 LOC): items 2, 7, 8

**Grouping rationale:** heaviest slice. `unidades_venta` needs schema + FK + seed ordering; delete-impact UI needs controller route + SweetAlert2 view; ADR is docs only.

#### Decision: `unidades_venta` catalog (item 7)

**Schema** in `20260901_000002_create_unidades_venta_and_fk.sql`:

1. `CREATE TABLE IF NOT EXISTS unidades_venta (id, codigo UNIQUE, nombre, activo, deleted_at, audit cols)` — mirrors `tipos_producto` shape (`Data/Entities/TipoProducto.cs`).
2. `INSERT IGNORE INTO unidades_venta (codigo, nombre) VALUES ('UNIDAD','Unidad'), ('GARRAFA','Garrafa'), ('BOLSA','Bolsa'), ('KG','Kilogramo')`.
3. `ADD COLUMN unidad_venta_id BIGINT UNSIGNED NULL` (guard via `information_schema` + `PREPARE/EXECUTE`).
4. Backfill: `UPDATE productos p JOIN unidades_venta u ON u.codigo = p.unidad_venta SET p.unidad_venta_id = u.id` — preserves existing data.
5. `ADD CONSTRAINT fk_productos_unidad_venta FOREIGN KEY (unidad_venta_id) REFERENCES unidades_venta(id) ON DELETE RESTRICT` (guard via `information_schema`).
6. **DROP COLUMN `unidad_venta`** deferred to a separate cleanup migration (after app has shipped and confirms no reads depend on the column).

| Aspect | Detail |
|--------|--------|
| **Choice** | Add `UnidadVentaId` (FK) alongside the existing `UnidadVenta` (string) during the transition. Drop the string column in a follow-up migration. |
| **Why two-step** | Same expand-contract pattern as ADR #12 (`pedido_items.unique_hash`). Eliminates the "deploy app before migration" risk: app reads `UnidadVentaId` if non-null, falls back to `UnidadVenta` if null (handled in the entity). |
| **Why `ON DELETE RESTRICT`** | Mirrors `fk_productos_tipo` (`ProductoConfiguration.cs:97`). A `unidad_venta` referenced by a product cannot be deleted — would orphan the lookup reference. |
| **DTO changes** | `ProductoDto.UnidadVenta` (string) → `ProductoDto.UnidadVentaId` (long?) + `ProductoDto.UnidadVentaNombre` (read-only display). `CreateProductoDto.UnidadVenta` → `UnidadVentaId` (long, with `[Range(1, ulong.MaxValue)]`). `MappingProfile` adds `.ForMember(d => d.UnidadVentaNombre, o => o.MapFrom(s => s.UnidadVenta != null ? s.UnidadVenta.Nombre : null))`. |
| **Service** | `ProductoService.GetUnidadesVentaAsync(CancellationToken)` returns `IEnumerable<UnidadVentaDto>` ordered by `Nombre`, cached analogously to `GetTiposProductoAsync` with key `"unidades_venta"` and 1h TTL. |
| **View** | `Create.cshtml`/`Edit.cshtml`: replace `<input asp-for="UnidadVenta" />` with `<select asp-for="UnidadVentaId" asp-items="@(new SelectList(ViewBag.UnidadesVenta, "Id", "Nombre"))"><option value="">(seleccione)</option></select>`. |
| **Controller** | `LoadViewBagsAsync` adds `ViewBag.UnidadesVenta = await _productoService.GetUnidadesVentaAsync(ct);` alongside `ViewBag.TiposProducto`. |

#### Decision: Delete-impact UI (item 2)

**Critical correction (exploration #43-45):** `pedido_items`, `recepcion_items`, `movimientos_garrafa` do NOT have a `deleted_at` column. The count query runs WITHOUT any soft-delete filter.

| Aspect | Detail |
|--------|--------|
| **New service method** | `Task<ProductoDeleteImpactDto> CountDependenciesAsync(ulong id, ct)` returns `(int PedidoItems, int RecepcionItems, int MovimientosGarrafa)`. Three `AsNoTracking().CountAsync()` calls (no `deleted_at` filter on any). |
| **DTO** | `public record ProductoDeleteImpactDto(int PedidoItems, int RecepcionItems, int MovimientosGarrafa) { int Total => PedidoItems + RecepcionItems + MovimientosGarrafa; }` |
| **Controller** | Add `[HttpGet] [Authorize(Policy="AdminOnly")] Delete(ulong id, ct)` that calls `GetByIdAsync` + `CountDependenciesAsync`, passes both to the view. Existing `[HttpPost] Delete` stays (it accepts the confirmed POST). |
| **View** | `Views/Productos/Delete.cshtml` (new). If `impact.Total == 0`: simple confirm button. Else: render a `<dl>` with the three counters, plus a `<input type="text" name="confirmCode" />` that the user must fill with the exact `Codigo` (case-sensitive) to enable the SweetAlert2 confirm. The confirm JS posts to `Delete`. |
| **Wire-up** | SweetAlert2 already loaded via `package.json` (`sweetalert2 ^11.26.25`); a small inline script block in `Delete.cshtml` (or new `wwwroot/js/productos-delete.js`) handles the type-to-confirm pattern. |
| **No DB schema change** | Counts are read-only against existing tables. |

#### Decision: ADR for closed catalogs (item 8)

Append **ADR #20** to `db/docs/DECISIONES.md`. Title: "Catálogos cerrados: `tipos_producto` y `unidades_venta`". Body sections: Context (no UI CRUD by design, types are stable business decisions), Decision (NO CRUD UI; adding a value = SQL migration under `db/migrations/`), Consequences (operator cannot create types in production; admin escape hatch if business need emerges = issue/PR with explicit justification), When to revisit (GAS_INDUSTRIAL, LENA_PALLET, etc. → new issue with `[AdminOnly]` policy proposal).

---

## Data Flow

### audit_log flow (Slice 2)

```
Operator submits Edit form
    │
    ▼
ProductosController.Edit (POST)
    │
    ▼
ProductoService.UpdateAsync(dto, usuarioId, ct)
    │
    │ 1. Load entity → snapshot for diff
    │ 2. Apply AutoMapper.Map
    │ 3. For each changed field:
    │      await _audit.LogChangeAsync("Producto", id, field, oldStr, newStr, usuarioId, ct)
    │         │ (adds AuditLogEntry to change tracker)
    │ 4. await _context.SaveChangesAsync(ct)
    │      → ONE transaction commits both Producto and audit_log rows
    ▼
Success / ValidationException
```

### Delete-impact flow (Slice 3)

```
Operator clicks "Desactivar" on Index.cshtml row
    │
    ▼
GET /Productos/Delete/{id}
    │
    ▼
ProductosController.Delete (GET) [AdminOnly]
    │
    ▼
ProductoService.CountDependenciesAsync(id)
    │  ├─ _context.PedidoItems.CountAsync(pi => pi.ProductoId == id, ct)
    │  ├─ _context.RecepcionItems.CountAsync(ri => ri.ProductoId == id, ct)
    │  └─ _context.MovimientosGarrafa.CountAsync(mg => mg.ProductoId == id, ct)
    │  (NO deleted_at filter on any of the three)
    ▼
View renders Delete.cshtml with impact counters
    │
    │ if Total == 0: simple confirm
    │ if Total > 0: SweetAlert2 modal, type-to-confirm codigo
    ▼
POST /Productos/Delete/{id}
    │
    ▼
ProductoService.DeleteAsync (existing, unchanged)
```

---

## File Changes

### Slice 1 (Bajo impacto)

| File | Action | Description |
|------|--------|-------------|
| `src/ExtraGasMVC/Extensions/StringNormalizer.cs` | Modify | Add `TrimAndUpper(string?) → string` |
| `src/ExtraGasMVC/Services/Implementations/ProductoService.cs` | Modify | Inject `IMemoryCache`; wrap `GetTiposProductoAsync`; apply `TrimAndUpper` in `CreateAsync`, `UpdateAsync`, `GetByCodigoAsync`, `GetPagedAsync` |
| `src/ExtraGasMVC/DTOs/ProductoDto.cs` | Modify | Add `CreatedAt`, `UpdatedAt`, `CreatedByUserName`, `UpdatedByUserName` |
| `src/ExtraGasMVC/Mappings/MappingProfile.cs` | Modify | Add explicit `.ForMember` for the 4 audit fields in `ConfigureProducto` |
| `src/ExtraGasMVC/Views/Productos/Details.cshtml` | Modify | Append audit card with `<dl>` block |
| `src/ExtraGasMVC/Views/Productos/Edit.cshtml` | Modify | Add read-only audit info row |
| `tests/ExtraGasMVC.Tests/StringNormalizerTests.cs` | Modify | Add 4 `TrimAndUpper` tests |
| `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` | Modify | Add 7 missing-branch tests |
| `tests/ExtraGasMVC.Tests/MappingProfileProductoTests.cs` (new) | Create | Verify audit field mapping doesn't regress |

### Slice 2 (Infraestructura)

| File | Action | Description |
|------|--------|-------------|
| `db/migrations/20260901_000001_create_audit_log.sql` | Create | Schema + index |
| `src/ExtraGasMVC/Data/Entities/AuditLogEntry.cs` | Create | POCO |
| `src/ExtraGasMVC/Data/Configurations/AuditLogEntryConfiguration.cs` | Create | Column names + index |
| `src/ExtraGasMVC/Services/Interfaces/IAuditLogger.cs` | Create | Interface |
| `src/ExtraGasMVC/Services/Implementations/AuditLogger.cs` | Create | Scoped implementation |
| `src/ExtraGasMVC/Services/Implementations/ProductoService.cs` | Modify | Inject `IAuditLogger`; call per changed field in `UpdateAsync`; add `DetectarCambiosAuditables` |
| `src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs` | Modify | Add `DbSet<AuditLogEntry> AuditLog` |
| `src/ExtraGasMVC/Program.cs` | Modify | Register `IAuditLogger` as Scoped (line ~73) |
| `tests/ExtraGasMVC.Tests/ProductoServiceAuditLogTests.cs` (new) | Create | 3-4 tests verifying emitted rows |
| `tests/ExtraGasMVC.Tests/ProductoAuditLogIntegrationTests.cs` (new) | Create | Testcontainers integration test |

### Slice 3 (Mixed)

| File | Action | Description |
|------|--------|-------------|
| `db/migrations/20260901_000002_create_unidades_venta_and_fk.sql` | Create | Table + seed + ADD COLUMN + backfill + FK |
| `src/ExtraGasMVC/Data/Entities/UnidadVenta.cs` | Create | POCO |
| `src/ExtraGasMVC/Data/Configurations/UnidadVentaConfiguration.cs` | Create | Column names + unique index on codigo |
| `src/ExtraGasMVC/Data/Entities/Producto.cs` | Modify | Add `UnidadVentaId` (long?) + navigation |
| `src/ExtraGasMVC/Data/Configurations/ProductoConfiguration.cs` | Modify | FK to `unidades_venta` |
| `src/ExtraGasMVC/DTOs/UnidadVentaDto.cs` | Create | New |
| `src/ExtraGasMVC/DTOs/ProductoDto.cs` | Modify | Replace `UnidadVenta` string with `UnidadVentaId` + `UnidadVentaNombre` |
| `src/ExtraGasMVC/DTOs/CreateProductoDto.cs` | Modify | `UnidadVentaId` (long, [Range]) |
| `src/ExtraGasMVC/DTOs/UpdateProductoDto.cs` | Modify | Same |
| `src/ExtraGasMVC/Services/Interfaces/IProductoService.cs` | Modify | Add `GetUnidadesVentaAsync`, `CountDependenciesAsync` |
| `src/ExtraGasMVC/Services/Implementations/ProductoService.cs` | Modify | Implement + cache; replace `UnidadVenta` refs with `UnidadVentaId`; add `CountDependenciesAsync` |
| `src/ExtraGasMVC/Controllers/ProductosController.cs` | Modify | Add `[HttpGet] Delete(id)` action; `LoadViewBagsAsync` adds `UnidadesVenta` |
| `src/ExtraGasMVC/Views/Productos/Delete.cshtml` | Create | New — confirms with counters |
| `src/ExtraGasMVC/Views/Productos/Create.cshtml` | Modify | `<select>` for `UnidadVentaId` |
| `src/ExtraGasMVC/Views/Productos/Edit.cshtml` | Modify | Same |
| `wwwroot/js/productos-delete.js` (new) or inline in `Delete.cshtml` | Create | SweetAlert2 type-to-confirm |
| `db/docs/DECISIONES.md` | Modify | Append ADR #20 |
| `tests/ExtraGasMVC.Tests/ProductoServiceCountDependenciesTests.cs` (new) | Create | 2-3 tests for count correctness |
| `tests/ExtraGasMVC.Tests/ProductosControllerDeleteTests.cs` (new) | Create | Verify GET passes impact to view |
| `tests/ExtraGasMVC.Tests/UnidadVentaMigrationTests.cs` (new) | Create | Integration test verifying seed-backfill works |

---

## Interfaces / Contracts

### `IAuditLogger` (new)

```csharp
public interface IAuditLogger
{
    Task LogChangeAsync(
        string entidad,        // e.g. "Producto"
        long registroId,        // entity.Id
        string campo,           // e.g. "PrecioActual"
        string? valorAnterior,  // string-serialized old value
        string? valorNuevo,     // string-serialized new value
        long? changedBy,        // usuarioId or null for system
        CancellationToken ct = default);
}
```

### `ProductoDeleteImpactDto` (new)

```csharp
public record ProductoDeleteImpactDto(
    int PedidoItems,
    int RecepcionItems,
    int MovimientosGarrafa)
{
    public int Total => PedidoItems + RecepcionItems + MovimientosGarrafa;
    public bool HasDependencies => Total > 0;
}
```

### `UnidadVentaDto` (new)

```csharp
public class UnidadVentaDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
}
```

### `ProductoDto` deltas (Slice 1 + 3)

```csharp
// Slice 1: 4 audit fields
+ public DateTime CreatedAt { get; set; }
+ public DateTime UpdatedAt { get; set; }
+ public string? CreatedByUserName { get; set; }
+ public string? UpdatedByUserName { get; set; }

// Slice 3: replace string with FK + display
- public string UnidadVenta { get; set; } = "UNIDAD";
+ public ulong? UnidadVentaId { get; set; }
+ public string? UnidadVentaNombre { get; set; }
```

### `IProductoService` deltas

```csharp
// Slice 3 additions
+ Task<IEnumerable<UnidadVentaDto>> GetUnidadesVentaAsync(CancellationToken ct = default);
+ Task<ProductoDeleteImpactDto> CountDependenciesAsync(ulong id, CancellationToken ct = default);
```

---

## Testing Strategy

| Layer | What to Test | Approach |
|-------|--------------|----------|
| **Unit — StringNormalizer** | `TrimAndUpper` 5 cases | xUnit + FluentAssertions; pure function tests |
| **Unit — ProductoService** | 7 missing-branch tests + cache verification | DbContext InMemory (`UseInMemoryDatabase`); `Mock<IMemoryCache>` for cache tests |
| **Unit — MappingProfile** | Audit field mapping (`CreatedByUserName` resolves from explicit path, not from `CreatedBy` FK) | Direct `MapperConfiguration.AssertConfigurationIsValid()` + assertion of mapped DTO |
| **Unit — ProductosController** | Delete GET passes impact to view | `Mock<IProductoService>`; verify `View(impact, producto)` |
| **Integration — audit_log** | After `UpdateAsync`, `audit_log` has expected rows | Testcontainers.MySql real DB; full SaveChangesAsync commit verified |
| **Integration — unidades_venta migration** | Existing products with `unidad_venta='GARRAFA'` get FK id after migration runs | Apply migration via Testcontainers, query state |
| **Integration — count dependencies** | Count values match pre-seeded fixture rows | Seed 3 fixture rows across the 3 tables, assert counts |
| **E2E — none** | Scope is library-level | Razor views validated by build + manual smoke test in browser |

---

## Migration / Rollout

**Phased deployment with stacked PRs (chained-to-main):**

```
PR #A: Slice 1 (Items 6, 4, 5, 1)         → main
PR #B: Slice 2 (Item 3)                    → main (stacked on #A)
PR #C: Slice 3 (Items 2, 7, 8)             → main (stacked on #B)
```

**Per-PR checklist:**
1. `dotnet build src/ExtraGasMVC` clean.
2. `dotnet test tests/ExtraGasMVC.Tests` — all green, including new tests.
3. Slice 2 only: run `./db/scripts/install.sh` locally with the migrator user — confirms schema_migrations registers the new file with checksum.
4. Slice 3 only: after migration runs, smoke-test that existing products still resolve their `UnidadVentaId`.
5. SonarQube `new_coverage` ≥ 65% (custom gate per AGENTS.md).

**No data migration backout.** Slice 3's `unidad_venta` column drop is deferred to a separate cleanup migration; the live column coexistence is safe because the app reads `UnidadVentaId` first, falls back to `UnidadVenta` string.

---

## Open Questions (for sdd-tasks)

- [ ] Slice 3 item 7: drop `unidad_venta` (string) column in the same PR, or defer to a separate cleanup migration? **Recommendation: defer.** Coexistence is safe and gives operators a rollback path.
- [ ] Slice 3 item 2 UX: inline `<script>` in `Delete.cshtml` vs. new `wwwroot/js/productos-delete.js`? **Recommendation: separate JS file**, easier to test and CSP-friendly.
- [ ] Slice 2: batch `LogChangeAsync` calls or one-per-call? One-per-call is simpler; batching needs a "list" overload. **Recommendation: one-per-call for first cut.**
- [ ] Item 1 cache TTL: 1h matches spec but other modules may want different TTLs. Centralize? **Out of scope** — per-spec is fine for now.
