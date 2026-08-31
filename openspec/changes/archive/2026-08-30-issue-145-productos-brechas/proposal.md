# Proposal: issue-145-productos-brechas

## Intent

Four data-integrity gaps in the Productos module are exposing production data to corruption: (1) soft-delete is permanent without DBA intervention, (2) `RecepcionService` accepts deactivated products, (3) `PedidoService` confirms orders against products that may have been deactivated since the draft was opened, and (4) price changes overwrite with no audit trail. All four are fixed in this change.

## Scope

### In Scope
- `RestoreAsync(ulong id, ulong? updatedBy, ct)` in `IProductoService` + `ProductoService` + `[HttpPost] Restore(id, ct)` in `ProductosController` with `[Authorize(Policy = "AdminOnly")]`
- "Restaurar" button in `Views/Productos/Index.cshtml` when `soloActivos=false`
- `RecepcionService.LoadProductosByIdAsync`: add `&& p.Activo` filter on line 111
- `PedidoService.CreateAsync`/`UpdateAsync`: validate `Activo && DeletedAt == null` on all `item.ProductoId` at confirmation time; throw `InvalidOperationException` with product name
- `producto_precios_historico` table via idempotent SQL migration
- `ProductoService.UpdateAsync` price-change hook writing to `producto_precios_historico`
- `UpdateProductoDto.MotivoCambioPrecio` (string?, max 255, required only when price changes)
- ADR update in `db/docs/DECISIONES.md`: price history and "producto.Activo ⇒ visible in Pedidos/Recepciones dropdowns" invariant
- Tests for all four fixes

### Out of Scope
- `GarrafaService` impact from deactivated products (deferred — separate analysis needed per AGENTS.md)
- Historical price data backfill for existing products
- `ProductoEditRules` changes (Restore flips `Activo`, it is not Edit — PreservarFlagsNoEditables does not apply)

## Capabilities

### New Capabilities
- `producto-precio-historico`: audit log of price changes per product with actor, timestamp, and optional motive. Persisted via hook in `ProductoService.UpdateAsync`.

### Modified Capabilities
- `productos`: add `RestoreAsync` action and the "producto.Activo ⇒ dropdown-eligible" invariant enforced in RecepcionService and at Pedido confirmation time.

## Approach

1. **DB migration** (`db/migrations/YYYYMMDD_XXX_producto_precios_historico.sql`): idempotent CREATE TABLE following the `information_schema`+`PREPARE`/`EXECUTE` pattern. `db/docs/DECISIONES.md` updated with the two new invariants.

2. **`IProductoService.RestoreAsync`** — mirror `ClienteService.RestoreAsync` (line 290) but explicitly set `Activo = true` (Producto retains the `Activo` flag unlike Cliente after issue #115). `IgnoreQueryFilters()` to find deleted records.

3. **`RecepcionService.LoadProductosByIdAsync`** — add `&& p.Activo` to line 111 WHERE clause.

4. **`PedidoService.CreateAsync`/`UpdateAsync`** — before transition to CONFIRMADO, validate every `PedidoItem.ProductoId` is `Activo && DeletedAt == null`. Fail fast with `InvalidOperationException("El producto {nombre} fue desactivado, refrescá el pedido.")` — no partial writes.

5. **`ProductoService.UpdateAsync`** — snapshot `PrecioActual` before `_mapper.Map`, detect change, insert `producto_precios_historico` row if different. Wire `MotivoCambioPrecio` from DTO.

6. **UI**: add "Restaurar" button in `Views/Productos/Index.cshtml` inside the `soloActivos=false` block.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Services/Interfaces/IProductoService.cs` | Modified | Add `RestoreAsync` signature |
| `Services/Implementations/ProductoService.cs` | Modified | Implement `RestoreAsync`, price-change hook |
| `Services/Implementations/RecepcionService.cs` | Modified | Add `&& p.Activo` filter (line 111) |
| `Services/Implementations/PedidoService.cs` | Modified | Validate `Activo` at confirm time |
| `Controllers/ProductosController.cs` | Modified | Add `[HttpPost] Restore` action |
| `DTOs/UpdateProductoDto.cs` | Modified | Add `MotivoCambioPrecio` (string?) |
| `Views/Productos/Index.cshtml` | Modified | Add "Restaurar" button |
| `db/migrations/` | New | `YYYYMMDD_XXX_producto_precios_historico.sql` |
| `db/docs/DECISIONES.md` | Modified | ADR for price history + Activo invariant |
| `tests/ExtraGasMVC.Tests/ProductoServiceTests.cs` | Modified | Tests for RestoreAsync + price history |
| `tests/ExtraGasMVC.Tests/` (new file) | New | `RecepcionServiceTests.cs` regression test |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Race on Pedido confirm: product deactivated between draft and confirm | Medium | Validate inside transaction boundary |
| `RestoreAsync` re-activates product that was deliberately deactivated | Low | Admin-only policy enforced at controller |
| Migration naming conflict with parallel work | Low | Checksum + schema_migrations tracking |
| Price-change hook fires on zero-value init (PrecioActual = 0 at creation) | Medium | Only log when `precio_anterior != precio_nuevo && precio_anterior != 0` |

## Rollback Plan

- **DB**: rollback via `mysql` — `DELETE FROM producto_precios_historico WHERE changed_at < X` + `DROP TABLE IF EXISTS producto_precios_historico`. The migration itself uses `CREATE TABLE IF NOT EXISTS` so re-running is safe.
- **Code**: revert service/controller changes in git; `dotnet build` is sufficient — no stateful migration needed for service-layer fixes.
- **UI**: remove "Restaurar" button from `Index.cshtml`.

## Dependencies

- `ClienteService.RestoreAsync` pattern (already in codebase, line 290) — reference only, no external dep.
- No new NuGet packages.
- DB migration must run before service-layer code in deployment order.

## Success Criteria

- [ ] `RestoreAsync` green in `ProductoServiceTests`; Controller action returns 200/404; button visible when `soloActivos=false`
- [ ] `RecepcionServiceTests`: deactivated product throws `InvalidOperationException` in `ValidarItemsPreCommitAsync`
- [ ] `PedidoServiceTests`: deactivated product at confirm time throws with product name; no partial writes committed
- [ ] `ProductoServiceTests`: price change creates `producto_precios_historico` row; `MotivoCambioPrecio` stored correctly; `MotivoCambioPrecio` ignored when price unchanged
- [ ] Smoke query: `SELECT * FROM producto_precios_historico ORDER BY changed_at DESC LIMIT 5` returns rows after a price update
- [ ] `dotnet build src/ExtraGasMVC` compiles without warnings
- [ ] `dotnet test` passes with coverage >= 65% on new code
