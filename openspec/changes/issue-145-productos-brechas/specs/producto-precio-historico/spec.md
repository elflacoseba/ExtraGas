# Producto Precio Historico Specification

## Purpose

Append-only audit log of every price change per `producto`. Persisted by hook in `ProductoService.UpdateAsync`. Used for price-history queries and auditability.

## Requirements

### Requirement: Schema de producto_precios_historico

The system MUST persist a `producto_precios_historico` table with `id`, `producto_id` (FK `productos.id`), `precio_anterior DECIMAL(12,2)`, `precio_nuevo DECIMAL(12,2)`, `motivo_cambio_precio VARCHAR(255) NULL`, `changed_by` (FK operator EmpleadoId), `changed_at` (auto timestamp). No soft-delete columns: the table is append-only.

#### Scenario: Migración idempotente crea tabla si no existe

- GIVEN `producto_precios_historico` does NOT exist
- WHEN the SQL migration runs (idempotent `CREATE TABLE IF NOT EXISTS` pattern)
- THEN the table MUST exist with all required columns, FKs, and an index on `(producto_id, changed_at DESC)`

#### Scenario: Re-correr migración es no-op

- GIVEN `producto_precios_historico` already exists
- WHEN the SQL migration runs again
- THEN no DDL changes occur (`schema_migrations` skip-by-checksum enforces this)

### Requirement: Log append-only, sin UPDATE ni DELETE

The system MUST treat `producto_precios_historico` as append-only: no `UpdateAsync`/`DeleteAsync` exposed in services, no UI edit affordance, no soft-delete columns.

#### Scenario: No existe operación de edición

- GIVEN the service surface
- WHEN a developer searches for update/delete methods on `producto_precios_historico`
- THEN none MUST exist

### Requirement: Hook escribe fila solo en cambio real

The system MUST insert one `producto_precios_historico` row from `ProductoService.UpdateAsync` when `precio_anterior != precio_nuevo && precio_anterior != 0`.

#### Scenario: Cambio real registra fila

- GIVEN prior `precio_actual = 1000`, new `precio_actual = 1200`
- WHEN `UpdateAsync` commits
- THEN exactly one row exists with `precio_anterior=1000`, `precio_nuevo=1200`, `motivo_cambio_precio` from DTO, `changed_by` = operator

#### Scenario: Sin cambio real no registra fila

- GIVEN prior `precio_actual = 1000`, new `precio_actual = 1000` (or prior was 0)
- WHEN `UpdateAsync` commits
- THEN no row is inserted; `motivo_cambio_precio` is ignored

### Requirement: Queries de auditoría

The system MUST support audit queries: latest price per product, full history per product ordered by `changed_at DESC`, last change-by-user.

#### Scenario: Última fila por producto

- GIVEN a product with N history rows
- WHEN querying `SELECT * FROM producto_precios_historico WHERE producto_id = P ORDER BY changed_at DESC LIMIT 1`
- THEN the latest row is returned with actor and motive

#### Scenario: Histórico completo ordenado

- GIVEN a product with 5 history rows
- WHEN querying the full history ordered by `changed_at DESC`
- THEN all 5 rows are returned in descending time order
