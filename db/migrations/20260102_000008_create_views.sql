-- =============================================================================
-- 20260102_000008_create_views.sql
-- Vistas para informes y consultas frecuentes.
-- Todas las vistas filtran `deleted_at IS NULL` en las tablas con soft delete.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- v_pedidos_resumen: pedido + cliente + empleado + estado + totales
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_pedidos_resumen AS
SELECT
  p.id,
  p.numero,
  p.fecha,
  p.fecha_entrega,
  p.entregado,
  p.cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  c.telefono_principal AS cliente_telefono,
  p.empleado_id,
  CONCAT(e.apellido, ', ', e.nombre) AS empleado,
  p.estado_pedido_id,
  ep.codigo AS estado_codigo,
  ep.nombre AS estado_nombre,
  p.canal_venta_id,
  cv.codigo AS canal_codigo,
  p.subtotal,
  p.descuento,
  p.total,
  p.monto_pagado,
  p.saldo,
  CASE
    WHEN p.saldo <= 0 THEN 'PAGADO'
    WHEN p.monto_pagado > 0 THEN 'PARCIAL'
    ELSE 'PENDIENTE'
  END AS estado_pago
FROM pedidos p
JOIN clientes c ON c.id = p.cliente_id
JOIN empleados e ON e.id = p.empleado_id
JOIN estados_pedido ep ON ep.id = p.estado_pedido_id
JOIN canales_venta cv ON cv.id = p.canal_venta_id
WHERE p.deleted_at IS NULL;

-- -----------------------------------------------------------------------------
-- v_productos_mas_vendidos: cantidad vendida por producto en un rango
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_productos_mas_vendidos AS
SELECT
  DATE(p.fecha) AS fecha,
  pi.producto_id,
  pr.codigo AS producto_codigo,
  pr.nombre AS producto_nombre,
  tp.nombre AS tipo_producto,
  SUM(CASE WHEN pi.tipo_linea = 'VENTA' THEN pi.cantidad ELSE 0 END) AS cantidad_vendida,
  SUM(CASE WHEN pi.tipo_linea = 'ENTREGA' THEN pi.cantidad ELSE 0 END) AS cantidad_entregada,
  SUM(CASE WHEN pi.tipo_linea = 'DEVOLUCION' THEN pi.cantidad ELSE 0 END) AS cantidad_devuelta,
  SUM(pi.subtotal) AS monto_total
FROM pedido_items pi
JOIN pedidos p ON p.id = pi.pedido_id
JOIN productos pr ON pr.id = pi.producto_id
JOIN tipos_producto tp ON tp.id = pr.tipo_producto_id
WHERE p.deleted_at IS NULL
GROUP BY DATE(p.fecha), pi.producto_id, pr.codigo, pr.nombre, tp.nombre;

-- -----------------------------------------------------------------------------
-- v_regularidad_clientes: promedio de días entre pedidos por cliente
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_regularidad_clientes AS
SELECT
  c.id AS cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  COUNT(p.id) AS total_pedidos,
  MAX(p.fecha) AS ultimo_pedido,
  MIN(p.fecha) AS primer_pedido,
  CASE
    WHEN COUNT(p.id) > 1
    THEN DATEDIFF(MAX(p.fecha), MIN(p.fecha)) / (COUNT(p.id) - 1)
    ELSE NULL
  END AS dias_promedio_entre_pedidos,
  SUM(p.total) AS total_facturado,
  SUM(p.saldo) AS saldo_pendiente
FROM clientes c
LEFT JOIN pedidos p ON p.cliente_id = c.id AND p.deleted_at IS NULL
WHERE c.deleted_at IS NULL
GROUP BY c.id, cliente;

-- -----------------------------------------------------------------------------
-- v_saldo_clientes: total adeudado por cliente
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_saldo_clientes AS
SELECT
  c.id AS cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  c.telefono_principal,
  COUNT(p.id) AS pedidos_pendientes,
  COALESCE(SUM(p.saldo), 0) AS saldo_total
FROM clientes c
LEFT JOIN pedidos p ON p.cliente_id = c.id
                     AND p.deleted_at IS NULL
                     AND p.saldo > 0
WHERE c.deleted_at IS NULL
GROUP BY c.id, cliente, c.telefono_principal
HAVING saldo_total > 0
ORDER BY saldo_total DESC;

-- -----------------------------------------------------------------------------
-- v_stock_garrafas: conteo por estado y capacidad
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_stock_garrafas AS
SELECT
  g.capacidad_kg,
  g.estado_garrafa_id,
  eg.codigo AS estado_codigo,
  eg.nombre AS estado_nombre,
  eg.color AS estado_color,
  COUNT(*) AS cantidad
