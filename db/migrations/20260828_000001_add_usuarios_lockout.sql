-- =============================================================================
-- 20260828_000001_add_usuarios_lockout.sql
-- Adds lockout columns to `usuarios` for failed-login protection.
-- Issue #90 (mejora #1): lockout por intentos fallidos.
--
-- - intentos_fallidos: SMALLINT UNSIGNED, contador de intentos fallidos
--   consecutivos desde el último login exitoso (o desde el reset).
-- - bloqueado_hasta: DATETIME NULL, fecha hasta la cual el usuario está
--   bloqueado. NULL = no bloqueado.
--
-- Idempotente: ALTER TABLE ADD COLUMN no soporta IF NOT EXISTS, usamos
-- information_schema + PREPARE/EXECUTE (mismo patrón que otras migraciones).
-- =============================================================================

USE extragas;

-- Columna intentos_fallidos
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'usuarios'
    AND column_name = 'intentos_fallidos'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE usuarios ADD COLUMN intentos_fallidos SMALLINT UNSIGNED NOT NULL DEFAULT 0 AFTER ultimo_login',
  'SELECT "Column intentos_fallidos ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Columna bloqueado_hasta
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'usuarios'
    AND column_name = 'bloqueado_hasta'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE usuarios ADD COLUMN bloqueado_hasta DATETIME NULL AFTER intentos_fallidos',
  'SELECT "Column bloqueado_hasta ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SELECT 'Lockout columns agregadas a usuarios' AS status;
