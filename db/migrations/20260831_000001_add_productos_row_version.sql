-- =============================================================================
-- 20260831_000001_add_productos_row_version.sql
-- Adds `row_version BINARY(8)` to `productos` for optimistic concurrency
-- (Issue #146.4). Pairs with:
--   - src/ExtraGasMVC/Data/Entities/Producto.cs (RowVersion byte[]?)
--   - src/ExtraGasMVC/Data/Configurations/ProductoConfiguration.cs (IsConcurrencyToken)
--   - src/ExtraGasMVC/Services/Implementations/ProductoService.cs (catches
--     DbUpdateConcurrencyException -> ValidationException)
--
-- MySQL 8.x NO soporta `IsRowVersion()` nativo como SQL Server (no existe el
-- tipo `rowversion`). En su lugar usamos:
--   1. Columna BINARY(8) con DEFAULT 0x00.
--   2. Trigger BEFORE UPDATE que asigna RANDOM_BYTES(8) — nuevo valor distinto
--      en cada UPDATE.
--   3. EF Core marca la columna como IsConcurrencyToken, así que agrega el
--      valor leído al WHERE del UPDATE. Si la fila fue actualizada por otro
--      proceso, el WHERE no matchea y EF lanza DbUpdateConcurrencyException.
--
-- RANDOM_BYTES(8) es nativo de MySQL 8.0+ (la rama soportada en este proyecto,
-- ver db/docs/DECISIONES.md ADR #11). No es único entre updates — no necesita
-- serlo; solo necesita ser DISTINTO del valor que el cliente leyó.
--
-- Idempotente: ALTER TABLE ADD COLUMN + DROP TRIGGER IF EXISTS antes del
-- CREATE TRIGGER. La idempotencia real (skip por checksum) la aplica
-- install.sh vía schema_migrations.
-- =============================================================================

USE extragas;

-- Columna row_version BINARY(8) NOT NULL DEFAULT 0x0000000000000000.
-- El DEFAULT permite INSERT sin especificar row_version (entity nueva en EF).
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'productos'
    AND column_name = 'row_version'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE productos ADD COLUMN row_version BINARY(8) NOT NULL DEFAULT 0x0000000000000000 AFTER deleted_at',
  'SELECT "Column row_version ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Trigger BEFORE UPDATE: asigna un row_version aleatorio en cada UPDATE,
-- garantizando que cualquier cambio de fila invalida el RowVersion que el
-- cliente leyó. Si el cliente quería actualizar con WHERE row_version=X y
-- otro proceso ya cambió el row_version, EF no afecta filas y el Service
-- traduce DbUpdateConcurrencyException a ValidationException con mensaje
-- "fue modificado por otro operador mientras editabas".
DROP TRIGGER IF EXISTS trg_productos_bu_rowversion;

CREATE TRIGGER trg_productos_bu_rowversion
BEFORE UPDATE ON productos
FOR EACH ROW
  SET NEW.row_version = RANDOM_BYTES(8);

SELECT 'productos.row_version + trg_productos_bu_rowversion listos' AS status;
