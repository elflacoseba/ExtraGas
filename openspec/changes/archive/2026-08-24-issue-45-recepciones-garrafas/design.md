# Design: Issue #45 — Integración con Recepciones (compra de garrafas)

## Technical Approach

`RecepcionService.CreateAsync` opens an EF Core transaction (`BeginTransactionAsync`), inserts the `RecepcionProveedor` header (trigger `trg_recepciones_bi` fills `numero`), then iterates items. For each GARRAFA item it validates the code list (`count == item.cantidad`, dedupe, no DB duplicates), then loops once per code: insert `Garrafa` (trigger `trg_mov_garrafa_ai` will later sync `estado_garrafa_id` + `fecha_ultimo_movimiento` from its matching `MovimientoGarrafa`) and immediately insert the matching `MovimientoGarrafa` (`tipo=COMPRA`, `estado_origen=estado_destino=LLENA_DEPOSITO`, `cliente_id=NULL`, `empleado_id=operator`). Non-GARRAFA items only persist the `RecepcionItem`. The controller delegates 100% to the service and never touches `ExtraGasDbContext`. UI submits the whole form in one POST; `recepciones.js` serializes the dynamic item table into the `CrearRecepcionDto` shape.

This satisfies spec requirements #1–#9 and #10 (soft-delete rollback via `ReversarAsync`).

## Architecture Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Transaction boundary | Explicit `BeginTransactionAsync` (same pattern as `PedidoService.RegistrarCanjePedidoAsync` line 673) | Reuse proven house style; rollback is automatic if any throw bubbles. |
| Save-points per garrafa | One `SaveChanges` per `MovimientoGarrafa` so `trg_mov_garrafa_ai` runs against a real `garrafa_id` | Trigger requires `garrafa_id` already persisted; avoids batching hazards. |
| Single-step vs two-step | Single-step (form → atomic submit) | Spec explicitly chose this; avoids draft/persist dual state for atomicity guarantee. |
| State for new garrafa | `LLENA_DEPOSITO` with matching `estado_origen = estado_destino` on the movement | Logical "creation event" — the garrafa exists and is in the deposit before any delivery. |
| Capacity source | `producto.capacidad_kg` (nullable `decimal?`); reject if NULL for GARRAFA products | Avoids brittle UI override; matches AGENTS decision #1 (single source of truth). |
| Code uniqueness scope | Include soft-deleted rows when pre-validating against `garrafas` | Spec requirement #4 + project convention that soft-deleted rows still occupy the unique index (rationale: a recycled code could be silently re-used). |
| Reversal policy | `UPDATE ... SET deleted_at = NOW()` in one transaction; reject if any garrafa state ≠ `LLENA_DEPOSITO` | Spec #10 + AGENTS #6 (soft delete over `DELETE`). |
| VS split | Two stacked PRs (backend / UI), not one | LOC estimate exceeds 400-line review budget; see §11. |

## Data Flow

