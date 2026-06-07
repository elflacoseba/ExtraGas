-- =============================================================================
-- 20260607_000001_drop_pedidos_entregado.sql
-- Elimina la columna `entregado` de la tabla `pedidos` por ser redundante con
-- `estado_pedido_id` (estado "ENTREGADO" en `estados_pedido`).
-- No se conserva como columna "sincronizada" porque nada en la app ni en la BD
-- la mantiene alineada con el estado, lo que permite incoherencias.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- 1) DROP de la columna en `pedidos`
-- -----------------------------------------------------------------------------
ALTER TABLE `pedidos`
  DROP COLUMN `entregado`;

-- -----------------------------------------------------------------------------
-- 2) DROP del campo en la vista `v_pedidos_resumen`
-- -----------------------------------------------------------------------------
DROP VIEW IF EXISTS `v_pedidos_resumen`;

CREATE OR REPLACE VIEW `v_pedidos_resumen` AS
SELECT
  p.id,
  p.numero,
  p.fecha,
  p.fecha_entrega,
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

SELECT 'Columna entregado eliminada y vista v_pedidos_resumen actualizada' AS status;
