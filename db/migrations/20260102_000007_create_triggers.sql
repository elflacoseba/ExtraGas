-- =============================================================================
-- 20260102_000007_create_triggers.sql
-- Triggers del sistema:
--   - Numeración automática desde tabla `secuencias`
--   - Mantenimiento de `monto_pagado` en pedidos y recepciones_proveedor
--   - Actualización de `fecha_ultimo_movimiento` en garrafas
--   - Validaciones de integridad referencial lógica
-- =============================================================================

USE extragas;

-- =============================================================================
-- 1) Numeración automática de PEDIDOS
-- =============================================================================
DROP TRIGGER IF EXISTS trg_pedidos_bi;

DELIMITER //
CREATE TRIGGER trg_pedidos_bi
BEFORE INSERT ON pedidos
FOR EACH ROW
BEGIN
  DECLARE v_anio SMALLINT UNSIGNED;
  DECLARE v_siguiente INT UNSIGNED;

  SET v_anio = YEAR(NEW.fecha);

  INSERT INTO secuencias (nombre, prefijo, anio, ultimo_valor)
  VALUES ('pedidos', 'PED', v_anio, 1)
  ON DUPLICATE KEY UPDATE ultimo_valor = ultimo_valor + 1;

  SELECT ultimo_valor INTO v_siguiente
  FROM secuencias
  WHERE nombre = 'pedidos' AND anio = v_anio;

  SET NEW.numero = CONCAT('PED-', v_anio, '-', LPAD(v_siguiente, 5, '0'));
END//
DELIMITER ;

-- =============================================================================
-- 2) Numeración automática de PAGOS (recibos de cliente)
-- =============================================================================
DROP TRIGGER IF EXISTS trg_pagos_bi;

DELIMITER //
CREATE TRIGGER trg_pagos_bi
BEFORE INSERT ON pagos
FOR EACH ROW
BEGIN
  DECLARE v_anio SMALLINT UNSIGNED;
  DECLARE v_siguiente INT UNSIGNED;

  SET v_anio = YEAR(NEW.fecha);

  INSERT INTO secuencias (nombre, prefijo, anio, ultimo_valor)
  VALUES ('pagos_cliente', 'REC', v_anio, 1)
  ON DUPLICATE KEY UPDATE ultimo_valor = ultimo_valor + 1;

  SELECT ultimo_valor INTO v_siguiente
  FROM secuencias
  WHERE nombre = 'pagos_cliente' AND anio = v_anio;

  SET NEW.numero_recibo = CONCAT('REC-', v_anio, '-', LPAD(v_siguiente, 5, '0'));
END//
DELIMITER ;

-- =============================================================================
-- 3) Numeración automática de RECEPCIONES_PROVEEDOR
-- =============================================================================
DROP TRIGGER IF EXISTS trg_recepciones_bi;

DELIMITER //
CREATE TRIGGER trg_recepciones_bi
BEFORE INSERT ON recepciones_proveedor
FOR EACH ROW
BEGIN
  DECLARE v_anio SMALLINT UNSIGNED;
  DECLARE v_siguiente INT UNSIGNED;

  SET v_anio = YEAR(NEW.fecha);

  INSERT INTO secuencias (nombre, prefijo, anio, ultimo_valor)
  VALUES ('recepciones_proveedor', 'REC-PROV', v_anio, 1)
  ON DUPLICATE KEY UPDATE ultimo_valor = ultimo_valor + 1;

  SELECT ultimo_valor INTO v_siguiente
  FROM secuencias
  WHERE nombre = 'recepciones_proveedor' AND anio = v_anio;

  SET NEW.numero = CONCAT('REC-PROV-', v_anio, '-', LPAD(v_siguiente, 5, '0'));
END//
DELIMITER ;

-- =============================================================================
-- 4) Numeración automática de PAGOS_PROVEEDOR
-- =============================================================================
DROP TRIGGER IF EXISTS trg_pagos_proveedor_bi;

DELIMITER //
CREATE TRIGGER trg_pagos_proveedor_bi
BEFORE INSERT ON pagos_proveedor
FOR EACH ROW
BEGIN
  DECLARE v_anio SMALLINT UNSIGNED;
  DECLARE v_siguiente INT UNSIGNED;

  SET v_anio = YEAR(NEW.fecha);

  INSERT INTO secuencias (nombre, prefijo, anio, ultimo_valor)
  VALUES ('pagos_proveedor', 'PAG-PROV', v_anio, 1)
  ON DUPLICATE KEY UPDATE ultimo_valor = ultimo_valor + 1;

  SELECT ultimo_valor INTO v_siguiente
  FROM secuencias
  WHERE nombre = 'pagos_proveedor' AND anio = v_anio;

  SET NEW.numero = CONCAT('PAG-PROV-', v_anio, '-', LPAD(v_siguiente, 5, '0'));
END//
DELIMITER ;

-- =============================================================================
-- 5) Mantenimiento de pedidos.monto_pagado ante cambios en pagos
-- =============================================================================
DROP TRIGGER IF EXISTS trg_pagos_ai;
DROP TRIGGER IF EXISTS trg_pagos_au;
DROP TRIGGER IF EXISTS trg_pagos_ad;