```
View (Create.cshtml)
    │ POST JSON { CrearRecepcionDto } + antiforgery
    ▼
RecepcionesController.Create
    │ resolves operator EmpleadoId via BaseController.GetCurrentUserId
    ▼
RecepcionService.CreateAsync(dto, usuarioId)
    │
    │   1. validate (codes/cantidad, dedupe, db dup)
    │   2. BeginTransactionAsync
    │   3. Add(RecepcionProveedor) → SaveChanges  (trigger sets numero)
    │   4. for each item: Add(RecepcionItem)
    │      if GARRAFA: per code →
    │          Add(Garrafa) → SaveChanges
    │          Add(MovimientoGarrafa COMPRA) → SaveChanges
    │          (trigger trg_mov_garrafa_ai updates garrafa estado/fecha)
    │   5. Commit / Rollback
    ▼
returns RecepcionDto → JSON → SweetAlert2 success → redirect Index
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/ExtraGasMVC/Services/Interfaces/IRecepcionService.cs` | New | Contract: `CreateAsync`, `ReversarAsync`, `GetProductosActivosAsync` (for the dropdown). |
| `src/ExtraGasMVC/Services/Implementations/RecepcionService.cs` | New | Transactional creation + soft-delete reversal. |
| `src/ExtraGasMVC/DTOs/CrearRecepcionDto.cs` + `CrearRecepcionItemDto` | New | Service input, includes `CodigosGarrafa` per item. |
| `src/ExtraGasMVC/Models/ViewModels/CrearRecepcionViewModel.cs` | New | Wraps the DTO + `IEnumerable<ProductoDto>` for the `<select>`. |
| `src/ExtraGasMVC/Controllers/RecepcionesController.cs` | Modified | Inject `IRecepcionService` + `IProductoService`; `Create` POST delegates; no `ExtraGasDbContext` direct access. |
| `src/ExtraGasMVC/Views/Recepciones/Create.cshtml` | Modified | 3-card AdminLTE layout (header, items table, buttons); textarea-per-GARRAFA row. |
| `src/ExtraGasMVC/wwwroot/js/recepciones.js` | New | Add/remove item rows; toggle textarea on `manejaGarrafaIndividual`; pre-submit serialize; bind antiforgery; SweetAlert2 confirm. |
| `src/ExtraGasMVC/Program.cs` | Modified | `AddScoped<IRecepcionService, RecepcionService>()`. |
| `src/ExtraGasMVC/Mappings/MappingProfile.cs` | Modified | Add `RecepcionProveedor ↔ RecepcionDto` mapping. |

(Reports/`PagosProveedor` controllers in the same file are out of scope.)

## Interfaces / Contracts

```csharp
public interface IRecepcionService
{
    Task<RecepcionDto> CreateAsync(CrearRecepcionDto dto, ulong? usuarioId, CancellationToken ct = default);
    Task<bool> ReversarAsync(ulong recepcionId, ulong? usuarioId, CancellationToken ct = default);
    Task<IEnumerable<ProductoDto>> GetProductosActivosAsync(CancellationToken ct = default);
}
```

```csharp
public class CrearRecepcionDto
{
    public ulong ProveedorId { get; set; }
    public ulong EmpleadoId { get; set; }   // optional — service resolves from usuarioId when 0
    public DateTime Fecha { get; set; }
    public string? NumeroFacturaProveedor { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public string? Observaciones { get; set; }
    public List<CrearRecepcionItemDto> Items { get; set; } = new();
}
public class CrearRecepcionItemDto
{
    public ulong ProductoId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public List<string> CodigosGarrafa { get; set; } = new();  // empty when product not GARRAFA
}
```

## Transactional core (non-obvious pattern)

```csharp
public async Task<RecepcionDto> CreateAsync(CrearRecepcionDto dto, ulong? usuarioId, CancellationToken ct)
{
    var operadorId = await ResolverEmpleadoIdAsync(usuarioId, ct)
        ?? throw new InvalidOperationException("No se pudo resolver el operador.");

    var estadoLlenaDepositoId = await LookupEstadoIdAsync(GarrafaEstados.LlenaDeposito, ct);
    var tipoCompraId          = await LookupTipoMovimientoIdAsync("COMPRA", ct);

    await using var tx = await _context.Database.BeginTransactionAsync(ct);
    try
    {
        var recepcion = new RecepcionProveedor { /* fields from dto */ };
        _context.RecepcionesProveedor.Add(recepcion);
        await _context.SaveChangesAsync(ct);   // trigger fills recepcion.Numero

        foreach (var itemDto in dto.Items)
        {
            var item = new RecepcionItem { RecepcionId = recepcion.Id, /* ... */ };
            _context.RecepcionItems.Add(item);
            await _context.SaveChangesAsync(ct);

            if (!await EsGarrafaAsync(itemDto.ProductoId, ct)) continue;

            ValidarCantidadEntera(itemDto);
            ValidarCodigosDuplicados(itemDto.CodigosGarrafa);
            await ValidarCodigosLibresAsync(itemDto.CodigosGarrafa, ct);

            var producto = await _context.Productos.FirstAsync(p => p.Id == itemDto.ProductoId, ct);
            var capacidad = producto.CapacidadKg
                ?? throw new InvalidOperationException($"Producto '{producto.Nombre}' sin capacidad_kg.");

            foreach (var codigo in itemDto.CodigosGarrafa.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var garrafa = new Garrafa {
                    Codigo = codigo, CapacidadKg = (byte)capacidad,
                    ProveedorId = recepcion.ProveedorId, RecepcionId = recepcion.Id,
                    FechaCompra = DateOnly.FromDateTime(recepcion.Fecha),
                    EstadoGarrafaId = estadoLlenaDepositoId, Activo = true,
                    CreatedBy = operadorId, UpdatedBy = operadorId,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                _context.Garrafas.Add(garrafa);
                try { await _context.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (IsDuplicateCodigo(ex))
                    { throw new InvalidOperationException($"Código duplicado: {codigo}."); }

                _context.MovimientosGarrafa.Add(new MovimientoGarrafa {
                    GarrafaId = garrafa.Id, Fecha = recepcion.Fecha,
                    TipoMovimientoId = tipoCompraId, RecepcionId = recepcion.Id,
                    EstadoOrigenId = estadoLlenaDepositoId,
                    EstadoDestinoId = estadoLlenaDepositoId,
                    EmpleadoId = operadorId,
                    Observaciones = $"Compra - {recepcion.Numero}",
                    CreatedBy = operadorId
                });
                await _context.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
        return _mapper.Map<RecepcionDto>(await LoadRecepcionWithIncludesAsync(recepcion.Id, ct));
    }
    catch
    {
        await tx.RollbackAsync(ct);
        throw;
    }
}
```

