-- =============================================================================
-- 20260102_000003_create_productos.sql
-- Crea la tabla de productos y catálogo de tipos.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- productos: catálogo vendible
--   - GAS-10/15/45: garrafas, requieren tracking individual (maneja_garrafa_individual = TRUE)
--   - CAR-3/5/10/25: bolsas de carbón
--   - LEN-25: bolsa de leña
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS productos (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  codigo VARCHAR(30) NOT NULL,
  nombre VARCHAR(150) NOT NULL,
  descripcion VARCHAR(255) NULL,
  tipo_producto_id BIGINT UNSIGNED NOT NULL,
  capacidad_kg DECIMAL(8,2) NULL,
  unidad_venta VARCHAR(20) NOT NULL DEFAULT 'UNIDAD',
  precio_actual DECIMAL(12,2) NOT NULL DEFAULT 0,
  maneja_garrafa_individual BOOLEAN NOT NULL DEFAULT FALSE,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT uq_productos_codigo UNIQUE (codigo),
  CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
  CONSTRAINT fk_productos_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_productos_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  CONSTRAINT chk_productos_precio CHECK (precio_actual >= 0),
  INDEX idx_productos_tipo (tipo_producto_id),
  INDEX idx_productos_codigo_nombre (codigo, nombre),
  INDEX idx_productos_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT 'Productos creados' AS status;
