-- 20260607_000002_pedido_items_soft_delete_and_unique.sql
-- Adds soft-delete column to pedido_items and unique constraint for duplicate prevention.
-- Issue #17: Soft-delete convention compliance.
-- Issue #19: Race condition prevention via unique constraint.

USE extragas;

-- Soft-delete column for pedido_items (AGENTS.md convention #6)
ALTER TABLE pedido_items
    ADD COLUMN deleted_at DATETIME NULL AFTER observaciones;

-- Index for soft-delete queries
CREATE INDEX idx_pedido_items_deleted_at ON pedido_items (deleted_at);

-- Unique constraint to prevent duplicate (pedido_id, producto_id, tipo_linea) among active items.
-- MySQL does not support filtered unique indexes, so we use a generated column + unique index
-- that makes deleted_at NULL rows unique while soft-deleted rows (with a timestamp) are excluded.
ALTER TABLE pedido_items
    ADD COLUMN unique_hash VARCHAR(64) GENERATED ALWAYS AS (
        CASE WHEN deleted_at IS NULL THEN
            CONCAT(CAST(pedido_id AS CHAR), '-', CAST(producto_id AS CHAR), '-', tipo_linea)
        ELSE
            CONCAT(CAST(pedido_id AS CHAR), '-', CAST(producto_id AS CHAR), '-', tipo_linea, '-', CAST(deleted_at AS CHAR))
        END
    ) STORED;

CREATE UNIQUE INDEX uk_pedido_items_pedido_producto_tipo ON pedido_items (unique_hash);