`ReversarAsync` mirrors this with three `ExecuteUpdateAsync` (recepciones / garrafas / movimientos) plus a `WHERE estado_garrafa_id = (LLENA_DEPOSITO)` guard loaded first; rows already `deleted_at IS NOT NULL` are left untouched (idempotent).

## Validation Rules

| Rule | Layer | Trigger / Message |
|---|---|---|
| Operador sin EmpleadoId | service | throw before tx, "No se pudo resolver el operador" |
| `cantidad != decimal.Truncate(cantidad)` for GARRAFA | service | "Cantidad debe ser entera para GARRAFA" |
| `Math.Truncate(cantidad) != codigos.Count` | service | "Item {idx}: esperaba X códigos, recibió Y" |
| Duplicate code (case-insensitive) | service | throw "Código duplicado: {codigo}" |
| Code exists in `garrafas` (incl. soft-deleted) via `IgnoreQueryFilters()` | service | "Código {codigo} ya existe en el sistema" |
| Product GARRAFA without `CapacidadKg` | service | "Producto {nombre} sin capacidad_kg" |
| MySQL 1062 race on `uq_garrafas_codigo` | service, inside SaveChanges catch | InvalidOperationException |
| `trg_mov_garrafa_ai` updates `estado_garrafa_id` and `fecha_ultimo_movimiento` automatically | trigger | source of truth — app must NOT write those columns |
| `trg_recepciones_bi` overwrites `numero` on INSERT | trigger | leave `Numero = null` pre-insert, the EF mapping uses `SetAfterSaveBehavior(PropertySaveBehavior.Ignore)` (already configured in `RecepcionProveedorConfiguration` lines 21) |

## Testing Strategy

No test runner configured. Validation is by smoke SQL probes (below) + manual UI walkthrough covering all 18 spec scenarios.

## Threat Matrix

N/A — design introduces no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundaries. It is a backend (EF Core + service) + Razor view + plain JS change.

## Migration / Rollout

No migration. Everything required exists after issue #36 (PR #60): `maneja_garrafa_individual`, `movimientos_garrafa.recepcion_id`, FKs, and the `COMPRA` row in `tipos_movimiento_garrafa`.

Rollout = code deploy. No feature flag; the existing `Create` endpoint is replaced.

## Smoke Commands

