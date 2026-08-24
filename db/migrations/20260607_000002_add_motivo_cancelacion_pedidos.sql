-- Migración: agregar columna motivo_cancelacion a pedidos
-- Fecha: 2026-06-07
-- Descripción: permite registrar el motivo de cancelación de un pedido
--              cuando se transiciona al estado CANCELADO.

USE extragas;

-- Idempotente: ALTER TABLE ADD COLUMN no soporta IF NOT EXISTS en MySQL 8.x,
-- usamos information_schema para detectar y saltar si ya existe.
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'pedidos'
    AND column_name = 'motivo_cancelacion'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE pedidos ADD COLUMN motivo_cancelacion VARCHAR(500) NULL AFTER observaciones',
  'SELECT "Column motivo_cancelacion ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
