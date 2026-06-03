-- =============================================================================
-- 20260102_000006_create_proveedores_y_recepciones.sql
-- Crea las tablas de recepciones de proveedor y pagos a proveedores.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- recepciones_proveedor: cabecera de la recepción
--   - numero: REC-PROV-YYYY-NNNNN (lo genera trigger)
--   - numero_factura_proveedor: opcional
--   - subtotal, descuento, total: DECIMAL(12,2)
--   - monto_pagado: lo mantiene trigger AFTER pagos_proveedor
--   - saldo: columna generada
-- -----------------------------------------------------------------------------
CREATE TABLE recepciones_proveedor (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  numero VARCHAR(20) NULL,
  fecha DATETIME NOT NULL,
  proveedor_id BIGINT UNSIGNED NOT NULL,
  empleado_id BIGINT UNSIGNED NOT NULL,
  numero_factura_proveedor VARCHAR(50) NULL,
  subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
  descuento DECIMAL(12,2) NOT NULL DEFAULT 0,
  total DECIMAL(12,2) NOT NULL DEFAULT 0,
  monto_pagado DECIMAL(12,2) NOT NULL DEFAULT 0,
  saldo DECIMAL(12,2) GENERATED ALWAYS AS (total - monto_pagado) STORED,
  observaciones TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT fk_recepciones_proveedor FOREIGN KEY (proveedor_id) REFERENCES proveedores(id),
  CONSTRAINT fk_recepciones_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
  CONSTRAINT fk_recepciones_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_recepciones_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  CONSTRAINT chk_recepciones_total CHECK (total >= 0),
  INDEX idx_recepciones_numero (numero),
  INDEX idx_recepciones_proveedor (proveedor_id, fecha),
  INDEX idx_recepciones_fecha (fecha),
  INDEX idx_recepciones_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- recepcion_items: líneas de la recepción (uno por producto recibido)
--   - subtotal: columna generada = cantidad * precio_unitario
-- -----------------------------------------------------------------------------
CREATE TABLE recepcion_items (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  recepcion_id BIGINT UNSIGNED NOT NULL,
  producto_id BIGINT UNSIGNED NOT NULL,
  cantidad DECIMAL(10,2) NOT NULL,
  precio_unitario DECIMAL(12,2) NOT NULL,
  subtotal DECIMAL(12,2) GENERATED ALWAYS AS (cantidad * precio_unitario) STORED,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  CONSTRAINT fk_recepcion_items_recepcion FOREIGN KEY (recepcion_id) REFERENCES recepciones_proveedor(id) ON DELETE CASCADE,
  CONSTRAINT fk_recepcion_items_producto FOREIGN KEY (producto_id) REFERENCES productos(id),
  CONSTRAINT chk_recepcion_items_cantidad CHECK (cantidad > 0),
  CONSTRAINT chk_recepcion_items_precio CHECK (precio_unitario >= 0),
  INDEX idx_recepcion_items_recepcion (recepcion_id),
  INDEX idx_recepcion_items_producto (producto_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- pagos_proveedor: pagos realizados a proveedores
--   - numero: PAG-PROV-YYYY-NNNNN (lo genera trigger)
--   - recepcion_id: NULL permitido = "pago a cuenta" del proveedor
--   - referencia: para transferencias
-- -----------------------------------------------------------------------------
CREATE TABLE pagos_proveedor (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  numero VARCHAR(20) NULL,
  fecha DATETIME NOT NULL,
  proveedor_id BIGINT UNSIGNED NOT NULL,
  recepcion_id BIGINT UNSIGNED NULL,
  forma_pago_id BIGINT UNSIGNED NOT NULL,
  monto DECIMAL(12,2) NOT NULL,
  referencia VARCHAR(100) NULL,
  observaciones VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT fk_pagos_proveedor_proveedor FOREIGN KEY (proveedor_id) REFERENCES proveedores(id),
  CONSTRAINT fk_pagos_proveedor_recepcion FOREIGN KEY (recepcion_id) REFERENCES recepciones_proveedor(id),
  CONSTRAINT fk_pagos_proveedor_forma FOREIGN KEY (forma_pago_id) REFERENCES formas_pago(id),
  CONSTRAINT fk_pagos_proveedor_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_pagos_proveedor_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  CONSTRAINT chk_pagos_proveedor_monto CHECK (monto > 0),
  INDEX idx_pagos_proveedor_numero (numero),
  INDEX idx_pagos_proveedor_proveedor (proveedor_id, fecha),
  INDEX idx_pagos_proveedor_recepcion (recepcion_id),
  INDEX idx_pagos_proveedor_forma (forma_pago_id, fecha),
  INDEX idx_pagos_proveedor_fecha (fecha),
  INDEX idx_pagos_proveedor_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT 'Recepciones y pagos proveedor creados' AS status;
