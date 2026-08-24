# Tasks: Issue #45 — Integración Recepciones→Garrafas (compra)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~696 (backend 376 + UI 320) |
| 400-line budget risk | **High** |
| Chained PRs recommended | **Yes** (stacked-to-main) |
| Suggested split | PR1 Backend T1–T8 · PR2 UI T9–T10 |
| Delivery strategy | single-pr (cacheado) |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | PR | Test cmd | Harness | Rollback |
|------|------|----|---------|---------|----------|
| 1 | Backend atómico | PR1 `feature/...-backend` → `develop` | `dotnet build src/ExtraGasMVC` | POST 1 GARRAFA (cant=3) + 1 carbón (cant=10); smoke SQL `design.md §Smoke Commands` | `git revert` PR1 — sin migración |
| 2 | UI dinámica | PR2 `feature/...-ui` → `develop` (tras PR1) | `dotnet build src/ExtraGasMVC` | Walkthrough: alta/baja items, textarea solo GARRAFA, modal SweetAlert2 | `git revert` PR2 borra view + JS |

## Phase 1 — Backend Foundation (PR1)

- [x] **T1**: `Services/Interfaces/IRecepcionService.cs` con `CreateAsync`, `ReversarAsync`, `GetProductosActivosAsync`.
- [x] **T2**: `DTOs/CrearRecepcionDto.cs` con clase externa + `CrearRecepcionItemDto` anidada (`CodigosGarrafa: List<string>`).
- [x] **T3**: `Models/ViewModels/CrearRecepcionViewModel.cs` que envuelve el DTO + `IEnumerable<ProductoDto>`.

## Phase 2 — Backend Core (PR1)

- [x] **T4**: `Services/Implementations/RecepcionService.cs` — `CreateAsync` transaccional. Resolver `EmpleadoId` (mirror `GarrafaService:182-189`) → lookup `LLENA_DEPOSITO`/`COMPRA` → `BeginTransactionAsync` → insert `RecepcionProveedor` (trigger `trg_recepciones_bi` llena `numero`) → foreach item: insert `RecepcionItem` → si GARRAFA: validar `cantidad==codigos.Count` + dedupe case-insensitive + `IgnoreQueryFilters` lookup existentes → foreach código: insert `Garrafa` con `SaveOrThrowDuplicateAsync` (mirror `GarrafaService:420`) → insert `MovimientoGarrafa` COMPRA (`cliente_id=NULL`, `estado_origen=estado_destino=LLENA_DEPOSITO`).
- [x] **T5**: `ReversarAsync` en el mismo archivo: `ExecuteUpdateAsync` soft-delete en 3 tablas con guard `estado_garrafa_id=LLENA_DEPOSITO` cargado primero.

## Phase 3 — Backend Wiring (PR1)

- [x] **T6**: Refactor `Controllers/RecepcionesController.cs`: inyectar `IRecepcionService` + `IProductoService`; POST `Create` delega 100% al servicio, sin acceso a `_context.RecepcionesProveedor`. Mantener antiforgery + `Json` camelCase de `BaseController`.
- [x] **T7**: `RecepcionProveedor ↔ RecepcionDto` en `Mappings/MappingProfile.cs`. **(no requerido: el servicio construye el DTO manualmente con joins; entities de recepción no tienen navigation properties)**
- [x] **T8**: `AddScoped<IRecepcionService, RecepcionService>` en `Program.cs`.

## Phase 4 — UI (PR2, tras PR1 mergeado)

- [x] **T9**: `wwwroot/js/recepciones.js`: agregar/quitar fila item, toggle textarea según `manejaGarrafaIndividual`, serializar al shape del DTO, binding antiforgery, SweetAlert2 de confirmación. Registrar en `_Scripts.cshtml` o `_AdminLTELayout.cshtml`. **(commit `9227cb1`, 274 LOC)**
- [x] **T10**: Rediseñar `Views/Recepciones/Create.cshtml` con 3 cards AdminLTE (encabezado, items, botones); textarea solo en filas GARRAFA; dropdown productos + botón "Agregar item"; modal SweetAlert2 al confirmar. **(commit `f0e5524`, +180/-32 — top-level inputs sin prefijo `Recepcion.` para bindear a `input: CrearRecepcionDto`)**

## Phase 5 — Verificación (post-PR2)

- [x] **T11**: Smoke end-to-end vía curl (simula exactamente el payload que `recepciones.js` produce en `serializarParaSubmit`): 1 GARRAFA cant=2 + 1 carbón cant=3 → HTTP 302 redirect; 1 recepción `REC-PROV-2026-00004`, 2 items, 2 garrafas `LLENA_DEPOSITO`, 2 movimientos `COMPRA` con `cliente_id IS NULL`. Tras verificar, soft-delete de la recepción + garrafas de prueba.
- [x] **T12**: Smoke rechazo vía curl — 3 negativos. Cantidad=3 con 2 códigos → "esperaba 3 código(s) y recibió 2". Dup case-insensitive (`NEW-CODE-X` + `new-code-x`) → "código(s) duplicado(s)". Reuso de código existente (`test-gas-pr2-a`) → "código(s) ya existente(s)". Todos dejan la BD intacta (count de recepciones no cambia). Reversión se hereda del PR1 smoke.

## Convenciones

- Conventional commits en español, scope `recepciones:` o `garrafas:`.
- Branch base: `develop`. Features: `feature/issue-45-recepciones-garrafas-backend` (PR1) y `feature/issue-45-recepciones-garrafas-ui` (PR2).
