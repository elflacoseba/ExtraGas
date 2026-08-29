-- 20260829_000001_clientes_dni_unique_soft_delete.sql
-- Convierte idx_clientes_dni en un índice único solo entre clientes activos.
-- Issue #105: el índice UNIQUE original bloqueaba re-registros tras soft-delete.
--
-- Patrón: columna VIRTUAL generada + UNIQUE INDEX sobre esa columna.
-- MySQL no soporta índices parciales nativos (no existe UNIQUE WHERE deleted_at IS NULL),
-- pero sí permite UNIQUE INDEX sobre columnas GENERATED ALWAYS AS (...) VIRTUAL.
-- La columna devuelve NULL cuando deleted_at IS NOT NULL, y MySQL trata múltiples NULLs
-- como distintos en índices UNIQUE, por lo que:
--   - 1 cliente activo por DNI (deleted_at IS NULL, dni_unique = dni)
--   - N soft-deleted por DNI (deleted_at IS NOT NULL, dni_unique = NULL)
--
-- Referencia: ADR #12 documenta el mismo patrón aplicado a pedido_items.unique_hash.

USE extragas;

-- 1. Drop del índice único original (idempotente: guard en information_schema).
SET @idx_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'clientes'
    AND index_name = 'idx_clientes_dni'
);
SET @sql = IF(@idx_exists > 0,
  'DROP INDEX idx_clientes_dni ON clientes',
  'SELECT "Index idx_clientes_dni no existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2. Alta de columna VIRTUAL dni_unique (idempotente: guard en information_schema).
--    Cuando deleted_at IS NULL -> dni_unique = dni (único entre activos).
--    Cuando deleted_at IS NOT NULL -> dni_unique = NULL (múltiples permitidos).
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'clientes'
    AND column_name = 'dni_unique'
);
SET @sql = IF(@col_exists = 0,
  "ALTER TABLE clientes ADD COLUMN dni_unique VARCHAR(15) GENERATED ALWAYS AS (
     CASE WHEN deleted_at IS NULL THEN dni ELSE NULL END
   ) VIRTUAL",
  'SELECT "Column dni_unique ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 3. Creación del UNIQUE INDEX sobre la columna virtual (idempotente: guard).
SET @idx_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'clientes'
    AND index_name = 'idx_clientes_dni_unique'
);
SET @sql = IF(@idx_exists = 0,
  'CREATE UNIQUE INDEX idx_clientes_dni_unique ON clientes (dni_unique)',
  'SELECT "Index idx_clientes_dni_unique ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
