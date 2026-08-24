# Proposal: issue-44-canje-garrafas-pedidos

## Intent

Automate garrafa physical tracking when a pedido is confirmed with `ENTREGA` or `DEVOLUCION` items — create matching `movimientos_garrafa` and update `garrafas.cliente_id`/`estado_garrafa_id` in the same transaction, reusing the infrastructure from issue #36 (`GarrafaService.CambiarEstadoAsync`).

## Scope

### In Scope
- Extend `CambiarEstadoGarrafaDto` (or create `RegistrarCanjePedidoGarrafaDto`) to carry `pedidoId` and `tipoMovimientoCodigo` (overrides the default `CAMBIO_ESTADO` behavior)
- Overload `IGarrafaService.CambiarEstadoAsync` to accept pedido context and emit `ENTREGA_CLIENTE`/`DEVOLUCION_CLIENTE` movements instead of `CAMBIO_ESTADO`
- Add `IPedidoService.RegistrarCanjeGarrafasAsync(pedidoId, codigosPorItem)` — receives the list of garrafa codes selected per item, calls the garrafa service per code, and returns the result summary
- Update `PedidosController.CambiarEstadoAsync` (POST `/Pedidos/CambiarEstado`) to call `RegistrarCanjeGarrafasAsync` when transitioning to `CONFIRMADO`
- **Modal simple en `Edit.cshtml`**: al confirmar el pedido, mostrar un modal Bootstrap con un textarea/input por cada item `ENTREGA`/`DEVOLUCION` donde el operador carga los códigos de garrafas (uno por línea, o escaneando). Cantidad de códigos validada contra `pedido_item.cantidad`. Si no se cargan códigos, bloquear el CONFIRMADO con mensaje claro.
- Add DB migration for any schema additions (if needed — likely none; seed types already exist)
- Validate stock and code validity: each code must exist, be in correct estado (`LLENA_DEPOSITO` for ENTREGA, `EN_CLIENTE` matching pedido.cliente_id for DEVOLUCION), and cantidad de códigos = pedido_item.cantidad

### Out of Scope
- Reverse canje / undo flow after delivery is confirmed
- Changes to `GarrafaTransiciones` rule engine or garrafa tracking model itself
- Auto-selección de garrafas (modo "agarrar las primeras N") — siempre selección explícita por código

## Capabilities

> Contract with sdd-spec. `openspec/specs/` is empty — all are New.

### New Capabilities
- `pedido-canje-garrafa`: Links a confirmed pedido's ENTREGA/DEVOLUCION items to physical garrafa movements (`ENTREGA_CLIENTE`/`DEVOLUCION_CLIENTE`), updating `garrafas.cliente_id` and `estado_garrafa_id` in one transaction. Operator explicitly selects each physical garrafa via code entry (textarea/input, one code per line) in a Bootstrap modal before confirmation. Code count per item must equal `pedido_item.cantidad`.

## Approach

