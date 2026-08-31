# Design: issue-145-productos-brechas

## Technical Approach

Four targeted fixes around the Productos module plus one append-only audit table. Each fix is independent and ships in one PR: (1) `RestoreAsync` reuses the `PedidoService.RestoreAsync` pattern (NOT the legacy `ClienteService` one from issue #115 — Producto retains its `Activo` flag); (2) `RecepcionService.LoadProductosByIdAsync` adds `&& p.Activo` to the existing WHERE; (3) `PedidoService.RegistrarCanjePedidoAsync` gets a pre-tx guard `ValidarProductosActivosAsync` that loads items + producto using `IgnoreQueryFilters()` and throws `InvalidOperationException("El producto {nombre} fue desactivado, refrescá el pedido.")`; (4) `UpdateAsync` snapshots `PrecioActual` before AutoMapper and inserts a `producto_precios_historico` row only when `precio_anterior != precio_nuevo && precio_anterior != 0`. Append-only table is idempotent via `CREATE TABLE IF NOT EXISTS`; FK to `usuarios(id)` for `changed_by` mirrors `productos.created_by`.

## Architecture Decisions

| # | Decision | Choice | Alternatives considered | Rationale |
|---|----------|--------|--------------------------|-----------|
| 1 | RestoreAsync reference | Mirror `PedidoService.RestoreAsync` (line 296), explicitly set `Activo = true` | Copy `ClienteService.RestoreAsync` (#115 era: no `Activo` flag on entity) | Producto keeps the `Activo` column by design (#114) — invariant `Activo=false ⇒ DeletedAt != null` must hold. Set both. |
| 2 | Price-history persistence | New entity + `IEntityTypeConfiguration` + DbSet on `ExtraGasDbContext` | `Owned` type on Producto; JSON column on Producto | Append-only semantics + audit-grade queries (`SELECT … ORDER BY changed_at DESC LIMIT 1`) are first-class. Owned types complicate EF tracking; JSON is opaque for reporting. |
| 3 | Price-change detection | Snapshot `entity.PrecioActual` BEFORE `_mapper.Map`; compare after | Use `EntityState.Modified` interception; `SaveChanges` interceptor | Service-level snapshot matches existing `ClienteService` `FechaAlta` snapshot (line 228). Interceptors add magic — the codebase prefers explicit Service-layer code. |
| 4 | `ChangedBy` semantics | `ulong?` FK to `usuarios(id)`; Service pulls from `usuarioId` parameter | Hard-require non-null; add `EmpleadoId` instead of `UsuarioId` | Existing services accept `ulong?` for audit (Create/Update/Delete). Consistency wins; tests cover the null path. |
| 5 | Pedido Activo validation placement | Private `ValidarProductosActivosAsync(pedidoId, ct)` called AFTER `AsegurarNoCanjeadoAsync`, BEFORE `LoadCatalogosParaCanjeAsync` (covers both canje and VENTA-only paths) | Validate inside `AplicarCanjeYConfirmarAsync` (inside tx); validate in `CreateAsync`/`UpdateAsync` | Same fast-fail pattern as `RecepcionService.ValidarItemsPreCommitAsync`. Race window microsecond-scale and admin-tier — matches existing precedent. Inside-tx would force refactor of `ConfirmarSinCanjeAsync`. |
| 6 | Authorize on Restore action | `[Authorize(Policy = "AdminOnly")]` on the action (overrides class-level `OperadorOrAdmin`) | New `AdminOnlyRestore` policy; manual `User.IsInRole("Admin")` check | Matches `AuditoriaLoginsController` class-level `AdminOnly` precedent and SonarQube-friendly. |
| 7 | Test strategy | EFC.InMemory for unit logic (Producto/Recepcion/Pedido Service) + Testcontainers.MySql for FK + transaction-rollback tests + controller tests instantiated directly | WebApplicationFactory (not used in repo); mocks only | Mirrors existing `ProductoServiceTests` (InMemory) + `PedidoCanjeIntegrationTests` (Testcontainers) + `ControllersActivoViewBagTests` (direct controller). No new infra. |
| 8 | Migration style | `CREATE TABLE IF NOT EXISTS producto_precios_historico` + FK constraints (idempotent native) | `PREPARE/EXECUTE` with `information_schema` (only needed for ADD/DROP) | This is a NEW table, no ADD/DROP. Native `IF NOT EXISTS` covers the re-run case; `schema_migrations` skip-by-checksum is the authoritative gate (AGENTS.md ADR #13). |
| 9 | Repository for history table | Direct DbContext use inside `ProductoService` (no `IProductoPrecioHistoricoRepository`) | Dedicated repository | Repo has no repositories (Services use DbContext directly — see `ProductoService`, `PedidoService`). Consistency. |

## Data Flow

    [Operator] -> [ProductosController.Edit POST] -> [ProductoService.UpdateAsync]
                                                              |
                                          +-------------------+------------------+
                                          | snapshot precioAnterior               |
                                          v                                       |
                                   _mapper.Map(dto, entity)                        |
                                          |                                       |
                                          v                                       |
                          precioAnterior != entity.PrecioActual                  |
                              && precioAnterior != 0 ?                             |
                                  YES -> _context.ProductoPreciosHistorico.Add() |
                                          |                                       |
                                          v                                       |
                                  SaveChangesAsync()  [atomic: product + history] |
                                          |
                                          v
                                   return ProductoDto

    [Operator] -> [RecepcionesController.Create POST] -> [RecepcionService.CreateAsync]
                                                              |
                                          LoadProductosByIdAsync(items) -- WHERE p.Activo=true
                                                              |
                                          ValidarItemsPreCommitAsync(...) -> throws if missing
                                                              |
                                                       BeginTransactionAsync()
                                                              |
                                                       ... (existing flow)

    [Operator] -> [PedidosController.RegistrarCanje POST] -> [PedidoService.RegistrarCanjePedidoAsync]
                                                              |
                                          LoadPedidoParaCanjeAsync
                                          AsegurarNoCanjeadoAsync
                                          ValidarProductosActivosAsync(pedidoId)  [NEW: throws if any item.producto.Activo=false || DeletedAt!=null]
                                          LoadCatalogosParaCanjeAsync
                                          ... (existing flow)

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `src/ExtraGasMVC/Data/Entities/ProductoPrecioHistorico.cs` | Create | POCO: `Id`, `ProductoId`, `PrecioAnterior`, `PrecioNuevo`, `MotivoCambioPrecio`, `ChangedBy`, `ChangedAt`. No `DeletedAt`, no `UpdatedAt` (append-only). |
| `src/ExtraGasMVC/Data/Configurations/ProductoPrecioHistoricoConfiguration.cs` | Create | `IEntityTypeConfiguration<ProductoPrecioHistorico>`. FK `producto_id` → `productos(id)` ON DELETE RESTRICT (no cascade on append-only). FK `changed_by` → `usuarios(id)` ON DELETE RESTRICT. Index `idx_pph_producto_changed (producto_id, changed_at DESC)`. No query filter (table is global, not soft-deleted). |
| `src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs` | Modify | Add `public DbSet<ProductoPrecioHistorico> ProductoPreciosHistorico => Set<ProductoPrecioHistorico>();` |
| `src/ExtraGasMVC/Services/Interfaces/IProductoService.cs` | Modify | Add `Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);` |
| `src/ExtraGasMVC/Services/Implementations/ProductoService.cs` | Modify | Implement `RestoreAsync` (`IgnoreQueryFilters` → set `DeletedAt = null`, `Activo = true`, `UpdatedAt = UtcNow`, `UpdatedBy = updatedBy`). In `UpdateAsync`: snapshot `precioAnterior`, after mapper add history row when `precioAnterior != entity.PrecioActual && precioAnterior != 0`. Pass `dto.MotivoCambioPrecio` to the new row. |
| `src/ExtraGasMVC/DTOs/ProductoDto.cs` | Modify | Add `MotivoCambioPrecio` to `UpdateProductoDto` with `[StringLength(255)]` validation. |
| `src/ExtraGasMVC/Mappings/MappingProfile.cs` | Modify | Update `ConfigureProducto` to add `.ForMember(d => d.MotivoCambioPrecio, o => o.Ignore())` on `UpdateProductoDto → Producto` (DTO field has no entity destination — kept on DTO, read directly by Service). |
| `src/ExtraGasMVC/Controllers/ProductosController.cs` | Modify | Add `[HttpPost][ValidateAntiForgeryToken] Restore(ulong id, ct)` with `[Authorize(Policy = "AdminOnly")]`. Mirrors `ClientesController.Restore` (line 167). |
| `src/ExtraGasMVC/Services/Implementations/RecepcionService.cs` | Modify | Line 109-112: add `&& p.Activo` to `LoadProductosByIdAsync` WHERE. Error message at line 148 ("no existe o está inactivo") becomes accurate. |
| `src/ExtraGasMVC/Services/Implementations/PedidoService.cs` | Modify | Add private `ValidarProductosActivosAsync(ulong pedidoId, ct)` after `AsegurarNoCanjeadoAsync` (line 596). Loads pedido_items joined with producto via `IgnoreQueryFilters()` on Producto; throws `InvalidOperationException("El producto {nombre} fue desactivado, refrescá el pedido.")`. |
| `src/ExtraGasMVC/Views/Productos/Index.cshtml` | Modify | In row actions: when `!p.Activo`, render a `<form asp-action="Restore" method="post">` with antiforgery + `js-confirm-form` (mirrors `Views/Usuarios/Index.cshtml` pattern). |
| `db/migrations/20260830_000001_producto_precios_historico.sql` | Create | Idempotent `CREATE TABLE IF NOT EXISTS producto_precios_historico (...)`. `schema_migrations` row auto-inserted by `install.sh`. |
| `db/docs/DECISIONES.md` | Modify | Add ADR: append-only price history rationale. Add ADR: `producto.Activo ⇒ visible en dropdowns de Pedidos/Recepciones` invariant. |
| `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` | Modify | Add: `RestoreAsync_ReactivatesSoftDeletedProducto`, `RestoreAsync_OnAlreadyActive_ReturnsFalse`, `UpdateAsync_PriceChange_CreatesHistoryRow`, `UpdateAsync_PriceUnchanged_NoHistoryRow`, `UpdateAsync_PriorZero_NoHistoryRow`, `UpdateAsync_PriceChange_StoresMotivoCambioPrecio`. |
| `tests/ExtraGasMVC.Tests/RecepcionServiceTests.cs` | Create | EFC.InMemory: `LoadProductosByIdAsync_ExcluyeProductosInactivos`, `ValidarItemsPreCommitAsync_RechazaProductoInactivo`. |
| `tests/ExtraGasMVC.Tests/PedidoServiceProductoActivoTests.cs` | Create | EFC.InMemory: `RegistrarCanjePedidoAsync_ProductoDesactivado_ThrowsInvalidOperation`, `ConfirmarSinCanjeAsync_ProductoDesactivado_ThrowsInvalidOperation`. (Unit-level logic; tx rollback semantics covered by existing `PedidoCanjeIntegrationTests`.) |

## Interfaces / Contracts

```csharp
// IProductoService.cs
Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);

// UpdateProductoDto.cs (additive)
[StringLength(255, ErrorMessage = "El motivo no puede superar {1} caracteres.")]
public string? MotivoCambioPrecio { get; set; }

// ProductoPrecioHistorico entity shape (informal)
public class ProductoPrecioHistorico {
  public ulong Id { get; set; }
  public ulong ProductoId { get; set; }
  public decimal PrecioAnterior { get; set; }
  public decimal PrecioNuevo { get; set; }
  public string? MotivoCambioPrecio { get; set; }   // VARCHAR(255) NULL
  public ulong? ChangedBy { get; set; }            // FK usuarios.id, NULL when system
  public DateTime ChangedAt { get; set; }          // default CURRENT_TIMESTAMP
}

// ProductosController.cs
[HttpPost]
[Authorize(Policy = "AdminOnly")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Restore(ulong id, CancellationToken ct = default);
```

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | `ProductoService.RestoreAsync`, price-history hook (5 scenarios) | EFC.InMemory + FluentAssertions (existing `ProductoServiceTests` pattern). |
| Unit | `RecepcionService` filter | New `RecepcionServiceTests.cs` with EFC.InMemory. Seed 3 active + 1 inactive + 1 soft-deleted; assert 3 in dictionary. |
| Unit | `PedidoService.RegistrarCanjePedidoAsync` Activo guard | New `PedidoServiceProductoActivoTests.cs` with EFC.InMemory. Cover VENTA-only path (`ConfirmarSinCanjeAsync`) and canje path. |
| Controller | `ProductosController.Restore` happy path + 403 | Direct controller instantiation (existing `ControllersActivoViewBagTests` pattern). Inject `IProductoService` mock; assert `RedirectToAction(nameof(Index))` on `true` and on `false`. 403 enforcement belongs to `[Authorize]` middleware — already covered by ASP.NET Core's own test suite; we trust it. |
| Integration | FK constraint on `changed_by` → `usuarios.id`, transaction rollback on race, `schema_migrations` row insertion | Testcontainers.MySql pattern (existing `PedidoCanjeIntegrationTests`/`ClienteIntegrationTests`). ONE fixture: create a producto, set null `changed_by`, attempt to insert history with invalid `changed_by` → expect FK violation 1452. Single test is enough — proves the FK is real. |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary changes.

## Migration / Rollout

- **DB**: run `db/migrations/20260830_000001_producto_precios_historico.sql` via `./db/scripts/install.sh` (idempotent — `schema_migrations` skip if checksum matches existing). No backfill required (issue proposal: out of scope).
- **App**: deploy service/controller changes after DB migration. The `producto_precios_historico` DbSet is only written by `UpdateAsync` — if the table is missing, the first price update throws `InvalidOperationException` from EF. Order matters in CI/CD pipeline.
- **Feature flag**: not needed. `Restore` action is new (additive); `&& p.Activo` filter is restrictive but matches what the spec demands; price-history hook is additive.
- **Rollback**: SQL `DROP TABLE IF EXISTS producto_precios_historico;` + revert code changes. `dotnet build` is sufficient for service-layer only fixes (filter in RecepcionService, validation in PedidoService).

## Open Questions

- [ ] Should `MotivoCambioPrecio` be required when price changes (DataAnnotations `[RequiredIfPriceChanged]`) or kept optional with UI nudge? Proposal says optional. Keeping optional for v1.
- [ ] Do we add `Restore` button also in `Views/Productos/Details.cshtml` for deactivated products visible via direct link? Out of scope for this change — Index-only per spec.
