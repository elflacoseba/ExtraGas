-- 20260830_000001_drop_clientes_activo.sql
-- Elimina la columna `clientes.activo` (flag duplicado de `deleted_at`).
-- Issue #115: dos fuentes de verdad para el mismo estado eran fuente de bugs.
-- `activo` se reemplaza por una vista derivada: `activo = deleted_at IS NULL`.
--
-- Esta es la Opción B de la issue. La Opción A (dejar la columna pero nunca
-- escribirla desde la app) fue descartada porque deja un pie de bomba para
-- futuras migraciones que toquen la columna por error.
--
-- Seguridad del DROP:
--   - La columna ya no se persiste desde la app (tras el merge del código
--     que acompaña esta migración). Antes de aplicar en producción, hay que
--     mergear el PR de código y deployar la app que no escribe la columna.
--   - La columna NO aparece en `pedidos`, ni en vistas SQL (ningún SELECT la
--     referencia sobre `clientes`; `g.activo` que sí existe pertenece a
--     `garrafas`, no a `clientes`).
--   - El comando es idempotente: si la columna ya no existe, el guard
--     information_schema evita el ALTER y la migración es un no-op.

USE extragas;

-- DROP COLUMN clientes.activo (idempotente: guard en information_schema).
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'clientes'
    AND column_name = 'activo'
);
SET @sql = IF(@col_exists > 0,
  'ALTER TABLE clientes DROP COLUMN activo',
  'SELECT "Column clientes.activo ya no existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;