DELIMITER //
CREATE TRIGGER trg_pagos_ai
AFTER INSERT ON pagos
FOR EACH ROW
BEGIN
  IF NEW.pedido_id IS NOT NULL AND NEW.deleted_at IS NULL THEN
    UPDATE pedidos
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos
      WHERE pedido_id = NEW.pedido_id AND deleted_at IS NULL
    )
    WHERE id = NEW.pedido_id;
  END IF;
END//

CREATE TRIGGER trg_pagos_au
AFTER UPDATE ON pagos
FOR EACH ROW
BEGIN
  IF NEW.pedido_id IS NOT NULL THEN
    UPDATE pedidos
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos
      WHERE pedido_id = NEW.pedido_id AND deleted_at IS NULL
    )
    WHERE id = NEW.pedido_id;
  END IF;
  IF OLD.pedido_id IS NOT NULL AND OLD.pedido_id <> NEW.pedido_id THEN
    UPDATE pedidos
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos
      WHERE pedido_id = OLD.pedido_id AND deleted_at IS NULL
    )
    WHERE id = OLD.pedido_id;
  END IF;
END//

CREATE TRIGGER trg_pagos_ad
AFTER DELETE ON pagos
FOR EACH ROW
BEGIN
  IF OLD.pedido_id IS NOT NULL THEN
    UPDATE pedidos
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos
      WHERE pedido_id = OLD.pedido_id AND deleted_at IS NULL
    )
    WHERE id = OLD.pedido_id;
  END IF;
END//
DELIMITER ;

-- =============================================================================
-- 6) Mantenimiento de recepciones_proveedor.monto_pagado
-- =============================================================================
DROP TRIGGER IF EXISTS trg_pagos_proveedor_ai;
DROP TRIGGER IF EXISTS trg_pagos_proveedor_au;
DROP TRIGGER IF EXISTS trg_pagos_proveedor_ad;

DELIMITER //
CREATE TRIGGER trg_pagos_proveedor_ai
AFTER INSERT ON pagos_proveedor
FOR EACH ROW
BEGIN
  IF NEW.recepcion_id IS NOT NULL AND NEW.deleted_at IS NULL THEN
    UPDATE recepciones_proveedor
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos_proveedor
      WHERE recepcion_id = NEW.recepcion_id AND deleted_at IS NULL
    )
    WHERE id = NEW.recepcion_id;
  END IF;
END//

CREATE TRIGGER trg_pagos_proveedor_au
AFTER UPDATE ON pagos_proveedor
FOR EACH ROW
BEGIN
  IF NEW.recepcion_id IS NOT NULL THEN
    UPDATE recepciones_proveedor
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos_proveedor
      WHERE recepcion_id = NEW.recepcion_id AND deleted_at IS NULL
    )
    WHERE id = NEW.recepcion_id;
  END IF;
  IF OLD.recepcion_id IS NOT NULL AND OLD.recepcion_id <> NEW.recepcion_id THEN
    UPDATE recepciones_proveedor
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos_proveedor
      WHERE recepcion_id = OLD.recepcion_id AND deleted_at IS NULL
    )
    WHERE id = OLD.recepcion_id;
  END IF;
END//

CREATE TRIGGER trg_pagos_proveedor_ad
AFTER DELETE ON pagos_proveedor
FOR EACH ROW
BEGIN
  IF OLD.recepcion_id IS NOT NULL THEN
    UPDATE recepciones_proveedor
    SET monto_pagado = (
      SELECT COALESCE(SUM(monto), 0)
      FROM pagos_proveedor
      WHERE recepcion_id = OLD.recepcion_id AND deleted_at IS NULL
    )
    WHERE id = OLD.recepcion_id;
  END IF;
END//
DELIMITER ;

-- =============================================================================
-- 7) Actualización de fecha_ultimo_movimiento en garrafas
-- =============================================================================
DROP TRIGGER IF EXISTS trg_mov_garrafa_ai;

DELIMITER //
CREATE TRIGGER trg_mov_garrafa_ai
AFTER INSERT ON movimientos_garrafa
FOR EACH ROW
BEGIN
  UPDATE garrafas
  SET fecha_ultimo_movimiento = NEW.fecha,
      estado_garrafa_id = NEW.estado_destino_id
  WHERE id = NEW.garrafa_id;
END//
DELIMITER ;

-- =============================================================================
-- 8) Validación: si estado_garrafa es EN_CLIENTE, cliente_id es obligatorio
-- =============================================================================
DROP TRIGGER IF EXISTS trg_garrafas_bi_validate;

DELIMITER //
CREATE TRIGGER trg_garrafas_bi_validate
BEFORE INSERT ON garrafas
FOR EACH ROW
BEGIN
  DECLARE v_requiere_cliente BOOLEAN;

  SELECT requiere_cliente INTO v_requiere_cliente
  FROM estados_garrafa
  WHERE id = NEW.estado_garrafa_id;

  IF v_requiere_cliente IS TRUE AND NEW.cliente_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
    SET MESSAGE_TEXT = 'El estado de garrafa requiere un cliente_id';
  END IF;
END//
DELIMITER ;

SELECT 'Triggers creados' AS status;
