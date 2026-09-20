-- =============================================================================
-- 20260920_175040_add_concurrency_version_garrafas.sql
-- Adds `version BIGINT UNSIGNED NOT NULL DEFAULT 0` to `garrafas` for
-- optimistic concurrency (Issue #182 T04). Pairs with:
--   - src/ExtraGasMVC/Data/Entities/Garrafa.cs (Version ulong)
--   - src/ExtraGasMVC/Data/Configurations/GarrafaConfiguration.cs (IsConcurrencyToken)
--   - src/ExtraGasMVC/Services/Implementations/GarrafaService.cs
--     (UpdateAsync / CambiarEstadoAsync / RegistrarMovimientoPorCanjeAsync
--      incrementan entity.Version antes de SaveChanges y capturan
--      DbUpdateConcurrencyException -> InvalidOperationException)
--   - src/ExtraGasMVC/Controllers/GarrafasController.cs
--     (CambiarEstado POST: una sola lectura, pasa EstadoGarrafaId al service
--      que detecta el race contra la fila recargada)
--
-- Por que BIGINT version y no BINARY(8) row_version + trigger como productos
-- (PR #145):
--   1. BIGINT version es mas testeable en InMemory sin simulacion de triggers.
--      El InMemory provider respeta tokens de concurrencia, pero NO simula
--      triggers MySQL, asi que la opcion BINARY(8) requiere un workaround
--      en tests (incrementar manualmente el RowVersion antes de SaveChanges).
--   2. El service ya conoce el patron "snapshot pre-mapper" para Activo /
--      EstadoGarrafaId / ClienteId (PR #183). Agregar `version` al snapshot
--      es consistente con esa mecanica.
--   3. El task spec (#182 T04 + prompt de este PR) recomienda explicitamente
--      "[ConcurrencyCheck] con columna BIGINT que el service incrementa
--      explicitamente antes de SaveChanges".
--
-- La columna arranca en 0 (DEFAULT) pero el backfill la pone en 1 — asi la
-- primera lectura por app devuelve 1, y el primer UPDATE compara contra 1,
-- incrementa a 2, persiste 2. Sin backfill las filas existentes quedarian
-- en 0 y la primera operacion haria UPDATE WHERE version=0 -> SET version=1,
-- que matchea y funciona, pero deja a la app un piso de 0 que confunde al
-- debugging. Empezar en 1 es mas claro.
--
-- Idempotente: ADD COLUMN con guard information_schema, UPDATE backfill que
-- cubre el re-run si la columna existe pero version=0 (ej. si el primer
-- run fallo entre ADD y UPDATE).
-- =============================================================================

USE extragas;

-- Columna version BIGINT UNSIGNED NOT NULL DEFAULT 0.
SET @col_exists = (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'garrafas'
    AND column_name = 'version'
);
SET @sql = IF(@col_exists = 0,
  'ALTER TABLE garrafas ADD COLUMN version BIGINT UNSIGNED NOT NULL DEFAULT 0 AFTER deleted_at',
  'SELECT "Column garrafas.version ya existe, skipping" AS status'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Backfill: cualquier fila con version=0 (recien creada por el ALTER o
-- preexistente) pasa a 1. Asi la primera operacion por app ve version=1
-- y compara contra 1.
UPDATE garrafas SET version = 1 WHERE version = 0;

SELECT 'garrafas.version + backfill a 1 listos' AS status;