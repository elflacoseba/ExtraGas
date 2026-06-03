-- =============================================================================
-- 20260101_000001_create_extragas_database.sql
-- Crea la base de datos del sistema ExtraGas.
--
-- Este script es idempotente: si la BD ya existe, la deja como está.
-- El DROP/CREATE destructivo se hace desde db/scripts/install.sh o reset.sh.
-- =============================================================================

CREATE DATABASE IF NOT EXISTS extragas
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

-- Time zone por defecto a nivel de servidor (opcional, recomendado).
-- Si la conexión no fija time_zone, MySQL usará el del sistema.
SET GLOBAL time_zone = '-03:00';

USE extragas;

-- Confirmación
SELECT 'Base de datos extragas lista' AS status, @@character_set_database AS charset, @@collation_database AS collation;
