# Design: issue-44-canje-garrafas-pedidos

## Technical Approach

Extend `GarrafaService.CambiarEstadoAsync` (already transactional in issue #36) with an internal helper that accepts `pedidoId` + `tipoMovimientoCodigo`, and add a new orchestrator `PedidoService.RegistrarCanjePedidoAsync(pedidoId, codigosPorItem, usuarioId)` that validates every code BEFORE writing, then loops through items in a single ambient transaction. The `PedidosController.CambiarEstado` POST path is widened to accept a `CodigosGarrafaJson` form field; when `nuevoEstadoId == CONFIRMADO` and the pedido has garrafa-capable ENTREGA/DEVOLUCION items, the modal is shown client-side and the JSON is submitted. Re-CONFIRMADO is rejected server-side if `movimientos_garrafa` rows already exist for the `pedido_id` (idempotency guard).

## Architecture Decisions

### Decision: DTO shape — extend existing + new controller-bound DTO

| Option | Tradeoff | Decision |
|---|---|---|
| Extend `CambiarEstadoGarrafaDto` with nullable `PedidoId` + `TipoMovimientoCodigo`; controller POST adds new `CodigosGarrafaJson` string field | Existing callers unaffected; one DTO for the inner flow; new field is purely controller-form-binding | **Chosen** |
| New `RegistrarCanjePedidoGarrafaDto` separate from `CambiarEstadoGarrafaDto` | Two parallel DTOs to maintain; existing callers untouched but intent fragmented | Rejected (DRY) |
| Wrap DTO in a polymorphic `MovimientoContextDto` | Future-proof for `RecepcionContext`, but speculative; nothing today needs it | Rejected (YAGNI) |

### Decision: Overload of `IGarrafaService.CambiarEstadoAsync`

| Option | Tradeoff | Decision |
|---|---|---|
| Add **internal** `RegistrarMovimientoPorCanjeAsync(garrafaId, estadoDestinoId, clienteId, pedidoId, tipoMovimientoCodigo, usuarioId, ct)` on `GarrafaService` — no public overload, no `CambiarEstadoGarrafaDto` | Caller (PedidoService) doesn't pass through the UI-bound DTO; avoids the trigger-mismatch risk on `NuevoEstadoId` (canje uses `ENTREGA_CLIENTE` row, NOT `CAMBIO_ESTADO`); internal-only keeps the public contract stable | **Chosen** |
| Public overload `CambiarEstadoAsync(id, dto, pedidoId, tipoMovimientoCodigo)` | Pollutes the public surface with a canje-specific contract | Rejected |
| Refactor `CambiarEstadoAsync` to always require the extra params | Breaks the existing `GarrafasController.CambiarEstado` POST flow | Rejected |

`RegistrarMovimientoPorCanjeAsync` derives the destination estado ID internally from the type code (e.g., `ENTREGA_CLIENTE` → `EN_CLIENTE`) so callers cannot accidentally set the wrong target state. It enforces `GarrafaTransiciones.EsValida(origen, destino)` with the same matrix the manual flow uses (issue #40). It does NOT open its own transaction — it relies on the ambient one in `RegistrarCanjePedidoAsync`.

### Decision: Re-CONFIRMADO handling

| Option | Tradeoff | Decision |
|---|---|---|
| **Reject** with `InvalidOperationException` if `movimientos_garrafa` rows already exist for `pedido_id` when entering CONFIRMADO | Guarantees single canje per pedido; aligns with spec scenario "Reversibilidad pre-entrega" (PENDIENTE←CONFIRMADO is allowed, but going back to CONFIRMADO requires a new pedido) | **Chosen** |
| Accumulate movements on re-CONFIRMADO | Creates duplicate stock movements; breaks reports; fights the trigger | Rejected |
| Silent no-op when current state == CONFIRMADO | Loses operator feedback; hides user error | Rejected |

The check runs BEFORE the transaction opens so the rejection is cheap and the error is reported via the controller's `TempData["Error"]`.

### Decision: Transaction scope

| Option | Tradeoff | Decision |
|---|---|---|
| `RegistrarCanjePedidoAsync` opens an outer `BeginTransactionAsync`; loops through items calling `RegistrarMovimientoPorCanjeAsync`; updates pedido state; single `CommitAsync` | One rollback boundary covers all garrafa movements + the pedido state change; satisfies spec "Atomicidad de la transacción de canje" | **Chosen** |
| Each `RegistrarMovimientoPorCanjeAsync` opens its own transaction; pedido state change is a separate SaveChanges | Partial-failure scenario leaks orphan movements; violates spec atomicity | Rejected |
| Wrap inside `PedidoService.CambiarEstadoAsync` directly | Mixes two responsibilities (state validation + canje orchestration); harder to test | Rejected |

### Decision: Modal transport + validation

| Option | Tradeoff | Decision |
|---|---|---|
| Bootstrap 5 modal (rendered in `Edit.cshtml`); one `<textarea>` per garrafa-capable ENTREGA/DEVOLUCION item; on submit, JS serializes `Dictionary<ulong,string[]>` to JSON into a hidden input `CodigosGarrafaJson` | Single form post; no repeated querystring; matches existing `_Scripts.cshtml` SweetAlert pattern; new hidden input fits the antiforgery flow | **Chosen** |
| Plain form fields `CodigosGarrafa[<itemId>][<i>]=CODE` | Verbose, brittle on dynamic item count, harder to dedupe | Rejected |
| JSON `fetch()` POST (no antiforgery) | Breaks the existing `_StatusMessage.cshtml` TempData flow | Rejected |

Client-side validation: trim each line, drop empties, dedupe (case-sensitive), count must equal `pedido_item.cantidad`. On mismatch, SweetAlert blocks submit. Server-side re-validates: each code must exist, be `LLENA_DEPOSITO` for ENTREGA or `EN_CLIENTE` with `cliente_id == pedido.cliente_id` for DEVOLUCION. All checks happen BEFORE the transaction opens — first failure rolls back nothing because nothing was written. The "garrafa-capable" filter uses `producto.ManejaGarrafaIndividual == TRUE` (see Decision: Garrafa-capable item filter).

### Decision: Garrafa-capable item filter

Filter items where `tipo_linea ∈ {ENTREGA, DEVOLUCION}` AND `producto.ManejaGarrafaIndividual == TRUE`. `ManejaGarrafaIndividual` (BD column `maneja_garrafa_individual BOOLEAN`, default `FALSE`) is the existing discriminator (see seed `20260102_000009_seed_data.sql` line 126-127: GAS-10/15/45 all have `TRUE`). NOT `UnidadVenta` — that field only describes the sale unit and can be `GARRAFA` for products sold without individual tracking. Do not introduce a new column.

## Data Flow

```
View (Edit.cshtml)                        Controller                       Service                          DB
─────────────────                         ──────────                       ───────                          ──
[Confirmar btn] ─── click ──► js-confirmar-btn handler
                                  │
                                  ├──► render Bootstrap modal (server-rendered partial)
                                  │    one textarea per garrafa-capable item
                                  │
                                  ├──► user types/scans codes
                                  │
                                  ├──► JS: trim, dedupe, count match ──► [invalid] SweetAlert
                                  │    valid → serialize to JSON, set CodigosGarrafaJson
                                  │
                                  └──► form.submit() POST /Pedidos/CambiarEstado
                                                                            │
                                                                            ▼
                                       [PedidosController.CambiarEstado]
                                       if (nuevoEstadoId == CONFIRMADO && hayItemsCanje)
                                           Deserialize CodigosGarrafaJson
                                           ──► IPedidoService.RegistrarCanjePedidoAsync(id, codigos, userId, ct)
                                                                            │
                                                                            ▼
                                       [PedidoService.RegistrarCanjePedidoAsync]
                                       (1) Load pedido; rechazar si estado == CONFIRMADO
                                           OR movimientos_garrafa ya existen para pedido_id
                                       (2) Pre-validate ALL codes (existence, estado, cliente_id,
                                           count == pedido_item.cantidad)
                                       (3) BeginTransactionAsync
                                       (4) For each (itemId, codes):
                                             For each code: GarrafaService.RegistrarMovimientoPorCanjeAsync
                                                 INSERT movimientos_garrafa (trigger sets garrafas.estado_garrafa_id
                                                                          + fecha_ultimo_movimiento)
                                                 UPDATE garrafas SET cliente_id = (or NULL)
                                       (5) UPDATE pedidos SET estado_pedido_id = CONFIRMADO, updated_by = userId
                                       (6) CommitAsync
                                                                            │
                                       on InvalidOperationException ─────► TempData["Error"] + redirect Edit
                                       on success ───────────────────────► redirect Details (existing flow)
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `src/ExtraGasMVC/DTOs/GarrafaDto.cs` | Modify | Extend `CambiarEstadoGarrafaDto` with `ulong? PedidoId`, `string? TipoMovimientoCodigo`. Add `CodigoGarrafaItemDto { ulong ItemId; string[] Codigos; }` for the controller form binding. |
| `src/ExtraGasMVC/Services/Interfaces/IGarrafaService.cs` | Modify | Add internal-facing `RegistrarMovimientoPorCanjeAsync(...)` signature. |
| `src/ExtraGasMVC/Services/Implementations/GarrafaService.cs` | Modify | Implement `RegistrarMovimientoPorCanjeAsync`: derive destino estado from codigo, run `GarrafaTransiciones.EsValida`, build `MovimientoGarrafa` with `PedidoId` + non-`CAMBIO_ESTADO` `TipoMovimientoId`, set `garrafa.ClienteId`, do NOT set `estado_garrafa_id` or `fecha_ultimo_movimiento` (trigger). NO own transaction. |
| `src/ExtraGasMVC/Services/Interfaces/IPedidoService.cs` | Modify | Add `RegistrarCanjePedidoAsync(ulong pedidoId, IReadOnlyDictionary<ulong, IReadOnlyList<string>> codigosPorItem, ulong? usuarioId, CancellationToken ct)`. |
| `src/ExtraGasMVC/Services/Implementations/PedidoService.cs` | Modify | Implement orchestrator: pre-validate, idempotency guard, open ambient transaction, loop, commit. |
| `src/ExtraGasMVC/Controllers/PedidosController.cs` | Modify | `CambiarEstado` POST: deserialize `CodigosGarrafaJson` when target = CONFIRMADO and garrafa-capable items exist; call `RegistrarCanjePedidoAsync`; map exceptions to `TempData["Error"]`. |
| `src/ExtraGasMVC/Views/Pedidos/Edit.cshtml` | Modify | Add Bootstrap modal partial (one textarea per garrafa-capable item); replace `js-confirmar-btn` SweetAlert direct-submit with `e.preventDefault()` + show modal; on confirm, populate hidden input and submit form. |
| `src/ExtraGasMVC/Views/Pedidos/Details.cshtml` | Modify | New card "Movimientos de garrafas" rendering `MovimientoGarrafaDto` linked by `pedido_id`. |
| `src/ExtraGasMVC/Models/ViewModels/PedidoEditViewModel.cs` | Modify | Add `List<PedidoItemGarrafaVm> ItemsGarrafaCanje` (itemId, productoNombre, capacidadKg, cantidad, tipoLinea). |
| `db/migrations/` | None | No schema change. Types `ENTREGA_CLIENTE`/`DEVOLUCION_CLIENTE` already seeded (`20260102_000009_seed_data.sql` L89-90). |

## Interfaces / Contracts

```csharp
// New DTO additions (DTOs/GarrafaDto.cs)
public class CambiarEstadoGarrafaDto {
    public ulong NuevoEstadoId { get; set; }
    public ulong? ClienteId { get; set; }
    public string? Observaciones { get; set; }
    // NEW (nullable — existing callers unaffected)
    public ulong? PedidoId { get; set; }
    public string? TipoMovimientoCodigo { get; set; }
}
public class CodigoGarrafaItemDto {
    public ulong ItemId { get; set; }
    public List<string> Codigos { get; set; } = new();
}

// New service method
public interface IGarrafaService {
    Task RegistrarMovimientoPorCanjeAsync(
        ulong garrafaId, ulong estadoDestinoId, ulong? clienteId,
        ulong pedidoId, string tipoMovimientoCodigo, ulong? usuarioId,
        CancellationToken ct = default);
}
public interface IPedidoService {
    Task<bool> RegistrarCanjePedidoAsync(
        ulong pedidoId,
        IReadOnlyDictionary<ulong, IReadOnlyList<string>> codigosPorItem,
        ulong? usuarioId, CancellationToken ct = default);
}

// Non-obvious pattern: re-CONFIRMADO idempotency guard
// (runs BEFORE transaction opens, throws InvalidOperationException)
var yaCanjeado = await _context.MovimientosGarrafa
    .AsNoTracking()
    .AnyAsync(m => m.PedidoId == pedidoId, ct);
if (yaCanjeado)
    throw new InvalidOperationException(
        "Este pedido ya tiene movimientos de canje registrados. " +
        "No se puede confirmar dos veces.");
```

## Testing Strategy

No test runner (`openspec/config.yaml` `testing.runner.available: false`). Validation = smoke queries + manual UI walkthrough.

| Layer | What to Validate | Approach |
|---|---|---|
| Unit | — | None (no runner) |
| Integration | — | None (no runner) |
| Smoke DB | Movement rows linked by `pedido_id` after CONFIRMADO | `SELECT g.codigo, tmg.codigo AS tipo, m.fecha FROM movimientos_garrafa m JOIN garrafas g ON g.id=m.garrafa_id JOIN tipos_movimiento_garrafa tmg ON tmg.id=m.tipo_movimiento_id WHERE m.pedido_id=<id> ORDER BY m.id` |
| Smoke DB | Garrafa state correct post-ENTREGA | `SELECT codigo, estado_garrafa_id, cliente_id FROM garrafas WHERE id IN (<ids>)` — expect `EN_CLIENTE` and `cliente_id=pedido.cliente_id` |
| Smoke DB | Garrafa state correct post-DEVOLUCION | Same query — expect `LLENA_DEPOSITO` and `cliente_id=NULL` |
| Smoke DB | Atomicity: partial failure rolls back everything | Submit pedido with one bad code; assert `movimientos_garrafa WHERE pedido_id=<id>` = 0 rows and `pedidos.estado_pedido_id` unchanged |
| Smoke DB | Re-CONFIRMADO rejected | CONFIRMADO→PENDIENTE→CONFIRMADO; assert second CONFIRMADO throws and no duplicate `movimientos_garrafa` rows |
| Smoke DB | Idempotency: PENDIENTE←CONFIRMADO leaves movements intact | Run `UPDATE pedidos SET estado_pedido_id=<pendiente_id> WHERE id=<id>` then re-query movements — assert N unchanged |
| UI | Modal renders only for GARRAFA items | Manual: pedido with ENTREGA carbón + ENTREGA GAS-10 → only GAS-10 textarea appears |
| UI | Modal skipped for non-canje pedidos | Manual: pedido with only VENTA items → Confirmar submits directly, no modal |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. The change touches ASP.NET Core MVC controllers/services/views within an existing process; no new attack surface beyond standard antiforgery + auth (already enforced by `[Authorize(Policy = "OperadorOrAdmin")]` on `PedidosController`).

## Migration / Rollout

No migration required. Seed types `ENTREGA_CLIENTE`/`DEVOLUCION_CLIENTE` exist in `20260102_000009_seed_data.sql`. Rollback: revert controller, services, view, DTO commits — no data cleanup needed (DB schema unchanged, transactions roll back atomically on failure).

## Open Questions

None. All blocking decisions resolved with rationale above.