Reuse `GarrafaService.CambiarEstadoAsync` (issue #36) as the transaction wrapper — it already handles the atomic `MovimientoGarrafa INSERT` + `Garrafa UPDATE` via trigger `trg_mov_garrafa_ai`. Create a new overload that accepts a `pedidoId` + `tipoMovimientoCodigo` and builds the correct `CambiarEstadoGarrafaDto` for each code received. The new `PedidoService.RegistrarCanjeGarrafasAsync` orchestrates the loop over items and codes.

UI flow:
1. From `Pedidos/Edit.cshtml`, when operator clicks "Confirmar" on a pedido with items `ENTREGA`/`DEVOLUCION`, the existing `js-confirmar-btn` opens a Bootstrap modal instead of submitting immediately.
2. Modal renders one textarea per such item, labeled "Códigos de garrafas para {productoNombre} (cantidad esperada: N)". Operator types/scans codes (one per line).
3. Client-side validation: trim, dedupe, count must equal N. Empty / invalid → SweetAlert blocking.
4. On submit, codes travel as `Dictionary<ulong, List<string>>` (itemId → codes) via POST to `/Pedidos/CambiarEstado`.
5. Server validates codes exist + are in correct estado, then calls `RegistrarCanjeGarrafasAsync` per item within the pedido transaction.

Key design decisions:
- `trg_mov_garrafa_ai` is the single source of truth for `estado_garrafa_id` and `fecha_ultimo_movimiento` — the app must NOT set these columns directly; they come from the movement.
- The app DOES set `garrafas.cliente_id` directly: `ENTREGA_CLIENTE` → `pedido.cliente_id`; `DEVOLUCION_CLIENTE` → `NULL`.
- `monto_pagado` on `pedidos` is maintained by its trigger — app never writes it.
- The new DTO carries `pedidoId` so `movimientos_garrafa.pedido_id` is set, enabling traceability without a join table.
- Modal is the minimum-viable UI: no autocomplete, no grilla. Codes typed/scanned; server is source of truth for validity.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `DTOs/CambiarEstadoGarrafaDto.cs` | Modified | Add `PedidoId` and `TipoMovimientoCodigo` nullable fields |
| `Services/Interfaces/IGarrafaService.cs` | Modified | Add overload `CambiarEstadoAsync(dto, pedidoId, tipoMovimientoCodigo)` |
| `Services/Implementations/GarrafaService.cs` | Modified | Implement overload; set `cliente_id` on garrafa; pass `tipoMovimientoCodigo` to movimiento |
| `Services/Interfaces/IPedidoService.cs` | Modified | Add `RegistrarCanjeGarrafasAsync(pedidoId, codigosPorItem)` |
| `Services/Implementations/PedidoService.cs` | Modified | Implement `RegistrarCanjeGarrafasAsync` |
| `Controllers/PedidosController.cs` | Modified | Accept `codigosPorItem` from form post; call `RegistrarCanjeGarrafasAsync` when new state = CONFIRMADO |
| `Views/Pedidos/Edit.cshtml` | Modified | Bootstrap modal with code textareas; updated `js-confirmar-btn` to open modal; form posts codes |
| `Views/Pedidos/Details.cshtml` | Modified | Show linked movimientos_garrafa for traceability post-confirm |
| `db/migrations/` | New | Migration if schema changes needed (seed types exist — likely none) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| `ENTREGA_CLIENTE`/`DEVOLUCION_CLIENTE` seed types missing | Low | Verified present in `20260102_000009_seed_data.sql` |
| UI allows self-transition (EN_CLIENTE→EN_CLIENTE) | Medium | Server-side validation rejects codes in wrong estado |
| Operator typos code or scans wrong barcode | Medium | Server validates code existence + estado before any DB write; modal blocks submit with SweetAlert if any code invalid |
| Code count mismatches `pedido_item.cantidad` | Medium | Client-side + server-side validation: counts must match exactly before CONFIRMADO proceeds |
| `CambiarEstadoGarrafaDto` schema change breaks existing callers | Low | Add nullable fields; existing callers unaffected |
| Transaction failure mid-loop (partial delivery) | Low | Full rollback via existing transaction scope in GarrafaService + new wrapper transaction in PedidoService |
| Modal UX: operator submits with empty codes for non-garrafa items (e.g., carbón) | Medium | Modal only renders textareas for items where `tipo_linea IN (ENTREGA, DEVOLUCION)` AND `producto.tipo_producto = GARRAFA` |

## Rollback Plan

1. Revert `IGarrafaService`, `GarrafaService`, `IPedidoService`, `PedidoService`, `PedidosController` to previous commit
2. Revert `CambiarEstadoGarrafaDto` — remove added fields
3. No DB migration to revert (no schema changes introduced)
4. If deployed: rebuild and restart app; no data cleanup needed

## Dependencies

- Issue #36 ✅ (GarrafaService.CambiarEstadoAsync) — already implemented
- Módulo Pedidos completo ✅ — exists and functions
- Seed types `ENTREGA_CLIENTE` and `DEVOLUCION_CLIENTE` ✅ — verified in seed

## Success Criteria

- [ ] `dotnet build src/ExtraGasMVC` succeeds with zero errors
- [ ] Smoke query: `SELECT * FROM movimientos_garrafa WHERE pedido_id = <test>` returns N records (N = total garrafas canjeadas) after a test pedido confirmation
- [ ] Smoke query: `garrafas.cliente_id` is correctly set/null after delivery/devolution
- [ ] Smoke query: `garrafas.estado_garrafa_id` reflects correct post-movement state (verified via trigger)
- [ ] Partial failure (e.g., one code invalid) rolls back entire transaction — pedido stays in previous state, no garrafa moved
- [ ] UI: modal blocks CONFIRMADO submit if any code missing or count mismatches `pedido_item.cantidad`
- [ ] UI: modal only shows textareas for items of type ENTREGA/DEVOLUCION with garrafa-capable products
