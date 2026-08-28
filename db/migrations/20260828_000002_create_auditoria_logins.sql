-- =============================================================================
-- 20260828_000002_create_auditoria_logins.sql
-- Creates auditoria_logins table to log every login attempt (successful or not).
-- Issue #90 (mejora #4): auditoria de intentos de login.
--
-- Permite detectar ataques de fuerza bruta y auditar accesos historicos.
-- Se registra TODO intento, exista el usuario o no, con username_intentado
-- para mantener trazabilidad incluso de intentos a usuarios inexistentes.
--
-- - motivo_fallo: codigo de LoginFailureReason (None, UserNotFound, UserInactive,
--   UserDeleted, InvalidPassword, LockedOut). NULL cuando exito = TRUE.
-- - FK a usuarios con ON DELETE SET NULL: si se elimina un usuario se mantiene
--   la fila de auditoria (no se pierde evidencia historica).
-- - ip_origen VARCHAR(45): alcance para IPv4 + IPv6 (max 45 chars).
-- - Idempotente: la tabla se crea solo si no existe.
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `auditoria_logins` (
  `id`                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `username_intentado` VARCHAR(50)     NOT NULL,
  `usuario_id`         BIGINT UNSIGNED NULL,
  `exito`              TINYINT(1)      NOT NULL,
  `motivo_fallo`       VARCHAR(20)     NULL,
  `ip_origen`          VARCHAR(45)     NULL,
  `user_agent`         VARCHAR(255)    NULL,
  `created_at`         DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_auditoria_logins_created_at` (`created_at`),
  KEY `idx_auditoria_logins_usuario_id` (`usuario_id`),
  KEY `idx_auditoria_logins_ip_origen` (`ip_origen`),
  CONSTRAINT `fk_auditoria_logins_usuario`
    FOREIGN KEY (`usuario_id`) REFERENCES `usuarios` (`id`)
    ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Registro de intentos de login (exitosos y fallidos)';

SELECT 'Tabla auditoria_logins lista' AS status;
