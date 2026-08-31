-- =============================================================================
-- 20260831_000002_create_pedido_estados_historico.sql
-- Crea la tabla append-only `pedido_estados_historico` para auditoría
-- completa de cambios de estado de pedidos.
--
-- Issue #165: hoy `pedidos` solo guarda `estado_pedido_id` + `updated_at`
-- + `updated_by` + `motivo_cancelacion`. Si un pedido recorre
-- PENDIENTE → CONFIRMADO → EN_PREPARACION → ENTREGADO, el sistema pierde
-- el rastro de los estados intermedios. Para auditoría (¿cuándo se
-- confirmó? ¿cuánto tiempo estuvo en preparación? ¿quién lo canceló y
-- por qué motivo en cada paso?) no hay forma de reconstruirlo.
--
-- Patrón consistente con:
--   - movimientos_garrafa (ADR #2 implícito, modelo append-only).
--   - producto_precios_historico (ADR #18, FKs RESTRICT, sin soft-delete,
--     sin updated_at).
--
-- Decisiones:
--   - Tabla append-only: sin columnas de soft-delete ni updated_at. Borrar
--     una fila sería reescribir la historia.
--   - FKs ON DELETE RESTRICT: no se permite perder histórico borrando un
--     pedido, estado o usuario referenciado. La auditoría es más importante
--     que la limpieza (mismo razonamiento que ADR #18).
--   - estado_anterior_id NULLABLE: una hipotética fila de creación del
--     pedido (estado inicial) tendría NULL porque no hay estado previo.
--     Hoy no se inserta esa fila — la app solo registra TRANSICIONES.
--     El NULL queda permitido para que un eventual seed de migración /
--     import inicial pueda representar el estado inicial sin tocar el
--     schema.
--   - motivo_cancelacion VARCHAR(500) NULL: solo se setea cuando el destino
--     es CANCELADO. En cualquier otra transición es NULL. Coincide con el
--     ancho de pedidos.motivo_cancelacion.
--   - Índice (pedido_id, created_at DESC): cubre la query más frecuente:
--     "SELECT ... WHERE pedido_id = ? ORDER BY created_at DESC", que es
--     la timeline del pedido (Details + endpoint /historial-estados).
--
-- Idempotencia:
--   - CREATE TABLE IF NOT EXISTS cubre el re-run directo.
--   - El índice está DENTRO del CREATE TABLE (no como ALTER posterior),
--     así que es creado atómicamente con la tabla en la primera ejecución
--     y skipped automáticamente en las siguientes.
--   - El nombre del archivo usa 000002 porque 000001 (add_productos_row_version)
--     ya existe en el mismo día.
--   - La capa runner (schema_migrations) skippea si el checksum coincide.
-- =============================================================================

USE extragas;

CREATE TABLE IF NOT EXISTS `pedido_estados_historico` (
  `id`                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `pedido_id`          BIGINT UNSIGNED NOT NULL,
  `estado_anterior_id` BIGINT UNSIGNED NULL,
  `estado_nuevo_id`    BIGINT UNSIGNED NOT NULL,
  `motivo_cancelacion` VARCHAR(500)    NULL,
  `usuario_id`         BIGINT UNSIGNED NULL,
  `created_at`         DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_peh_pedido_created` (`pedido_id`, `created_at` DESC),
  CONSTRAINT `fk_peh_pedido`
    FOREIGN KEY (`pedido_id`) REFERENCES `pedidos` (`id`)
    ON DELETE RESTRICT,
  CONSTRAINT `fk_peh_estado_anterior`
    FOREIGN KEY (`estado_anterior_id`) REFERENCES `estados_pedido` (`id`)
    ON DELETE RESTRICT,
  CONSTRAINT `fk_peh_estado_nuevo`
    FOREIGN KEY (`estado_nuevo_id`) REFERENCES `estados_pedido` (`id`)
    ON DELETE RESTRICT,
  CONSTRAINT `fk_peh_usuario`
    FOREIGN KEY (`usuario_id`) REFERENCES `usuarios` (`id`)
    ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Auditoría append-only de cambios de estado de pedidos (issue #165)';

SELECT 'Tabla pedido_estados_historico lista' AS status;