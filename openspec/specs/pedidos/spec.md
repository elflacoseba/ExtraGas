# Delta for pedidos

> First-time capability. The main spec (`openspec/specs/pedidos/spec.md`) will be created from this delta on archive. `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Validación de productos activos al confirmar pedido

The system MUST validate, at CONFIRMADO transition time, that every `PedidoItem.ProductoId` resolves to a product with `activo = true AND deleted_at IS NULL`. Failure MUST throw `InvalidOperationException` naming the offending product and roll back the entire confirmation transaction.

#### Scenario: Todos los productos activos acepta confirmación

- GIVEN a PENDIENTE pedido with 3 items, all referencing products with `activo = true AND deleted_at IS NULL`
- WHEN CONFIRMADO is submitted
- THEN pedido → CONFIRMADO AND no `InvalidOperationException` is thrown AND all item writes commit atomically

#### Scenario: Producto desactivado entre draft y confirm rechaza

- GIVEN a PENDIENTE pedido draft referencing product P; P is later deactivated by an Admin before the operator hits CONFIRMADO
- WHEN the operator submits CONFIRMADO
- THEN `InvalidOperationException("El producto {nombre} fue desactivado, refrescá el pedido.")` MUST be thrown AND pedido MUST remain PENDIENTE AND no item, garrafa movement, or pago MUST commit

#### Scenario: Producto soft-deleted entre draft y confirm rechaza

- GIVEN a PENDIENTE pedido referencing product P; P is later soft-deleted (`deleted_at` set) before confirm
- WHEN the operator submits CONFIRMADO
- THEN the same `InvalidOperationException` MUST be thrown; nothing commits

#### Scenario: Validación corre dentro del boundary transaccional

- GIVEN the validation throws partway through confirming
- WHEN the transaction is rolled back
- THEN no `pedido_items`, `movimientos_garrafa`, `pagos`, or `pedidos` row changes MUST persist (no partial writes)
