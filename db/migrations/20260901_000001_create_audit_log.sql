-- =============================================================================
-- 20260901_000001_create_audit_log.sql
-- Creates audit_log table: append-only log of field-level changes for any
-- business entity. The product of issue #147 (slice 2).
--
-- Why a single generic table (not per-entity):
--   - One place to query "who changed X for entity Y in window W" across the
--     whole app, without per-module audit table sprawl.
--   - Reusable by future modules (Clientes, Pedidos, etc.) — they just pick
--     their `entidad` value (e.g. 'Producto', 'Cliente') and write rows.
--
-- Schema notes:
--   - `entidad` is a string discriminator, not an FK. Allowing new entity
--     types to use the table without ALTER.
--   - `registro_id` is BIGINT UNSIGNED — matches the convention of every PK
--     in the schema. No FK to the source table (audit log must survive
--     deletion of the source row, and FKs would force ON DELETE behavior
--     that hides evidence).
--   - `user_id` is BIGINT UNSIGNED NULL — nullable for system-initiated
--     changes (backfills, scheduled jobs). No FK to usuarios for the same
--     reason: the audit log must outlive a user delete.
--   - `valor_anterior` / `valor_nuevo` are TEXT — accommodates any serialized
--     value (decimals, bools as "true"/"false", enum codes, JSON snippets).
--   - `changed_at` is the only timestamp; this table is append-only.
--   - NO `created_by`/`updated_by`/`deleted_at` — the table itself is the
--     auditor, doesn't need its own audit trail.
--
-- Indexes:
--   - idx_audit_entidad_registro: covers "all changes for entity X id Y"
--     and "last change for X" (the most common query).
--   - idx_audit_changed_at: covers "changes between T1 and T2" for
--     time-window reports.
--
-- Idempotent: CREATE TABLE IF NOT EXISTS + CREATE INDEX ... (with manual
-- existence guard to stay portable across MySQL versions that don't support
-- CREATE INDEX IF NOT EXISTS).
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `audit_log` (
  `id`              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `entidad`         VARCHAR(50)     NOT NULL,
  `registro_id`     BIGINT UNSIGNED NOT NULL,
  `campo`           VARCHAR(100)    NOT NULL,
  `valor_anterior`  TEXT            NULL,
  `valor_nuevo`     TEXT            NULL,
  `user_id`         BIGINT UNSIGNED NULL,
  `changed_at`      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_audit_entidad_registro` (`entidad`, `registro_id`, `changed_at`),
  KEY `idx_audit_changed_at` (`changed_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Auditoría genérica append-only de cambios por campo';

-- Index guards (CREATE INDEX IF NOT EXISTS no existe en MySQL 8.x — usamos
-- information_schema). El CREATE TABLE ya incluye los KEY, pero si la tabla
-- existía de una corrida previa sin índices, este bloque los crea.

SET @idx1 := (SELECT COUNT(*) FROM information_schema.statistics
              WHERE table_schema = DATABASE()
                AND table_name = 'audit_log'
                AND index_name = 'idx_audit_entidad_registro');
SET @sql := IF(@idx1 = 0,
  'CREATE INDEX `idx_audit_entidad_registro` ON `audit_log` (`entidad`, `registro_id`, `changed_at`)',
  'SELECT "idx_audit_entidad_registro already exists" AS status');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx2 := (SELECT COUNT(*) FROM information_schema.statistics
              WHERE table_schema = DATABASE()
                AND table_name = 'audit_log'
                AND index_name = 'idx_audit_changed_at');
SET @sql := IF(@idx2 = 0,
  'CREATE INDEX `idx_audit_changed_at` ON `audit_log` (`changed_at`)',
  'SELECT "idx_audit_changed_at already exists" AS status');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SELECT 'Tabla audit_log lista' AS status;
