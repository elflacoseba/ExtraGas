-- =============================================================================
-- 20260824_000001_create_schema_migrations.sql
-- Crea la tabla `schema_migrations` para registrar cada archivo .sql aplicado,
-- con su checksum SHA256.
--
-- Habilita idempotencia real en db/scripts/install.sh:
--   - Si filename + checksum están registrados → skip (no ejecuta el archivo).
--   - Si filename no está registrado → ejecutar y registrar.
--   - Si filename está registrado con checksum distinto → ERROR (drift).
--
-- Esta tabla es esquema del proyecto, pero NO depende de ningún trigger ni
-- del dominio: los INSERT los hace install.sh, no otra migración.
-- Si install.sh corre contra una BD sin esta tabla, también la crea como
-- bootstrap defensivo (CREATE TABLE IF NOT EXISTS antes del loop).
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `schema_migrations` (
  `filename`    VARCHAR(255) NOT NULL,
  `checksum`    CHAR(64)     NOT NULL,
  `applied_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`filename`),
  KEY `idx_schema_migrations_applied_at` (`applied_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Registro de migraciones aplicadas (idempotencia real)';

SELECT 'Tabla schema_migrations lista' AS status;
