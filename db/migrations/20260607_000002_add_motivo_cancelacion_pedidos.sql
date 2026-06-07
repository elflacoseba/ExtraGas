-- Migración: agregar columna motivo_cancelacion a pedidos
-- Fecha: 2026-06-07
-- Descripción: permite registrar el motivo de cancelación de un pedido
--              cuando se transiciona al estado CANCELADO.

ALTER TABLE pedidos
  ADD COLUMN motivo_cancelacion VARCHAR(500) NULL AFTER observaciones;
