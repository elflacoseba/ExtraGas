# Delta for pedido-canje-garrafa

> First-time capability. The main spec (`openspec/specs/pedido-canje-garrafa/spec.md`) was created from this delta. The `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Confirmar pedido con items de canje crea movimientos de garrafas

The system MUST create one `movimientos_garrafa` per submitted code per `ENTREGA`/`DEVOLUCION` GARRAFA item on CONFIRMADO transition.

#### Scenario: Confirmación con ENTREGA mueve garrafas del depósito al cliente

- GIVEN an `ENTREGA` GARRAFA item (cantidad=2) with 2 codes in estado `LLENA_DEPOSITO`
- WHEN CONFIRMADO is submitted with those codes
- THEN pedido → `CONFIRMADO` AND 2 `movimientos_garrafa` (`ENTREGA_CLIENTE`, `pedido_id` linked) AND garrafas get `cliente_id`=pedido.cliente_id, `estado_garrafa_id`→`EN_CLIENTE`

#### Scenario: Confirmación con DEVOLUCION mueve garrafas del cliente al depósito

- GIVEN a `DEVOLUCION` GARRAFA item (cantidad=3) with 3 codes in estado `EN_CLIENTE` for same cliente_id
- WHEN CONFIRMADO is submitted with those codes
- THEN pedido → `CONFIRMADO` AND 3 `movimientos_garrafa` (`DEVOLUCION_CLIENTE`) AND garrafas get `cliente_id`=NULL, `estado_garrafa_id`→`LLENA_DEPOSITO`

### Requirement: Validación de códigos antes de CONFIRMADO

The system MUST reject CONFIRMADO when any code fails existence, estado, or count validation.

#### Scenario: Código inexistente bloquea CONFIRMADO

- GIVEN an ENTREGA item (cantidad=2), 1 valid + 1 unknown code
- WHEN CONFIRMADO is submitted
- THEN pedido state MUST NOT change AND the response MUST identify the unknown code

#### Scenario: Código en estado incorrecto bloquea CONFIRMADO

- GIVEN an ENTREGA item (cantidad=1), code in estado `EN_CLIENTE`
- WHEN CONFIRMADO is submitted
- THEN pedido state MUST NOT change AND the response MUST identify the code and its estado

#### Scenario: Cantidad de códigos distinta de pedido_item.cantidad bloquea CONFIRMADO

- GIVEN an ENTREGA item (cantidad=3), only 2 codes
- WHEN CONFIRMADO is submitted
- THEN pedido state MUST NOT change AND the response MUST identify the item and the mismatch

### Requirement: Atomicidad de la transacción de canje

The system MUST roll back every garrafa movement and the pedido state change on any code failure mid-loop.

#### Scenario: Fallo parcial rollbackea el pedido completo

- GIVEN 3 ENTREGA items (6 codes), 4th code in `EN_CLIENTE`
- WHEN CONFIRMADO is submitted with all 6 codes
- THEN no `movimientos_garrafa` are inserted AND pedido state MUST remain unchanged AND no garrafa field is modified

### Requirement: UI de carga de códigos de garrafas

The system MUST show a Bootstrap modal with one textarea per `ENTREGA`/`DEVOLUCION` GARRAFA item on CONFIRMADO initiation.

#### Scenario: Modal muestra textareas solo para items GARRAFA con ENTREGA/DEVOLUCION

- GIVEN ENTREGA GARRAFA (cant=2) + DEVOLUCION GARRAFA (cant=1)
- WHEN clicking "Confirmar" on `Edit.cshtml`
- THEN the modal MUST render 2 textareas (producto name, expected count)

#### Scenario: Items VENTA, carbón o leña no requieren modal

- GIVEN only `VENTA` items, or `ENTREGA`/`DEVOLUCION` items of type `CARBON`/`LENA`
- WHEN clicking "Confirmar" on `Edit.cshtml`
- THEN the modal MUST NOT appear AND pedido → CONFIRMADO without codes

### Requirement: Trazabilidad post-CONFIRMADO

The system MUST render every `movimientos_garrafa` linked by `pedido_id` on the pedido `Details` view on CONFIRMADO.

#### Scenario: Details muestra los movimientos_garrafa del pedido confirmado

- GIVEN a CONFIRMADO pedido with 5 `movimientos_garrafa` linked by `pedido_id`
- WHEN opening `Pedidos/Details/{id}`
- THEN the view MUST list all 5 movements with garrafa `codigo`, `tipo_movimiento`, timestamp

### Requirement: Reversibilidad pre-entrega

The system MUST allow CONFIRMADO pedido to return to PENDIENTE without modifying `movimientos_garrafa` or garrafa state created at confirmation, pre-`ENTREGADO`.

#### Scenario: Pedido CONFIRMADO sin ENTREGAR puede volver a PENDIENTE

- GIVEN a CONFIRMADO pedido with N `movimientos_garrafa`, pre-`ENTREGADO`
- WHEN transitioning back to PENDIENTE
- THEN pedido → PENDIENTE AND the N movements and their effect on garrafas MUST remain unchanged

### Requirement: Auditoría del cambio de estado

The system MUST record the CONFIRMADO operator as `updated_by` on `pedidos` and `created_by` on each `movimientos_garrafa`.

#### Scenario: created_by y updated_by registran al usuario que confirma

- GIVEN an operator with id=U
- WHEN pedido is confirmed
- THEN `pedidos.updated_by`=U, each `movimientos_garrafa.created_by`=U, `pedidos.created_by` preserved