```bash
# Pre
mysql -uroot extragas -e "SELECT COUNT(*) FROM productos p JOIN tipos_producto t ON t.id=p.tipo_producto_id WHERE t.codigo='GARRAFA' AND p.activo=1;"
# Submit 1 GARRAFA item (cant=3) + 1 carbon item via UI
mysql -uroot extragas <<SQL
SELECT * FROM recepciones_proveedor ORDER BY id DESC LIMIT 1;
SELECT COUNT(*) FROM recepcion_items WHERE recepcion_id = (SELECT id FROM (SELECT MAX(id) FROM recepciones_proveedor) t);
SELECT codigo, estado_garrafa_id, capacidad_kg, proveedor_id, recepcion_id, fecha_compra
  FROM garrafas WHERE recepcion_id = (SELECT id FROM (SELECT MAX(id) FROM recepciones_proveedor) t);
SELECT tipo_movimiento_id, estado_origen_id, estado_destino_id, cliente_id, empleado_id, recepcion_id
  FROM movimientos_garrafa WHERE recepcion_id = (SELECT id FROM (SELECT MAX(id) FROM recepciones_proveedor) t);
SQL
```

Expected: 1 recepción with `numero='REC-PROV-YYYY-NNNNN'`, 2 items, N garrafas all in `LLENA_DEPOSITO`, N movimientos `COMPRA` with `cliente_id IS NULL`.

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| MySQL 1062 race (two simultaneous submits of the same code) | Inner `DbUpdateException`/`MySqlException.Number==1062` → throw `InvalidOperationException` with code. |
| FK circular insert order (recepciones→items→garrafas→movimientos) | Strict insert order: header first, then per-item, save before children. |
| `trg_mov_garrafa_ai` rewrites `estado_garrafa_id` and `fecha_ultimo_movimiento` | App writes only the other fields; the trigger is authoritative (house convention since #36). |
| `cantidad` is `decimal(10,2)` but must be integer for GARRAFA | Validated by `Math.Truncate` comparison before SaveChanges. |
| Authn identity unavailable in dev | Service throws early `InvalidOperationException` instead of writing garbage. |

## LOC estimate vs review budget

| File | LOC |
|---|---|
| `IRecepcionService.cs` | ~25 |
| `RecepcionService.cs` | ~250 |
| `DTOs/CrearRecepcionDto.cs` | ~30 |
| `Models/ViewModels/CrearRecepcionViewModel.cs` | ~30 |
| `Controllers/RecepcionesController.cs` (modified, +LOC) | +30 |
| `MappingProfile.cs` (modified, +LOC) | +10 |
| `Program.cs` (1 line) | +1 |
| `Views/Recepciones/Create.cshtml` (rewrite) | ~200 |
| `wwwroot/js/recepciones.js` | ~120 |
| **Subtotal backend** | **~376** |
| **Subtotal UI** | **~320** |
| **Total** | **~696** |

**⚠ Exceeds the 400-line review budget.** Recommend splitting into two stacked PRs (`stacked-to-main`):

- **PR #1 — Backend**: service, interface, DTO, view-model, controller refactor, mapping, `Program.cs`. ~376 LOC. Pure compile + smoke SQL.
- **PR #2 — UI**: `Create.cshtml` rewrite + `recepciones.js` new file. ~320 LOC. Depends on PR #1 merged (or rebased onto main after merge).

The `sdd-tasks` phase should encode this split explicitly so `sdd-apply` produces two commits/branches.

## Open Questions

- None blocking. The proposal/spec closed all ambiguous decisions.

---

## Appendix — repository conventions worth re-reading before coding

- Existing transaction pattern: `PedidoService.RegistrarCanjePedidoAsync` (lines 673–730) and `GarrafaService.CambiarEstadoAsync` (lines 191–226).
- Operator lookup pattern: `GarrafaService` resolves `EmpleadoId` from `currentUserId` via `_context.Empleados` query (lines 182–189).
- Duplicate-catch helper: `SaveOrThrowDuplicateAsync` in `GarrafaService.cs` line 420 — reuse or mirror for 1062 handling.
- Trigger-aware mapping: `RecepcionProveedorConfiguration` already sets `SetAfterSaveBehavior(PropertySaveBehavior.Ignore)` on `MontoPagado` (line 55) and `Numero` is `ValueGeneratedOnAdd` (line 20).
- Identity-from-claim helper: `BaseController.GetCurrentUserId()` (returns `ulong?`); call it before delegating.
- Single-class multi-controller pattern: `RecepcionesController.cs` already houses `PagosProveedorController` and `ReportesController` — keep them grouped.
