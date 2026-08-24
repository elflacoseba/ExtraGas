# Spec: Recepcion Compra Garrafa

## Purpose

On `Recepciones/Create` submit, the system MUST atomically create `recepciones_proveedor`, its `recepcion_items`, and for every GARRAFA item the matching `garrafas` and `movimientos_garrafa` (tipo `COMPRA`). This eliminates the manual step operators do today.

## Requirements

### Requirement: Confirmación atómica con garrafas

The system MUST persist `recepciones_proveedor`, `recepcion_items`, and per-code `garrafas` + `movimientos_garrafa` in a single transaction; any failure MUST roll back all writes.

#### Scenario: GARRAFA pura crea N garrafas y N movimientos atómicamente

- GIVEN a recepción with 1 GARRAFA item (cantidad=3) and 3 codes
- WHEN the operator submits
- THEN 1 recepción, 1 item, 3 garrafas (LLENA_DEPOSITO) and 3 movimientos (COMPRA) are persisted

#### Scenario: Fallo parcial rollbackea todo

- GIVEN 1 GARRAFA item (cantidad=3), 1st code inserted, 2nd triggers DB error
- WHEN submit
- THEN 0 recepciones, 0 garrafas and 0 movimientos persist; the operator sees a clear error

#### Scenario: Item no GARRAFA no crea garrafas ni movimientos

- GIVEN 1 carbón item (cantidad=10, no codes)
- WHEN submit
- THEN 0 garrafas and 0 movimientos are created; the carbón item persists

### Requirement: Validación cantidad == códigos para GARRAFA

The system MUST reject when `recepcion_item.cantidad` differs from the codes count for GARRAFA items, including `cantidad=0` with codes and empty codes with `cantidad>0`.

#### Scenario: Cantidad coincide acepta

- GIVEN GARRAFA item (cantidad=4) with 4 codes
- WHEN submit
- THEN accepted

#### Scenario: Mismatch en cualquier dirección rechaza

- GIVEN GARRAFA item (cantidad=0) with 2 codes, OR (cantidad=5) with empty codes
- WHEN submit
- THEN the response identifies the item and the mismatch; nothing persists

### Requirement: Códigos únicos en submit

The system MUST reject when two submitted codes match ignoring case and surrounding whitespace.

#### Scenario: Duplicado exacto rechaza

- GIVEN codes `G001, G002, G001`
- WHEN submit
- THEN the response identifies the duplicate; nothing persists

#### Scenario: Duplicado que difiere en case rechaza

- GIVEN codes `G001, g001`
- WHEN submit
- THEN the response identifies the duplicate; nothing persists

### Requirement: Códigos no existentes en BD

The system MUST reject when any submitted code already exists in `garrafas`, including soft-deleted rows.

#### Scenario: Código nuevo acepta

- GIVEN code `G999` not in `garrafas`
- WHEN submit
- THEN accepted

#### Scenario: Código existente activo o soft-deleted rechaza

- GIVEN code `G001` in `garrafas` with `deleted_at` IS NULL OR `deleted_at` IS NOT NULL
- WHEN submit
- THEN the response identifies the existing code; nothing persists

### Requirement: Auditoría CreatedBy en Garrafa y MovimientoGarrafa

The system MUST set `created_by` and `updated_by` on each new `garrafa` and `created_by` on each new `movimiento_garrafa` to the operator (UsuarioId → EmpleadoId).

#### Scenario: Creación exitosa registra operador

- GIVEN an operator with EmpleadoId=E
- WHEN a GARRAFA item (cantidad=2) confirms
- THEN each garrafa.created_by=E, garrafa.updated_by=E, movimiento_garrafa.created_by=E

#### Scenario: Operador sin EmpleadoId rechaza sin escribir

- GIVEN the session has no resolvable EmpleadoId
- WHEN submit
- THEN rejected with a clear message; nothing persists

### Requirement: Movimiento COMPRA sin cliente

The system MUST create each `movimiento_garrafa` with `cliente_id` NULL, `tipo_movimiento_id`=`COMPRA`, `estado_origen_id`=`estado_destino_id`=`LLENA_DEPOSITO`, `recepcion_id` set, `empleado_id`=operator.

#### Scenario: Compra de N garrafas crea N movimientos correctos

- GIVEN GARRAFA item (cantidad=3) confirmed
- WHEN movements are inserted
- THEN 3 movimientos exist with cliente_id NULL, tipo=COMPRA, estado_origen=LLENA_DEPOSITO, estado_destino=LLENA_DEPOSITO, recepcion_id=X, empleado_id=operator

### Requirement: Garrafa inicial con LLENA_DEPOSITO, proveedor y fecha

The system MUST create each `garrafa` with `estado_garrafa_id`=`LLENA_DEPOSITO`, `proveedor_id`=recepcion.proveedor_id, `fecha_compra`=recepcion.fecha, `recepcion_id` set, `activo`=TRUE, `capacidad_kg`=producto.capacidad_kg.

#### Scenario: Producto GARRAFA con capacidad crea garrafa válida

- GIVEN a GARRAFA product with capacidad_kg=10
- WHEN the code persists
- THEN garrafa.capacidad_kg=10, estado_garrafa_id=LLENA_DEPOSITO, proveedor_id=X, fecha_compra=recepcion.fecha, activo=TRUE

#### Scenario: Producto GARRAFA sin capacidad rechaza

- GIVEN a GARRAFA product with capacidad_kg IS NULL
- WHEN submit
- THEN the response identifies the product; nothing persists

### Requirement: UI textarea códigos solo para items GARRAFA

The view MUST render a codes textarea only for items whose product has `maneja_garrafa_individual`=TRUE.

#### Scenario: Textareas se renderizan solo para items GARRAFA

- GIVEN 1 GARRAFA item (cant=2) + 1 carbón item (cant=10), OR only carbón/leña, OR 2 GARRAFA items
- WHEN rendering `Create.cshtml`
- THEN textareas appear 1, 0 or 2 respectively, matching the count of GARRAFA items

### Requirement: Refactor RecepcionesController a servicio

`RecepcionesController.Create` MUST delegate 100% to the reception service; the controller MUST NOT access `ExtraGasDbContext` directly for this action.

#### Scenario: Controller delega al servicio sin tocar DbContext

- GIVEN a valid submit
- WHEN Create runs
- THEN the service is invoked; the controller does not query `ExtraGasDbContext` for this action

### Requirement: Soft delete post-confirm para reversión

The system MUST allow post-confirm reversal via soft delete of the recepción, its garrafas, and its movimientos; physical DELETE is forbidden.

#### Scenario: Reversión simple limpia la recepción y sus garrafas

- GIVEN a confirmed recepción with 5 garrafas and 5 movimientos, all in LLENA_DEPOSITO
- WHEN the operator triggers reversal
- THEN the recepción, 5 garrafas and 5 movimientos have `deleted_at` set; no physical DELETE occurs

#### Scenario: Reversión con garrafas ya entregadas rechaza

- GIVEN a confirmed recepción where at least one garrafa has estado≠LLENA_DEPOSITO
- WHEN the operator triggers reversal
- THEN the response rejects the reversal; nothing is soft-deleted
