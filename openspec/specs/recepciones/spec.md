# Delta for recepciones

> First-time capability. The main spec (`openspec/specs/recepciones/spec.md`) will be created from this delta on archive. `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Dropdown de productos excluye inactivos

The system MUST load only products with `activo = true AND deleted_at IS NULL` when populating the product dropdown for `Recepciones/Create` and `Recepciones/Edit`.

#### Scenario: Solo productos activos aparecen en el dropdown

- GIVEN 3 active products + 2 soft-deleted products + 1 product with `activo = false`
- WHEN `RecepcionService.LoadProductosByIdAsync` runs
- THEN only the 3 active products MUST be returned; the inactive and soft-deleted ones MUST be excluded

#### Scenario: Producto desactivado no es seleccionable

- GIVEN a product P was active when the operator opened `Recepciones/Create`, then deactivated before the form submitted
- WHEN the operator submits with `producto_id = P`
- THEN the form MUST surface a validation error identifying P as inactive; nothing persists

### Requirement: Validación pre-commit bloquea productos desactivados

The system MUST reject a recepción submit when any `recepcion_item.producto_id` resolves to `activo = false OR deleted_at IS NOT NULL` at submit time.

#### Scenario: Item con producto desactivado rechaza antes de persistir

- GIVEN a recepción with 2 items, one referencing an active product, the other a deactivated product
- WHEN the operator submits
- THEN validation MUST throw `InvalidOperationException` naming the deactivated product; zero `recepciones_proveedor`, `recepcion_items`, `garrafas`, or `movimientos_garrafa` MUST persist
