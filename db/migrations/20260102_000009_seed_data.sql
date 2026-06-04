-- =============================================================================
-- 20260102_000009_seed_data.sql
-- Carga datos iniciales del sistema.
-- Se ejecuta con cwd = raíz del proyecto (para resolver SOURCE relativo).
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- Provincias (referenciadas por empleados, clientes y proveedores)
-- (También disponible en db/seed/provincias_argentina.sql como referencia)
-- -----------------------------------------------------------------------------
INSERT INTO provincias (codigo, nombre, pais) VALUES
  ('AR-C', 'Ciudad Autónoma de Buenos Aires', 'Argentina'),
  ('AR-B', 'Buenos Aires', 'Argentina'),
  ('AR-K', 'Catamarca', 'Argentina'),
  ('AR-H', 'Chaco', 'Argentina'),
  ('AR-U', 'Chubut', 'Argentina'),
  ('AR-X', 'Córdoba', 'Argentina'),
  ('AR-W', 'Corrientes', 'Argentina'),
  ('AR-E', 'Entre Ríos', 'Argentina'),
  ('AR-P', 'Formosa', 'Argentina'),
  ('AR-Y', 'Jujuy', 'Argentina'),
  ('AR-L', 'La Pampa', 'Argentina'),
  ('AR-F', 'La Rioja', 'Argentina'),
  ('AR-M', 'Mendoza', 'Argentina'),
  ('AR-N', 'Misiones', 'Argentina'),
  ('AR-Q', 'Neuquén', 'Argentina'),
  ('AR-R', 'Río Negro', 'Argentina'),
  ('AR-A', 'Salta', 'Argentina'),
  ('AR-J', 'San Juan', 'Argentina'),
  ('AR-D', 'San Luis', 'Argentina'),
  ('AR-Z', 'Santa Cruz', 'Argentina'),
  ('AR-S', 'Santa Fe', 'Argentina'),
  ('AR-G', 'Santiago del Estero', 'Argentina'),
  ('AR-V', 'Tierra del Fuego, Antártida e Islas del Atlántico Sur', 'Argentina'),
  ('AR-T', 'Tucumán', 'Argentina');

-- -----------------------------------------------------------------------------
-- Roles de usuario
-- -----------------------------------------------------------------------------
INSERT INTO roles (codigo, nombre, descripcion) VALUES
  ('ADMIN', 'Administrador', 'Dueño del sistema, acceso total'),
  ('OPERADOR', 'Operador', 'Empleado, gestión de pedidos y cobros');

-- -----------------------------------------------------------------------------
-- Tipos de producto
-- -----------------------------------------------------------------------------
INSERT INTO tipos_producto (codigo, nombre, descripcion) VALUES
  ('GAS', 'Gas envasado', 'Garrafas de gas en distintas capacidades'),
  ('CARBON', 'Carbón', 'Bolsas de carbón en distintas capacidades'),
  ('LENA', 'Leña', 'Bolsa de leña para hogar');

-- -----------------------------------------------------------------------------
-- Formas de pago
-- -----------------------------------------------------------------------------
INSERT INTO formas_pago (codigo, nombre, descripcion, requiere_referencia) VALUES
  ('EFECTIVO', 'Efectivo', 'Pago en efectivo en el momento', FALSE),
  ('TRANSFERENCIA', 'Transferencia bancaria', 'Pago por transferencia/transferencia', TRUE),
  ('MERCADO_PAGO', 'Mercado Pago', 'Pago vía Mercado Pago u otra billetera virtual', TRUE),
  ('CHEQUE', 'Cheque', 'Pago con cheque (a verificar)', TRUE);

-- -----------------------------------------------------------------------------
-- Estados de pedido
-- -----------------------------------------------------------------------------
INSERT INTO estados_pedido (codigo, nombre, descripcion, es_final, color) VALUES
  ('PENDIENTE', 'Pendiente', 'Pedido recibido, sin confirmar', FALSE, '#FFA500'),
  ('CONFIRMADO', 'Confirmado', 'Pedido confirmado, pendiente de preparación', FALSE, '#1E90FF'),
  ('EN_PREPARACION', 'En preparación', 'Pedido en proceso de armado/entrega', FALSE, '#9370DB'),
  ('ENTREGADO', 'Entregado', 'Pedido entregado al cliente', TRUE, '#228B22'),
  ('CANCELADO', 'Cancelado', 'Pedido cancelado', TRUE, '#DC143C');

