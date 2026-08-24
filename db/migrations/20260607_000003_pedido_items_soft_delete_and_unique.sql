-- 20260607_000003_pedido_items_soft_delete_and_unique.sql
-- Adds soft-delete column to pedido_items and unique constraint for duplicate prevention.
-- Issue #17: Soft-delete convention compliance.
-- Issue #19: Race condition prevention via unique constraint.

USE extragas;

-- Soft-delete column for pedido_items (AGENTS.md convention #6)
-- Idempotente: ALTER TABLE ADD COLUMN no soporta IF NOT EXISTS, usamos information_schema.
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'pedido_items'
    AND column_name = 'deleted_at'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE pedido_items ADD COLUMN deleted_at DATETIME NULL AFTER observaciones',
  'SELECT "Column deleted_at ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Index for soft-delete queries (idempotente: PREPARE/EXECUTE con check en information_schema)
SET @idx_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'pedido_items'
    AND index_name = 'idx_pedido_items_deleted_at'
);
SET @sql = IF(@idx_exists = 0,
  'CREATE INDEX idx_pedido_items_deleted_at ON pedido_items (deleted_at)',
  'SELECT "Index idx_pedido_items_deleted_at ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Unique constraint to prevent duplicate (pedido_id, producto_id, tipo_linea) among active items.
-- MySQL does not support filtered unique indexes, so we use a generated column + unique index
-- that makes deleted_at NULL rows unique while soft-deleted rows (with a timestamp) are excluded.
-- Idempotente: guard para ADD COLUMN y CREATE UNIQUE INDEX.

SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'pedido_items'
    AND column_name = 'unique_hash'
);
SET @sql = IF(@col_exists = 0,
  "ALTER TABLE pedido_items ADD COLUMN unique_hash VARCHAR(255) GENERATED ALWAYS AS (
     CONCAT(
       CAST(pedido_id AS CHAR), '-',
       CAST(producto_id AS CHAR), '-',
       tipo_linea, '-',
       COALESCE(CAST(deleted_at AS CHAR), '0')
     )
   ) VIRTUAL",
  'SELECT "Column unique_hash ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @idx_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'pedido_items'
    AND index_name = 'uk_pedido_items_pedido_producto_tipo'
);
SET @sql = IF(@idx_exists = 0,
  'CREATE UNIQUE INDEX uk_pedido_items_pedido_producto_tipo ON pedido_items (unique_hash)',
  'SELECT "Index uk_pedido_items_pedido_producto_tipo ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;