-- =============================================================================
-- 20260102_000004_create_pedidos_y_pagos.sql
-- Crea las tablas de pedidos, líneas, pagos y catálogos relacionados.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- pedidos: cabecera del pedido
--   - numero: PED-YYYY-NNNNN (lo genera trigger desde secuencias)
--   - subtotal, descuento, total: DECIMAL(12,2)
--   - monto_pagado: lo mantiene trigger AFTER pagos
--   - saldo: columna generada = total - monto_pagado
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pedidos (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  numero VARCHAR(20) NULL,
  fecha DATETIME NOT NULL,
  fecha_entrega DATETIME NULL,
  entregado BOOLEAN NOT NULL DEFAULT FALSE,
  cliente_id BIGINT UNSIGNED NOT NULL,
  empleado_id BIGINT UNSIGNED NOT NULL,
  estado_pedido_id BIGINT UNSIGNED NOT NULL,
  canal_venta_id BIGINT UNSIGNED NOT NULL,
  medio_contacto_id BIGINT UNSIGNED NULL,
  subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
  descuento DECIMAL(12,2) NOT NULL DEFAULT 0,
  total DECIMAL(12,2) NOT NULL DEFAULT 0,
  monto_pagado DECIMAL(12,2) NOT NULL DEFAULT 0,
  saldo DECIMAL(12,2) GENERATED ALWAYS AS (total - monto_pagado) STORED,
  observaciones TEXT NULL,
  direccion_entrega VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT fk_pedidos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
  CONSTRAINT fk_pedidos_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
  CONSTRAINT fk_pedidos_estado FOREIGN KEY (estado_pedido_id) REFERENCES estados_pedido(id),
  CONSTRAINT fk_pedidos_canal FOREIGN KEY (canal_venta_id) REFERENCES canales_venta(id),
  CONSTRAINT fk_pedidos_medio_contacto FOREIGN KEY (medio_contacto_id) REFERENCES medios_contacto_pedido(id),
  CONSTRAINT fk_pedidos_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_pedidos_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  CONSTRAINT chk_pedidos_total CHECK (total >= 0),
  CONSTRAINT chk_pedidos_monto_pagado CHECK (monto_pagado >= 0),
  INDEX idx_pedidos_numero (numero),
  INDEX idx_pedidos_cliente (cliente_id, fecha),
  INDEX idx_pedidos_fecha (fecha),
  INDEX idx_pedidos_estado (estado_pedido_id),
  INDEX idx_pedidos_empleado (empleado_id, fecha),
  INDEX idx_pedidos_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- pedido_items: líneas del pedido
--   - tipo_linea:
--       ENTREGA: garrafa llena al cliente (precio positivo)
--       DEVOLUCION: garrafa vacía que el cliente devuelve (precio positivo, pero se descuenta al total del pedido vía lógica de app)
--       VENTA: carbón, leña, otros (precio positivo)
--   - subtotal: columna generada = cantidad * precio_unitario
--   - precio_unitario se congela al momento de crear el pedido (histórico)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pedido_items (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  pedido_id BIGINT UNSIGNED NOT NULL,
  producto_id BIGINT UNSIGNED NOT NULL,
  tipo_linea ENUM('ENTREGA','DEVOLUCION','VENTA') NOT NULL DEFAULT 'VENTA',
  cantidad DECIMAL(10,2) NOT NULL,
  precio_unitario DECIMAL(12,2) NOT NULL,
  subtotal DECIMAL(12,2) GENERATED ALWAYS AS (cantidad * precio_unitario) STORED,
  observaciones VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  CONSTRAINT fk_pedido_items_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id) ON DELETE CASCADE,
  CONSTRAINT fk_pedido_items_producto FOREIGN KEY (producto_id) REFERENCES productos(id),
  CONSTRAINT chk_pedido_items_cantidad CHECK (cantidad > 0),
  CONSTRAINT chk_pedido_items_precio CHECK (precio_unitario >= 0),
  INDEX idx_pedido_items_pedido (pedido_id),
  INDEX idx_pedido_items_producto (producto_id),
  INDEX idx_pedido_items_tipo (tipo_linea)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- pagos: cobros a clientes
--   - numero_recibo: REC-YYYY-NNNNN (lo genera trigger)
--   - pedido_id: NULL permitido = "pago a cuenta" (se aplica al pedido más antiguo con saldo del cliente)
--   - referencia: para transferencias, número de operación
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pagos (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  numero_recibo VARCHAR(20) NULL,
  fecha DATETIME NOT NULL,
  cliente_id BIGINT UNSIGNED NOT NULL,
  pedido_id BIGINT UNSIGNED NULL,
  forma_pago_id BIGINT UNSIGNED NOT NULL,
  monto DECIMAL(12,2) NOT NULL,
  referencia VARCHAR(100) NULL,
  observaciones VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT fk_pagos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
  CONSTRAINT fk_pagos_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
  CONSTRAINT fk_pagos_forma FOREIGN KEY (forma_pago_id) REFERENCES formas_pago(id),
  CONSTRAINT fk_pagos_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_pagos_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  CONSTRAINT chk_pagos_monto CHECK (monto > 0),
  INDEX idx_pagos_numero (numero_recibo),
  INDEX idx_pagos_cliente (cliente_id, fecha),
  INDEX idx_pagos_pedido (pedido_id),
  INDEX idx_pagos_forma (forma_pago_id, fecha),
  INDEX idx_pagos_fecha (fecha),
  INDEX idx_pagos_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT 'Pedidos y pagos creados' AS status;
