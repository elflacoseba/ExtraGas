-- =============================================================================
-- 20260828_000003_add_debe_cambiar_password.sql
-- Adds debe_cambiar_password flag to usuarios for forced password change.
-- Issue #90 (mejora #2): recuperacion de contrasena admin-assisted.
--
-- Cuando un admin resetea la password de un usuario, se genera una password
-- temporal y se marca este flag en TRUE. En el siguiente login, el usuario
-- es redirigido a la pantalla de cambio obligatorio hasta que setee su
-- propia contrasena (luego el flag vuelve a FALSE).
--
-- Idempotente: ALTER TABLE ADD COLUMN con guard en information_schema.
-- =============================================================================

USE extragas;

SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'usuarios'
    AND column_name = 'debe_cambiar_password'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE usuarios ADD COLUMN debe_cambiar_password TINYINT(1) NOT NULL DEFAULT 0 AFTER bloqueado_hasta',
  'SELECT "Column debe_cambiar_password ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SELECT 'Column debe_cambiar_password agregada' AS status;