-- -----------------------------------------------------------------------------
-- Estados de garrafa
-- -----------------------------------------------------------------------------
INSERT INTO estados_garrafa (codigo, nombre, descripcion, es_disponible_para_venta, requiere_cliente, color) VALUES
  ('LLENA_DEPOSITO', 'Llena en depósito', 'Garrafa llena disponible para entregar', TRUE, FALSE, '#228B22'),
  ('VACIA_DEPOSITO', 'Vacía en depósito', 'Garrafa vacía en el depósito', FALSE, FALSE, '#808080'),
  ('EN_CLIENTE', 'En cliente', 'Garrafa en poder de un cliente (canje/consignación)', FALSE, TRUE, '#1E90FF'),
  ('EN_TRANSITO', 'En tránsito', 'Garrafa entregada pero pendiente de confirmación', FALSE, FALSE, '#FFA500'),
  ('DAÑADA', 'Dañada', 'Garrafa dañada, no apta para intercambio', FALSE, FALSE, '#DC143C'),
  ('FUERA_SERVICIO', 'Fuera de servicio', 'Garrafa dada de baja definitiva', FALSE, FALSE, '#2F2F2F');

-- -----------------------------------------------------------------------------
-- Tipos de movimiento de garrafa
-- -----------------------------------------------------------------------------
INSERT INTO tipos_movimiento_garrafa (codigo, nombre, descripcion) VALUES
  ('COMPRA', 'Compra a proveedor', 'Ingreso de garrafa por recepción de proveedor'),
  ('ENTREGA_CLIENTE', 'Entrega a cliente', 'Salida de garrafa hacia un cliente'),
  ('DEVOLUCION_CLIENTE', 'Devolución de cliente', 'Regreso de garrafa desde un cliente'),
  ('ENVIO_PROVEEDOR', 'Envío a proveedor', 'Salida de garrafa hacia el proveedor'),
  ('BAJA', 'Baja definitiva', 'Garrafa retirada del sistema'),
  ('REPARACION', 'Envío a reparación', 'Garrafa enviada a reparar'),
  ('REINGRESO', 'Reingreso de reparación', 'Garrafa que vuelve de reparación'),
  ('DAÑO', 'Registrar daño', 'Garrafa dañada en operación');

-- -----------------------------------------------------------------------------
-- Canales de venta
-- -----------------------------------------------------------------------------
INSERT INTO canales_venta (codigo, nombre, descripcion) VALUES
  ('TELEFONO', 'Teléfono', 'Pedido recibido por llamada telefónica'),
  ('WHATSAPP', 'WhatsApp', 'Pedido recibido por mensaje de WhatsApp'),
  ('PRESENCIAL', 'Presencial', 'Pedido realizado en el local');

-- -----------------------------------------------------------------------------
-- Medios de contacto del pedido
-- -----------------------------------------------------------------------------
INSERT INTO medios_contacto_pedido (codigo, nombre, descripcion) VALUES
  ('WHATSAPP', 'WhatsApp', 'Se contactó al cliente por WhatsApp'),
  ('TELEFONO', 'Llamada telefónica', 'Se contactó al cliente por llamada'),
  ('PRESENCIAL', 'Presencial', 'Cliente concurrió al local'),
  ('OTRO', 'Otro', 'Otro medio de contacto');

-- -----------------------------------------------------------------------------
-- Tipos de contacto adicionales de cliente
-- -----------------------------------------------------------------------------
INSERT INTO tipos_contacto_cliente (codigo, nombre) VALUES
  ('TELEFONO', 'Teléfono'),
  ('WHATSAPP', 'WhatsApp'),
  ('EMAIL', 'Email'),
  ('OTRO', 'Otro');

