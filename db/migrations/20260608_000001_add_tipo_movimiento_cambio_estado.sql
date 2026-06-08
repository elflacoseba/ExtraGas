-- 20260608_000001_add_tipo_movimiento_cambio_estado.sql
-- Adds the CAMBIO_ESTADO type to tipos_movimiento_garrafa to support
-- manual state changes performed from the UI.
-- Issue #36: GarrafaService.CambiarEstadoAsync must log a movimiento_garrafa.

USE extragas;

INSERT INTO tipos_movimiento_garrafa (codigo, nombre, descripcion) VALUES
  ('CAMBIO_ESTADO', 'Cambio manual de estado', 'Cambio de estado realizado manualmente desde la UI');
