-- =============================================================================
-- 20260828_000004_create_password_reset_tokens.sql
-- Creates password_reset_tokens table for self-service password recovery.
-- Change: password-recovery (SDD), PR1 schema work unit.
--
-- - Solo se persiste SHA-256 hex del token raw; el token raw viaja solo por email.
-- - Single-use enforced at consume-time via atomic UPDATE ... WHERE used_at IS NULL
--   AND expires_at > NOW(), con uk_token_hash serializando via row-lock de InnoDB.
-- - FK a usuarios con ON DELETE CASCADE: un reset token sin usuario no tiene sentido
--   y la eliminacion del usuario limpia sus tokens pendientes.
-- - created_by / updated_by NULL: la creacion es anonima (el solicitante no esta
--   autenticado); sigue convencion de auditoria del proyecto.
-- - Idempotente: la tabla se crea solo si no existe.
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `password_reset_tokens` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `usuario_id`  BIGINT UNSIGNED NOT NULL,
  `token_hash`  VARCHAR(64)     NOT NULL COMMENT 'SHA-256 hex del token raw; el token raw nunca se persiste',
  `ip_address`  VARCHAR(45)     NULL     COMMENT 'IPv4 o IPv6 del solicitante',
  `user_agent`  VARCHAR(500)    NULL,
  `expires_at`  DATETIME        NOT NULL COMMENT 'UTC; vence y se ignora en consume',
  `used_at`     DATETIME        NULL     COMMENT 'NULL=sin usar; se setea al consumir exitosamente',
  `created_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`  BIGINT UNSIGNED NULL     COMMENT 'NULL: anonimo (solicitante no autenticado)',
  `updated_by`  BIGINT UNSIGNED NULL     COMMENT 'NULL: anonimo',
  `deleted_at`  DATETIME        NULL     COMMENT 'soft delete; nunca DELETE de filas',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_token_hash` (`token_hash`),
  KEY `idx_usuario_used` (`usuario_id`, `used_at`),
  KEY `idx_expires_at` (`expires_at`),
  CONSTRAINT `fk_password_reset_tokens_usuario`
    FOREIGN KEY (`usuario_id`) REFERENCES `usuarios` (`id`)
    ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Tokens de un solo uso para reset de contrasena; solo SHA-256 se persiste';

SELECT 'Tabla password_reset_tokens lista' AS status;
