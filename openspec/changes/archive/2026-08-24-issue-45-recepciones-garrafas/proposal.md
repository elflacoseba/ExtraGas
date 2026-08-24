# Proposal: Issue #45 — Integración con Recepciones (compra de garrafas)

## Intent

Al confirmar una recepción con productos `maneja_garrafa_individual = TRUE`, crear automáticamente cada `Garrafa` y su `MovimientoGarrafa` (tipo `COMPRA`) en transacción atómica. El operador hoy lo hace a mano.

## Scope

**In Scope:** `IRecepcionService` + `RecepcionService` (Scoped); refactor de `RecepcionesController.Create`; `CrearRecepcionViewModel` + DTO; rediseño de `Views/Recepciones/Create.cshtml`; `wwwroot/js/recepciones.js`; registro en `Program.cs`.

**Out of Scope:** CRUD completo de recepciones, máquina de estados, modificación de `RecepcionItem`, items VENTA/ENTREGA/DEVOLUCION, concurrencia multi-operador.

## Capabilities

**New:** `recepcion-compra-garrafa` — creación atómica de garrafas y movimientos al confirmar recepción con productos GARRAFA.

**Modified:** Ninguna.

## Approach

**Single-step confirm**: formulario único (encabezado + items + códigosGARRAFA) → submit atómico.

Transacción en `RecepcionService.CreateAsync`:
1. `BeginTransactionAsync` → insertar `RecepcionProveedor` + `RecepcionItem` → `SaveChanges`
2. Por cada item GARRAFA: validar `cantidad == códigos.count`; validar códigos no duplicados en submit; validar códigos no existentes en BD
3. Por cada código: insertar `Garrafa` (`LLENA_DEPOSITO`, `recepcion_id`, `proveedor_id`, `fecha_compra = recepcion.fecha`) → `SaveChanges`; insertar `MovimientoGarrafa` (`COMPRA`, `estado_origen = estado_destino = LLENA_DEPOSITO`, `recepcion_id`, sin `cliente_id`) → `SaveChanges`
4. `Commit`

Decisiones cerradas: `COMPRA` sin cliente; `estado_origen = estado_destino = LLENA_DEPOSITO`; `capacidad_kg` inferida de `Producto.CapacidadKg` (rechazar si no existe).

## Affected Areas

| Area | Impact |
|------|--------|
| `Services/Interfaces/IRecepcionService.cs` | New |
| `Services/Implementations/RecepcionService.cs` | New |
| `Controllers/RecepcionesController.cs` | Modified |
| `Models/ViewModels/CrearRecepcionViewModel.cs` | New |
| `DTOs/CrearRecepcionDto.cs` | New |
| `Views/Recepciones/Create.cshtml` | Modified |
| `wwwroot/js/recepciones.js` | New |
| `Program.cs` | Modified |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Códigos duplicados en textarea | Low | Split + dedup + count antes de transacción |
| Producto GARRAFA sin `CapacidadKg` | Low | Servicio rechaza con mensaje claro |
| Fallo parcial ( deadlock) | Low | Transacción explícita con rollback automático |

## Rollback Plan

Fallo antes de `Commit` → MySQL revierte automáticamente. Reversión post-confirmación: `UPDATE recepciones_proveedor SET deleted_at = NOW()` + `UPDATE garrafas SET deleted_at = NOW() WHERE recepcion_id = X` en misma transacción (soft delete, no DELETE).

## Dependencies

- Issue #36 (PR #60 merged): `maneja_garrafa_individual`, `MovimientoGarrafa.RecepcionId`
- `tipos_movimiento_garrafa` con `COMPRA` ya existe (no migración)
- FK `garrafas.recepcion_id` ya existe
- `trg_mov_garrafa_ai` es source of truth de estado y fecha

## Success Criteria

- [ ] `RecepcionesController.Create` delega 100% a servicio
- [ ] Productos GARRAFA crean N `Garrafa` + N `MovimientoGarrafa` (COMPRA) en misma transacción
- [ ] Validación: `cantidad != códigos.count` → rechazar; duplicados en submit → rechazar; código ya en BD → rechazar
- [ ] Auditoría: `CreatedBy` en `Garrafa` y `MovimientoGarrafa` = operador
- [ ] Vista muestra textarea por item GARRAFA, ninguno para carbón/leña
- [ ] Cero migraciones SQL nuevas
