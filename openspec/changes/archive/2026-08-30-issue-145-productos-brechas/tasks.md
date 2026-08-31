# Tasks: issue-145-productos-brechas

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~940 (prod ~320 / tests ~500 / migration ~45 / docs ~30) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2 → PR 3 → PR 4 |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | DB foundation: migration + entity + config + DbSet + mapping (~185) | PR 1 | `dotnet test --filter FullyQualifiedName~ProductoPrecioHistorico` | `./db/scripts/install.sh` on homelab; smoke `SELECT * FROM producto_precios_historico` | `DROP TABLE IF EXISTS producto_precios_historico` + revert new files |
| 2 | Producto Restore (service + controller + view) (~215) | PR 2 | `dotnet test --filter FullyQualifiedName~Restore` | `dotnet run` → `/Productos?soloActivos=false` → Restaurar | Revert Restore method/action/button only |
| 3 | Price-history hook in `UpdateAsync` + `MotivoCambioPrecio` (~265) | PR 3 | `dotnet test --filter FullyQualifiedName~UpdateAsync` | Edit a product price, verify history row | Remove hook + DTO field; table stays harmless |
| 4 | Integrity bugs (Recepcion filter, Pedido guard) + ADR (~275) | PR 4 | `dotnet test --filter "RecepcionService|ProductoActivo"` | Deactivate product, retry Recepcion/Pedido confirm | Revert two service edits + ADR |

Phase 1 = PR 1, Phase 2 = PR 2, Phase 3 = PR 3, Phase 4 = PR 4. Strict TDD: RED task before every GREEN task.

## Phase 1: DB Foundation (PR 1)

- [x] 1.1 RED: Testcontainers test `ProductoPrecioHistoricoSchemaTests` — apply `install.sh` migrations, assert table + `idx_pph_producto_changed` exist and re-run is a no-op.
- [x] 1.2 GREEN: Create `db/migrations/20260830_000001_producto_precios_historico.sql` — `CREATE TABLE IF NOT EXISTS` with FKs `producto_id→productos(id)`, `changed_by→usuarios(id)` RESTRICT, index `(producto_id, changed_at DESC)`.
- [x] 1.3 GREEN: Create `src/ExtraGasMVC/Data/Entities/ProductoPrecioHistorico.cs` (Id, ProductoId, PrecioAnterior, PrecioNuevo, MotivoCambioPrecio, ChangedBy, ChangedAt).
- [x] 1.4 GREEN: Create `Data/Configurations/ProductoPrecioHistoricoConfiguration.cs`; add DbSet `ProductoPreciosHistorico` in `Data/Context/ExtraGasDbContext.cs`.
- [x] 1.5 RED+GREEN: Testcontainers test — insert history row with invalid `changed_by` expects MySQL error 1452.

## Phase 2: Producto Restore (PR 2)

- [x] 2.1 RED: `ProductoServiceTests` — `RestoreAsync_ReactivatesSoftDeletedProducto` (DeletedAt null, Activo true, UpdatedBy set) and `RestoreAsync_OnAlreadyActive_ReturnsFalse`.
- [x] 2.2 GREEN: Add `RestoreAsync` to `Services/Interfaces/IProductoService.cs` + implement in `ProductoService.cs` via `IgnoreQueryFilters()`.
- [x] 2.3 RED: Controller test in `ControllersActivoViewBagTests` — `Restore` redirects to `Index` on true and on false (mocked service).
- [x] 2.4 GREEN: Add `[HttpPost][Authorize(Policy="AdminOnly")][ValidateAntiForgeryToken] Restore(ulong id, ct)` to `Controllers/ProductosController.cs`.
- [x] 2.5 GREEN: Render "Restaurar" post form (antiforgery + `js-confirm-form`) in `Views/Productos/Index.cshtml` only when `!p.Activo`.

## Phase 3: Price History Hook (PR 3)

- [x] 3.1 RED: `ProductoServiceTests` — `UpdateAsync_PriceChange_CreatesHistoryRow`, `UpdateAsync_PriceUnchanged_NoHistoryRow`, `UpdateAsync_PriorZero_NoHistoryRow`, `UpdateAsync_PriceChange_StoresMotivoCambioPrecio`.
- [x] 3.2 GREEN: Add `MotivoCambioPrecio` (`string?`, `[StringLength(255)]`) to `UpdateProductoDto` in `DTOs/ProductoDto.cs`; ignore member in `Mappings/MappingProfile.cs`.
- [x] 3.3 GREEN: In `ProductoService.UpdateAsync`, snapshot `PrecioActual` before `_mapper.Map`; add history row when `precioAnterior != PrecioActual && precioAnterior != 0`; single `SaveChangesAsync`.

## Phase 4: Integrity Bugs + ADR (PR 4)

- [x] 4.1 RED: New `tests/ExtraGasMVC.Tests/RecepcionServiceTests.cs` — `LoadProductosByIdAsync_ExcluyeProductosInactivos`, `ValidarItemsPreCommitAsync_RechazaProductoInactivo`.
- [x] 4.2 GREEN: Add `&& p.Activo` to `RecepcionService.LoadProductosByIdAsync` WHERE (line ~111).
- [x] 4.3 RED: New `tests/ExtraGasMVC.Tests/PedidoServiceProductoActivoTests.cs` — canje and VENTA-only paths throw `InvalidOperationException` naming the product for inactive and soft-deleted products; active products succeed.
- [x] 4.4 GREEN: Add private `ValidarProductosActivosAsync(pedidoId, ct)` in `PedidoService`, called after `AsegurarNoCanjeadoAsync` and before `LoadCatalogosParaCanjeAsync`.
- [x] 4.5 Add two ADRs to `db/docs/DECISIONES.md`: append-only price history; invariant `producto.Activo ⇒ visible en dropdowns de Pedidos/Recepciones`.
- [x] 4.6 Verify `dotnet build` warning-free and `dotnet test` coverage ≥ 65% on new code.
