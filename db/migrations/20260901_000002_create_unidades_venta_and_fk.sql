-- =============================================================================
-- 20260901_000002_create_unidades_venta_and_fk.sql
-- Crea la lookup table `unidades_venta` y la FK desde `productos`.
-- Issue #147 slice 3 item 7.
--
-- Contexto: el campo legacy `productos.unidad_venta VARCHAR(20)` es texto
-- libre. La consistencia del dropdown de Create/Edit depende de que el
-- operador tipee exactamente el mismo string que el seed original
-- (UNIDAD/GARRAFA/BOLSA/KG). Cualquier typo ("unidad" con minúscula, "bols")
-- rompe la UX y el inventario porque no hay FK que lo valide.
--
-- Decisión (issue #147 spec scenario "Catálogo cerrado de unidades_venta"):
-- 1. Crear lookup `unidades_venta` con 4 valores canónicos seed-only.
-- 2. Agregar `unidad_venta_id BIGINT UNSIGNED NULL` a `productos`.
-- 3. Backfill: `UPDATE productos JOIN unidades_venta` para resolver el
--    VARCHAR legacy al FK nuevo (preserva los datos existentes sin pérdida).
-- 4. Crear FK `fk_productos_unidad_venta` con `ON DELETE RESTRICT` (mismo
--    patrón que `fk_productos_tipo`): si alguien intenta borrar una
--    unidad_venta referenciada, la operación falla. Evita huérfanos.
-- 5. Crear índice `idx_productos_unidad_venta_id` para acelerar JOINs.
--
-- NO se hace `DROP COLUMN unidad_venta VARCHAR(20)` en esta migración —
-- decisión documentada en design.md "Open Questions" / "Slice 3 item 7":
-- el DROP se difiere a una migración cleanup separada (ADR #12 pattern,
-- expand-contract) para que el deploy de la app pueda coexistir con la
-- BD en cualquier orden durante la ventana de transición. La app lee
-- `UnidadVentaId` (FK) si está populado y cae al `UnidadVenta` (string)
-- como fallback.
--
-- Idempotencia: cada step usa guards `information_schema` + `PREPARE`/
-- `EXECUTE` para que re-ejecuciones (manual, retry) no fallen. `INSERT
-- IGNORE` para el seed.
--
-- MySQL 8.x: `AllowUserVariables=true` requerido en la connection string
-- para el patrón PREPARE/EXECUTE. El install.sh usa CLI `mysql` que
-- no tiene esta restricción; los tests de integración sí deben setearlo
-- en la fixture (ver UnidadesVentaMySqlFixture.GetConnectionString).
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- Step 1: Crear la lookup table `unidades_venta`.
-- Réplica del shape de `tipos_producto` (ver migración 20260102_000003 y ADR
-- #4 sobre catálogos-en-lugar-de-ENUM). Incluye audit cols y soft delete
-- para mantener consistencia con el resto del schema.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `unidades_venta` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `codigo`      VARCHAR(20)     NOT NULL,
  `nombre`      VARCHAR(50)     NOT NULL,
  `activo`      TINYINT(1)      NOT NULL DEFAULT 1,
  `created_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`  BIGINT UNSIGNED NULL,
  `updated_by`  BIGINT UNSIGNED NULL,
  `deleted_at`  DATETIME        NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_unidades_venta_codigo` (`codigo`),
  KEY `idx_unidades_venta_activo` (`activo`, `deleted_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catálogo cerrado de unidades de venta (UNIDAD/GARRAFA/BOLSA/KG). Issue #147 slice 3.';

-- -----------------------------------------------------------------------------
-- Step 2: Seed idempotente de los 4 valores canónicos. Orden importante:
-- corre ANTES del ALTER TABLE productos para que el backfill del step 4
-- pueda hacer JOIN contra estas filas. `INSERT IGNORE` descarta solo el
-- error de duplicate-key, no aborta.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO `unidades_venta` (`codigo`, `nombre`) VALUES
  ('UNIDAD',  'Unidad'),
  ('GARRAFA', 'Garrafa'),
  ('BOLSA',   'Bolsa'),
  ('KG',      'Kilogramo');

-- -----------------------------------------------------------------------------
-- Step 3: Agregar `unidad_venta_id BIGINT UNSIGNED NULL` a `productos`.
-- Idempotente: el guard `information_schema.COLUMNS` evita el error
-- "Duplicate column name" en re-ejecuciones. La columna arranca NULL para
-- no romper inserts existentes durante la ventana de transición.
-- -----------------------------------------------------------------------------
SET @col_exists := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'productos' AND COLUMN_NAME = 'unidad_venta_id');
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `productos` ADD COLUMN `unidad_venta_id` BIGINT UNSIGNED NULL AFTER `unidad_venta`',
  'SELECT "columna unidad_venta_id ya existe" AS status');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- Step 4: Backfill. Mapea el VARCHAR legacy al FK. Solo para filas donde
-- unidad_venta_id es NULL (evita pisar backfills previos si se re-ejecuta).
-- -----------------------------------------------------------------------------
UPDATE `productos` p
JOIN `unidades_venta` u ON u.codigo = p.`unidad_venta`
SET p.`unidad_venta_id` = u.`id`
WHERE p.`unidad_venta_id` IS NULL AND p.`unidad_venta` IS NOT NULL;

-- -----------------------------------------------------------------------------
-- Step 5: FK constraint `fk_productos_unidad_venta`. Idempotente: si ya
-- existe, primero la dropeamos para que el ADD CONSTRAINT no falle por
-- nombre duplicado. ON DELETE RESTRICT (mismo que fk_productos_tipo):
-- protege contra bajas accidentales de unidades referenciadas.
-- -----------------------------------------------------------------------------
SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'productos' AND CONSTRAINT_NAME = 'fk_productos_unidad_venta');
SET @sql := IF(@fk_exists > 0,
  'ALTER TABLE `productos` DROP FOREIGN KEY `fk_productos_unidad_venta`',
  'SELECT "fk_productos_unidad_venta no existe, OK para crear" AS status');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

ALTER TABLE `productos`
  ADD CONSTRAINT `fk_productos_unidad_venta`
  FOREIGN KEY (`unidad_venta_id`) REFERENCES `unidades_venta` (`id`);

-- -----------------------------------------------------------------------------
-- Step 6: Índice sobre la FK para que los JOINs con unidades_venta (vía
-- navigation property) sean rápidos. Idempotente vía information_schema.
-- -----------------------------------------------------------------------------
SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'productos' AND INDEX_NAME = 'idx_productos_unidad_venta_id');
SET @sql := IF(@idx_exists = 0,
  'CREATE INDEX `idx_productos_unidad_venta_id` ON `productos` (`unidad_venta_id`)',
  'SELECT "idx_productos_unidad_venta_id ya existe" AS status');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- NOTA: el DROP COLUMN de `productos.unidad_venta VARCHAR(20)` se difiere
-- a una migración cleanup separada (issue #147 design.md "Open Questions").
-- Mientras convivan ambas columnas, la app lee `unidad_venta_id` (FK) si
-- está populado y cae a `unidad_venta` (string) como fallback. Eliminar la
-- columna legacy después de confirmar que la app lee siempre del FK.
-- -----------------------------------------------------------------------------

SELECT 'Tabla unidades_venta creada y FK aplicada' AS status;
