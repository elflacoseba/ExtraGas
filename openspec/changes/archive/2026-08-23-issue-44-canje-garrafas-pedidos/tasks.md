# Tasks: issue-44-canje-garrafas-pedidos

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~280–320 (9 files; modal is largest contributor) |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR (no slicing needed) |
| Delivery strategy | single-pr |
| Chain strategy | size-exception |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: Low

Forecast reasoning: total diff stays below the 400-line budget (orchestrator's preliminary estimate verified against target file sizes). No work-unit split needed; `single-pr` applies directly with no `size:exception` approval required.

## Phase 1: Foundation (DTOs, interfaces, ViewModel)

- [x] 1.1 In `DTOs/GarrafaDto.cs`, add nullable `PedidoId` + `TipoMovimientoCodigo` to `CambiarEstadoGarrafaDto`; add new `CodigoGarrafaItemDto { ItemId, Codigos }`.
- [x] 1.2 In `Services/Interfaces/IGarrafaService.cs`, declare `RegistrarMovimientoPorCanjeAsync(garrafaId, estadoDestinoId, clienteId, pedidoId, tipoMovimientoCodigo, usuarioId, ct)`.
- [x] 1.3 In `Services/Interfaces/IPedidoService.cs`, declare `RegistrarCanjePedidoAsync(pedidoId, codigosPorItem, usuarioId, ct)`.
- [x] 1.4 In `Models/ViewModels/PedidoEditViewModel.cs`, add `List<PedidoItemGarrafaVm> ItemsGarrafaCanje` filtered by `tipo_linea ∈ {ENTREGA,DEVOLUCION}` AND `producto.ManejaGarrafaIndividual == TRUE`.

## Phase 2: Service Layer (transactions + idempotency)

- [x] 2.1 In `Services/Implementations/GarrafaService.cs`, implement `RegistrarMovimientoPorCanjeAsync`: resolve destino estado from `tipoMovimientoCodigo`, validate `GarrafaTransiciones.EsValida`, build `MovimientoGarrafa` with `PedidoId` + non-`CAMBIO_ESTADO` `TipoMovimientoId`, set `garrafa.ClienteId` (or NULL); trigger owns `estado_garrafa_id`/`fecha_ultimo_movimiento`. No own transaction.
- [x] 2.2 In `Services/Implementations/PedidoService.cs`, implement `RegistrarCanjePedidoAsync`: pre-check `movimientos_garrafa WHERE pedido_id` (throw `InvalidOperationException` if any), pre-validate every code (existence + estado + cliente_id match + count), `BeginTransactionAsync`, loop items → `RegistrarMovimientoPorCanjeAsync`, set `pedido.EstadoPedidoId = CONFIRMADO` + `UpdatedBy`, `CommitAsync`.

## Phase 3: Controller Wiring

- [x] 3.1 In `Controllers/PedidosController.cs`, in `CambiarEstado` POST, when `nuevoEstadoId == CONFIRMADO`, deserialize `CodigosGarrafaJson` to `Dictionary<ulong, List<string>>` and invoke `RegistrarCanjePedidoAsync`. Map `InvalidOperationException` → `TempData["Error"]` and redirect to `Edit`.

## Phase 4: UI (Edit modal + Details traceability)

- [x] 4.1 In `Views/Pedidos/Edit.cshtml`, add Bootstrap modal partial with one `<textarea>` per `ItemsGarrafaCanje` entry (label: producto + capacidadKg + tipoLinea + expected count); filter via `ManejaGarrafaIndividual` (NOT `UnidadVenta`).
- [x] 4.2 Replace `js-confirmar-btn`: `e.preventDefault()` when garrafa-capable items exist, else submit. JS trims/dedupes per textarea; count mismatch → SweetAlert block.
- [x] 4.3 On modal confirm, JS serializes `Dictionary<ulong, string[]>` to JSON, sets hidden input `CodigosGarrafaJson`, then submits (preserving antiforgery).
- [x] 4.4 In `Views/Pedidos/Details.cshtml`, add "Movimientos de garrafas" card joining `movimientos_garrafa` + `garrafas.codigo` + `tipos_movimiento_garrafa.codigo` + `fecha` filtered by `pedido_id`, rendered only when CONFIRMADO.

## Phase 5: Smoke Verification (no test runner)

- [ ] 5.1 Smoke: confirm pedido with 2 ENTREGA + 1 DEVOLUCION GARRAFA items; run `SELECT g.codigo, tmg.codigo, m.fecha FROM movimientos_garrafa m JOIN garrafas g ON g.id=m.garrafa_id JOIN tipos_movimiento_garrafa tmg ON tmg.id=m.tipo_movimiento_id WHERE m.pedido_id=<id>` — expect N=3 with correct `tipo_movimiento`.
- [ ] 5.2 Smoke: re-run 5.1 with one invalid code (wrong estado); assert zero rows for `pedido_id` AND `pedidos.estado_pedido_id` unchanged — atomicity proof.
- [ ] 5.3 Smoke: CONFIRMADO → PENDIENTE → CONFIRMADO; assert second CONFIRMADO throws and no duplicate `movimientos_garrafa` rows — idempotency proof.
- [ ] 5.4 Smoke: confirm pedido with only VENTA / carbón items; assert modal never opens and pedido transitions straight to CONFIRMADO — UI filter proof. _(User-runs manually — see verify-report.md)_

- [x] 5.5 Build: `dotnet build src/ExtraGasMVC` exits 0. _(Verified during sdd-verify)_
- [x] 5.5 Build: `dotnet build src/ExtraGasMVC` exits 0.
