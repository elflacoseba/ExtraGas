-- =============================================================================
-- 20260830_000001_producto_precios_historico.sql
-- Crea la tabla append-only `producto_precios_historico` para auditoría de
-- cambios de precio de productos.
--
-- Issue #145 (Slice 1 — DB foundation). Diseño completo en
-- openspec/changes/issue-145-productos-brechas/design.md §Architecture Decisions.
--
-- Características:
--   - Tabla append-only: sin columnas de soft-delete (no deleted_at) ni
--     updated_at. La app nunca expone Update/Delete de esta entidad.
--   - FKs ON DELETE RESTRICT: no se permite perder histórico borrando un
--     producto o un usuario referenciado. La auditoría es más importante
--     que la limpieza.
--   - Índice (producto_id, changed_at DESC): cubre el caso
--     SELECT ... WHERE producto_id = ? ORDER BY changed_at DESC LIMIT 1
--     que es la query de "último precio" más frecuente.
--   - motivo_cambio_precio VARCHAR(255) NULL: el operador puede dejarlo en
--     blanco; el sistema nunca lo rechaza (spec §"Hook escribe fila solo en
--     cambio real").
--
-- Idempotencia: CREATE TABLE IF NOT EXISTS cubre el caso de re-run directo.
-- La idempotencia real (skip por checksum) la aplica install.sh vía
-- schema_migrations — no hace falta INSERT manual acá.
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `producto_precios_historico` (
  `id`                   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `producto_id`          BIGINT UNSIGNED NOT NULL,
  `precio_anterior`      DECIMAL(12,2)   NOT NULL,
  `precio_nuevo`         DECIMAL(12,2)   NOT NULL,
  `motivo_cambio_precio` VARCHAR(255)    NULL,
  `changed_by`           BIGINT UNSIGNED NULL,
  `changed_at`           DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_pph_producto_changed` (`producto_id`, `changed_at` DESC),
  CONSTRAINT `fk_pph_producto`
    FOREIGN KEY (`producto_id`) REFERENCES `productos` (`id`)
    ON DELETE RESTRICT,
  CONSTRAINT `fk_pph_changed_by`
    FOREIGN KEY (`changed_by`) REFERENCES `usuarios` (`id`)
    ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Auditoría append-only de cambios de precio (issue #145)';

SELECT 'Tabla producto_precios_historico lista' AS status;
