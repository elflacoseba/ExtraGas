-- =============================================================================
-- 20260102_000005_create_garrafas.sql
-- Crea las tablas de garrafas (tracking individual) y sus movimientos.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- garrafas: cada garrafa física como activo rastreable
--   - codigo: identificador único (troquel/serial físico, VARCHAR(50))
--   - capacidad_kg: 10, 15 o 45 (CHECK)
--   - estado_garrafa_id: estado actual
--   - cliente_id: solo cuando el estado es EN_CLIENTE
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS garrafas (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  codigo VARCHAR(50) NOT NULL,
  capacidad_kg TINYINT UNSIGNED NOT NULL,
  proveedor_id BIGINT UNSIGNED NULL,
  recepcion_id BIGINT UNSIGNED NULL,
  fecha_compra DATE NOT NULL,
  estado_garrafa_id BIGINT UNSIGNED NOT NULL,
  cliente_id BIGINT UNSIGNED NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  fecha_ultimo_movimiento DATETIME NULL,
  observaciones TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT uq_garrafas_codigo UNIQUE (codigo),
  CONSTRAINT chk_garrafas_capacidad CHECK (capacidad_kg IN (10, 15, 45)),
  CONSTRAINT fk_garrafas_proveedor FOREIGN KEY (proveedor_id) REFERENCES proveedores(id),
  CONSTRAINT fk_garrafas_recepcion FOREIGN KEY (recepcion_id) REFERENCES recepciones_proveedor(id),
  CONSTRAINT fk_garrafas_estado FOREIGN KEY (estado_garrafa_id) REFERENCES estados_garrafa(id),
  CONSTRAINT fk_garrafas_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
  CONSTRAINT fk_garrafas_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_garrafas_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  INDEX idx_garrafas_estado (estado_garrafa_id),
  INDEX idx_garrafas_cliente (cliente_id),
  INDEX idx_garrafas_capacidad (capacidad_kg),
  INDEX idx_garrafas_recepcion (recepcion_id),
  INDEX idx_garrafas_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- movimientos_garrafa: log inmutable de cambios de estado
--   - pedido_id: NULL si no está asociado a un pedido
--   - recepcion_id: NULL si no está asociado a una recepción
--   - estado_origen_id, estado_destino_id: para trazabilidad
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS movimientos_garrafa (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  garrafa_id BIGINT UNSIGNED NOT NULL,
  fecha DATETIME NOT NULL,
  tipo_movimiento_id BIGINT UNSIGNED NOT NULL,
  pedido_id BIGINT UNSIGNED NULL,
  recepcion_id BIGINT UNSIGNED NULL,
  cliente_id BIGINT UNSIGNED NULL,
  estado_origen_id BIGINT UNSIGNED NULL,
  estado_destino_id BIGINT UNSIGNED NOT NULL,
  empleado_id BIGINT UNSIGNED NULL,
  observaciones TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  CONSTRAINT fk_mov_garrafa_garrafa FOREIGN KEY (garrafa_id) REFERENCES garrafas(id),
  CONSTRAINT fk_mov_garrafa_tipo FOREIGN KEY (tipo_movimiento_id) REFERENCES tipos_movimiento_garrafa(id),
  CONSTRAINT fk_mov_garrafa_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
  CONSTRAINT fk_mov_garrafa_recepcion FOREIGN KEY (recepcion_id) REFERENCES recepciones_proveedor(id),
  CONSTRAINT fk_mov_garrafa_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
  CONSTRAINT fk_mov_garrafa_estado_origen FOREIGN KEY (estado_origen_id) REFERENCES estados_garrafa(id),
  CONSTRAINT fk_mov_garrafa_estado_destino FOREIGN KEY (estado_destino_id) REFERENCES estados_garrafa(id),
  CONSTRAINT fk_mov_garrafa_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
  CONSTRAINT fk_mov_garrafa_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  INDEX idx_mov_garrafa_garrafa (garrafa_id, fecha),
  INDEX idx_mov_garrafa_fecha (fecha),
  INDEX idx_mov_garrafa_pedido (pedido_id),
  INDEX idx_mov_garrafa_recepcion (recepcion_id),
  INDEX idx_mov_garrafa_tipo (tipo_movimiento_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT 'Garrafas y movimientos creados' AS status;