FROM garrafas g
JOIN estados_garrafa eg ON eg.id = g.estado_garrafa_id
WHERE g.deleted_at IS NULL AND g.activo = TRUE
GROUP BY g.capacidad_kg, g.estado_garrafa_id, eg.codigo, eg.nombre, eg.color
ORDER BY g.capacidad_kg, eg.nombre;

-- -----------------------------------------------------------------------------
-- v_garrafas_en_clientes: detalle de garrafas en poder de cada cliente
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_garrafas_en_clientes AS
SELECT
  g.id AS garrafa_id,
  g.codigo,
  g.capacidad_kg,
  g.cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  g.fecha_ultimo_movimiento,
  DATEDIFF(CURDATE(), g.fecha_ultimo_movimiento) AS dias_en_cliente
FROM garrafas g
JOIN clientes c ON c.id = g.cliente_id
JOIN estados_garrafa eg ON eg.id = g.estado_garrafa_id
WHERE eg.codigo = 'EN_CLIENTE'
  AND g.deleted_at IS NULL
  AND g.activo = TRUE
  AND c.deleted_at IS NULL
ORDER BY c.apellido, c.nombre, g.capacidad_kg;

-- -----------------------------------------------------------------------------
-- v_pagos_por_forma_pago: agregado por forma de pago y fecha
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_pagos_por_forma_pago AS
SELECT
  DATE(p.fecha) AS fecha,
  fp.codigo AS forma_pago_codigo,
  fp.nombre AS forma_pago_nombre,
  COUNT(p.id) AS cantidad_pagos,
  COALESCE(SUM(p.monto), 0) AS monto_total
FROM pagos p
JOIN formas_pago fp ON fp.id = p.forma_pago_id
WHERE p.deleted_at IS NULL
GROUP BY DATE(p.fecha), fp.codigo, fp.nombre;

-- -----------------------------------------------------------------------------
-- v_cuenta_corriente_cliente: pedidos y pagos ordenados por fecha
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_cuenta_corriente_cliente AS
SELECT
  c.id AS cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  p.id AS pedido_id,
  p.numero AS comprobante,
  p.fecha,
  'PEDIDO' AS tipo_movimiento,
  p.total AS debe,
  0 AS haber,
  p.observaciones
FROM pedidos p
JOIN clientes c ON c.id = p.cliente_id
WHERE p.deleted_at IS NULL
UNION ALL
SELECT
  c.id AS cliente_id,
  CONCAT(c.apellido, ', ', c.nombre) AS cliente,
  pa.pedido_id,
  pa.numero_recibo AS comprobante,
  pa.fecha,
  'PAGO' AS tipo_movimiento,
  0 AS debe,
  pa.monto AS haber,
  pa.observaciones
FROM pagos pa
JOIN clientes c ON c.id = pa.cliente_id
WHERE pa.deleted_at IS NULL
ORDER BY cliente_id, fecha;

-- -----------------------------------------------------------------------------
-- v_saldo_proveedores: pendiente de pago por proveedor
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_saldo_proveedores AS
SELECT
  pr.id AS proveedor_id,
  pr.razon_social,
  pr.cuit,
  COUNT(r.id) AS recepciones_pendientes,
  COALESCE(SUM(r.saldo), 0) AS saldo_total
FROM proveedores pr
LEFT JOIN recepciones_proveedor r ON r.proveedor_id = pr.id
                                   AND r.deleted_at IS NULL
                                   AND r.saldo > 0
WHERE pr.deleted_at IS NULL
GROUP BY pr.id, pr.razon_social, pr.cuit
HAVING saldo_total > 0
ORDER BY saldo_total DESC;

-- -----------------------------------------------------------------------------
-- v_recepciones_resumen: recepción + proveedor + totales
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_recepciones_resumen AS
SELECT
  r.id,
  r.numero,
  r.fecha,
  r.proveedor_id,
  pr.razon_social AS proveedor,
  pr.cuit AS proveedor_cuit,
  r.empleado_id,
  CONCAT(e.apellido, ', ', e.nombre) AS empleado,
  r.numero_factura_proveedor,
  r.subtotal,
  r.descuento,
  r.total,
  r.monto_pagado,
  r.saldo,
  CASE
    WHEN r.saldo <= 0 THEN 'PAGADO'
    WHEN r.monto_pagado > 0 THEN 'PARCIAL'
    ELSE 'PENDIENTE'
  END AS estado_pago
FROM recepciones_proveedor r
JOIN proveedores pr ON pr.id = r.proveedor_id
JOIN empleados e ON e.id = r.empleado_id
WHERE r.deleted_at IS NULL;

SELECT 'Vistas creadas' AS status;
