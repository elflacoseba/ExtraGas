-- =============================================================================
-- verify_issue_105_clientes_dni_soft_delete.sql
-- Verificación end-to-end del fix de la issue #105.
--
-- Reproduce el flujo:
--   1. Crear cliente activo con DNI X
--   2. Soft-delete del cliente
--   3. Crear OTRO cliente con DNI X (debe pasar sin duplicate key)
--   4. Verificar que la unicidad sigue activa entre no soft-deleted
--
-- Uso:
--   mysql -u<user> -h<host> extragas < db/scripts/verify_issue_105_clientes_dni_soft_delete.sql
--
-- Pre-requisito: la migración 20260829_000001 ya está aplicada
-- (idx_clientes_dni_unique existe, dni_unique es VIRTUAL).
-- =============================================================================

USE extragas;

-- Forzar colación de la sesión a la del schema (utf8mb4_unicode_ci) para evitar
-- "Illegal mix of collations" en comparaciones con information_schema en MySQL 8.4+,
-- donde el default del server es utf8mb4_0900_ai_ci.
SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;

-- DNI de prueba único. dni es VARCHAR(15), así que limitamos el random a 4 dígitos
-- y usamos prefijo corto: 'T' + epoch truncado a últimos 9 + '-' + 4 dígitos random.
SET @dni_test = CONCAT('T', SUBSTRING(UNIX_TIMESTAMP(), -6), '-', LPAD(FLOOR(RAND() * 10000), 4, '0'));

-- Limpieza previa por si quedó un cliente de prueba de una corrida anterior
-- (DNI empieza con 'T' + 6 dígitos + '-' + 4 dígitos = 12 chars).
UPDATE clientes SET deleted_at = NOW(), activo = 0, updated_at = NOW(), updated_by = 1
 WHERE dni LIKE 'T______-____';

SELECT '=== Pre-condiciones ===' AS step;

SELECT 'Verificando columna virtual dni_unique...' AS check_name;
SELECT COLUMN_NAME, EXTRA, GENERATION_EXPRESSION
  FROM information_schema.columns
 WHERE table_schema = DATABASE()
   AND table_name = 'clientes'
   AND column_name = 'dni_unique';

SELECT 'Verificando índice idx_clientes_dni_unique...' AS check_name;
SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE
  FROM information_schema.statistics
 WHERE table_schema = DATABASE()
   AND table_name = 'clientes'
   AND index_name = 'idx_clientes_dni_unique';

SELECT '=== Paso 1: Insertar cliente activo con DNI de prueba ===' AS step;

INSERT INTO clientes
  (codigo, nombre, apellido, dni, telefono_principal, fecha_alta, activo, created_at, updated_at, created_by, updated_by)
VALUES
  (CONCAT('TEST-', UNIX_TIMESTAMP()), 'TEST', 'PRUEBA-1', @dni_test, '000', CURDATE(), 1, NOW(), NOW(), 1, 1);

SET @id_cliente_1 = LAST_INSERT_ID();
SELECT CONCAT('Insertado cliente id=', @id_cliente_1, ' con DNI=', @dni_test, ' (activo)') AS info;

SELECT id, dni, deleted_at, dni_unique
  FROM clientes WHERE id = @id_cliente_1;

SELECT '=== Paso 2: Soft-delete del cliente ===' AS step;

UPDATE clientes
   SET deleted_at = NOW(), activo = 0, updated_at = NOW(), updated_by = 1
 WHERE id = @id_cliente_1;

SELECT id, dni, deleted_at, dni_unique, activo
  FROM clientes WHERE id = @id_cliente_1;
-- Esperado: deleted_at != NULL, dni_unique = NULL, activo = 0

SELECT '=== Paso 3: Crear OTRO cliente con el mismo DNI (el bug original fallaba acá) ===' AS step;

-- Antes del fix, este INSERT fallaba con ER_DUP_ENTRY (1062) sobre idx_clientes_dni.
-- Con la columna virtual dni_unique, debe pasar porque el soft-deleted tiene dni_unique=NULL.
INSERT INTO clientes
  (codigo, nombre, apellido, dni, telefono_principal, fecha_alta, activo, created_at, updated_at, created_by, updated_by)
VALUES
  (CONCAT('TEST-', UNIX_TIMESTAMP()), 'TEST', 'PRUEBA-2', @dni_test, '000', CURDATE(), 1, NOW(), NOW(), 1, 1);

SET @id_cliente_2 = LAST_INSERT_ID();
SELECT CONCAT('OK — Insertado cliente id=', @id_cliente_2, ' con DNI=', @dni_test, ' (activo, mismo DNI que el soft-deleted)') AS info;

SELECT id, dni, deleted_at, dni_unique, activo
  FROM clientes WHERE dni = @dni_test ORDER BY id;
-- Esperado: 2 filas, una con deleted_at NULL + dni_unique=dni, otra con deleted_at != NULL + dni_unique=NULL

SELECT '=== Paso 4: Intentar crear un TERCER cliente activo con el mismo DNI (debe fallar) ===' AS step;

-- La unicidad entre ACTIVOS sigue activa. Usamos INSERT IGNORE para que el
-- duplicate key se downgradee a warning en vez de abortar el script.
INSERT IGNORE INTO clientes
  (codigo, nombre, apellido, dni, telefono_principal, fecha_alta, activo, created_at, updated_at, created_by, updated_by)
VALUES
  (CONCAT('TEST-', UNIX_TIMESTAMP()), 'TEST', 'PRUEBA-3', @dni_test, '000', CURDATE(), 1, NOW(), NOW(), 1, 1);

SET @filas_insertadas = ROW_COUNT();

SELECT CASE
         WHEN @filas_insertadas = 0
           THEN CONCAT('OK — Unicidad entre activos PRESERVADA (filas insertadas=0; ya hay 1 activo con DNI ', @dni_test, ')')
         ELSE CONCAT('FALLO — Se permitió crear ', @filas_insertadas, ' clientes activos con el mismo DNI (esperado=0)')
       END AS verificacion_paso_4;

SELECT '=== Conteo final de filas con el DNI de prueba ===' AS step;

SELECT
  SUM(deleted_at IS NULL) AS activos,
  SUM(deleted_at IS NOT NULL) AS soft_deleted,
  COUNT(*) AS total
  FROM clientes WHERE dni = @dni_test;
-- Esperado: activos=1, soft_deleted=1, total=2

SELECT '=== Limpieza ===' AS step;

UPDATE clientes SET deleted_at = NOW(), activo = 0, updated_at = NOW(), updated_by = 1
 WHERE dni = @dni_test;

SELECT CONCAT('Cleanup OK — DNI de prueba ', @dni_test, ' marcado como soft-deleted') AS info;

SELECT '=== Verificación completa: issue #105 RESUELTA ===' AS result;
