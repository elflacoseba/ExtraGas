# Delta for productos

> First-time capability. The main spec (`openspec/specs/productos/spec.md`) will be created from this delta on archive. `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Restore de producto soft-deleted

The system MUST allow an Admin to restore a soft-deleted product by flipping `Activo = true`, exposing it again in operational dropdowns.

#### Scenario: Admin restaura producto eliminado

- GIVEN a soft-deleted product (deleted_at IS NOT NULL, activo = false) with id=P
- WHEN an Admin POSTs `Productos/Restore/{P}`
- THEN `deleted_at` MUST be NULL, `activo` MUST be true, the product MUST appear in dropdowns of Pedidos and Recepciones, AND `updated_by` MUST be the admin's EmpleadoId

#### Scenario: No-Admin intenta restaurar rechaza con 403

- GIVEN a soft-deleted product id=P, an Operator (not Admin) session
- WHEN Operator POSTs `Productos/Restore/{P}`
- THEN the action MUST return 403 (Forbidden); product state MUST NOT change

#### Scenario: Botón "Restaurar" solo se renderiza en vista de inactivos

- GIVEN `Productos/Index` rendered with `soloActivos=false`
- WHEN the row corresponds to a product with `deleted_at IS NOT NULL`
- THEN the view MUST render a "Restaurar" button that POSTs to `Productos/Restore/{id}`

### Requirement: Invariante producto.Activo ⇒ elegible en dropdowns

The system MUST expose only products with `activo = true AND deleted_at IS NULL` in Pedidos and Recepciones product dropdowns. A deactivated product MUST be excluded from both dropdowns at load time.

#### Scenario: Producto activo aparece en dropdowns

- GIVEN a product with `activo = true AND deleted_at IS NULL`
- WHEN Pedidos or Recepciones load the product list
- THEN the product MUST appear in the dropdown

#### Scenario: Producto desactivado NO aparece en dropdowns

- GIVEN a product with `activo = false OR deleted_at IS NOT NULL`
- WHEN Pedidos or Recepciones load the product list
- THEN the product MUST NOT appear in the dropdown

### Requirement: Hook de histórico de precios en UpdateAsync

The system MUST write one `producto_precios_historico` row per `UpdateAsync` call when `PrecioActual` changes (and prior price is not zero). The motive is required only when price changes.

#### Scenario: Cambio de precio crea fila de histórico con motivo

- GIVEN a product with `precio_actual = 1000`
- WHEN `UpdateAsync` is called with `precio_actual = 1200` and `motivo_cambio_precio = "Ajuste Q3"`
- THEN exactly one `producto_precios_historico` row MUST exist with `producto_id = P`, `precio_anterior = 1000`, `precio_nuevo = 1200`, `motivo_cambio_precio = "Ajuste Q3"`, `changed_by` = operator

#### Scenario: Precio sin cambios NO crea fila de histórico

- GIVEN a product with `precio_actual = 1000`
- WHEN `UpdateAsync` is called with `precio_actual = 1000`
- THEN no `producto_precios_historico` row MUST be inserted; `motivo_cambio_precio` MUST be ignored

#### Scenario: Precio anterior cero NO crea fila de histórico

- GIVEN a product with `precio_actual = 0` (creation path)
- WHEN `UpdateAsync` sets `precio_actual = 1500`
- THEN no `producto_precios_historico` row MUST be inserted (avoids creation-noise rows)

### Requirement: DTO de edición con motivo de cambio de precio

`UpdateProductoDto` MUST carry an optional `MotivoCambioPrecio` string (max length 255) used only by the price-history hook.

#### Scenario: Motivo persistido junto al cambio

- GIVEN a valid `UpdateProductoDto` with `MotivoCambioPrecio = "Ajuste Q3"` and a new price
- WHEN `ProductoService.UpdateAsync` runs
- THEN the motive MUST be persisted on the new `producto_precios_historico` row

#### Scenario: Motivo excede 255 caracteres rechaza

- GIVEN a `MotivoCambioPrecio` of 256+ characters
- WHEN `UpdateAsync` is called
- THEN validation MUST reject with a clear error; no `producto_precios_historico` row MUST be inserted