-- -----------------------------------------------------------------------------
-- Productos del catálogo
-- -----------------------------------------------------------------------------
INSERT INTO productos (codigo, nombre, descripcion, tipo_producto_id, capacidad_kg, unidad_venta, precio_actual, maneja_garrafa_individual) VALUES
  ('GAS-10',  'Garrafa de gas 10 kg',  'Garrafa de gas envasado de 10 kg',  (SELECT id FROM tipos_producto WHERE codigo='GAS'),    10.00, 'GARRAFA', 0.00, TRUE),
  ('GAS-15',  'Garrafa de gas 15 kg',  'Garrafa de gas envasado de 15 kg',  (SELECT id FROM tipos_producto WHERE codigo='GAS'),    15.00, 'GARRAFA', 0.00, TRUE),
  ('GAS-45',  'Garrafa de gas 45 kg',  'Garrafa de gas envasado de 45 kg',  (SELECT id FROM tipos_producto WHERE codigo='GAS'),    45.00, 'GARRAFA', 0.00, TRUE),
  ('CAR-3',   'Bolsa de carbón 3 kg',  'Bolsa de carbón de 3 kg',           (SELECT id FROM tipos_producto WHERE codigo='CARBON'),  3.00, 'BOLSA',   0.00, FALSE),
  ('CAR-5',   'Bolsa de carbón 5 kg',  'Bolsa de carbón de 5 kg',           (SELECT id FROM tipos_producto WHERE codigo='CARBON'),  5.00, 'BOLSA',   0.00, FALSE),
  ('CAR-10',  'Bolsa de carbón 10 kg', 'Bolsa de carbón de 10 kg',          (SELECT id FROM tipos_producto WHERE codigo='CARBON'), 10.00, 'BOLSA',   0.00, FALSE),
  ('CAR-25',  'Bolsa de carbón 25 kg', 'Bolsa de carbón de 25 kg',          (SELECT id FROM tipos_producto WHERE codigo='CARBON'), 25.00, 'BOLSA',   0.00, FALSE),
  ('LEN-25',  'Bolsa de leña 25 kg',   'Bolsa de leña para hogar de 25 kg', (SELECT id FROM tipos_producto WHERE codigo='LENA'),   25.00, 'BOLSA',   0.00, FALSE);

-- -----------------------------------------------------------------------------
-- Secuencias inicializadas (la numeración real la gestiona el trigger,
-- esto es solo para que aparezcan en la tabla desde el principio)
-- -----------------------------------------------------------------------------
INSERT INTO secuencias (nombre, prefijo, anio, ultimo_valor) VALUES
  ('pedidos',              'PED',      YEAR(CURDATE()), 0),
  ('pagos_cliente',        'REC',      YEAR(CURDATE()), 0),
  ('recepciones_proveedor','REC-PROV', YEAR(CURDATE()), 0),
  ('pagos_proveedor',      'PAG-PROV', YEAR(CURDATE()), 0);

-- -----------------------------------------------------------------------------
-- Empleado seed: el dueño. El username 'admin' y password 'admin123'
-- es SOLO para desarrollo inicial. Cambiar inmediatamente en producción.
-- password_hash es un placeholder; la app debe hashear con argon2id/bcrypt
-- en el primer login o antes del primer uso.
-- -----------------------------------------------------------------------------
INSERT INTO empleados (nombre, apellido, dni, fecha_ingreso, activo) VALUES
  ('Dueño', 'Administrador', NULL, CURDATE(), TRUE);

INSERT INTO usuarios (username, password_hash, email, rol_id, activo) VALUES
  ('admin', '$2a$11$LXexHD1uSwkOLaBCUvArbe5YKVYC7xujLmwQHCRHtkHz61kTY6S2K', NULL, (SELECT id FROM roles WHERE codigo='ADMIN'), TRUE);

UPDATE empleados e
JOIN usuarios u ON u.username = 'admin'
SET e.usuario_id = u.id
WHERE e.nombre='Dueño' AND e.apellido='Administrador';

SELECT 'Seed data cargado' AS status;
