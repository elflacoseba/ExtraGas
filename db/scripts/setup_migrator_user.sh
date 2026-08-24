#!/usr/bin/env bash
# =============================================================================
# setup_migrator_user.sh — Crea (idempotente) el user `extragas_migrator` con
# los grants necesarios para correr install.sh: SYSTEM_VARIABLES_ADMIN (para
# SET GLOBAL) + permisos completos sobre `extragas.*` (para CREATE TRIGGER y
# todo el resto del schema).
#
# Pensado para homelab/dev. Para producción, ajustar el host y rotar la
# password fuera de este script.
#
# Uso:
#   MYSQL_USER=root MYSQL_MIGRATOR_PASS='secretpass' ./db/scripts/setup_migrator_user.sh
#
# Pre-requisito: MySQL corriendo y MYSQL_USER con privilege CREATE USER.
# Idempotente: se puede correr múltiples veces sin efecto colateral.
# =============================================================================

set -euo pipefail

# --- Configuración -----------------------------------------------------------
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASS="${MYSQL_PASS:-}"
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MIGRATOR_HOST="${MYSQL_MIGRATOR_HOST:-%}"   # '%' para homelab; ajustar para prod
MIGRATOR_USER_NAME="${MYSQL_MIGRATOR_NAME:-extragas_migrator}"
MYSQL_MIGRATOR_PASS="${MYSQL_MIGRATOR_PASS:-}"

if [ -z "${MYSQL_MIGRATOR_PASS}" ]; then
  echo "ERROR: MYSQL_MIGRATOR_PASS es requerido (la password del user a crear)." >&2
  echo "       Ejemplo: MYSQL_MIGRATOR_PASS='unaPassDe16+Chars' ./db/scripts/setup_migrator_user.sh" >&2
  exit 1
fi

if [ "${#MYSQL_MIGRATOR_PASS}" -lt 12 ]; then
  echo "WARN: la password tiene ${#MYSQL_MIGRATOR_PASS} caracteres (recomendado >= 16)." >&2
fi

MYSQL_CMD="mysql -u${MYSQL_USER} -h${MYSQL_HOST}"
if [ -n "${MYSQL_PASS}" ]; then
  MYSQL_CMD="${MYSQL_CMD} -p${MYSQL_PASS}"
fi

# --- Pre-check ---------------------------------------------------------------
echo "==> Verificando conexión a MySQL como ${MYSQL_USER}@${MYSQL_HOST}..."
ERROR_OUTPUT=$(${MYSQL_CMD} -e "SELECT VERSION();" 2>&1 >/dev/null)
if [ $? -ne 0 ]; then
  echo "    ERROR: no se puede conectar. Detalle: ${ERROR_OUTPUT}" >&2
  exit 1
fi
${MYSQL_CMD} -e "SELECT VERSION();" 2>/dev/null

# --- Crear / actualizar user (idempotente) -----------------------------------
echo "==> Creando/actualizando user ${MIGRATOR_USER_NAME}@${MIGRATOR_HOST}..."

# MySQL 8.0+ no soporta CREATE USER IF NOT EXISTS. Usamos el patrón
# information_schema + PREPARE/EXECUTE (consistente con las migraciones).
${MYSQL_CMD} <<EOF
SET @user_exists = (
  SELECT COUNT(*) FROM mysql.user
  WHERE user = '${MIGRATOR_USER_NAME}' AND host = '${MIGRATOR_HOST}'
);
SET @sql = IF(@user_exists = 0,
  "CREATE USER '${MIGRATOR_USER_NAME}'@'${MIGRATOR_HOST}' IDENTIFIED BY '${MYSQL_MIGRATOR_PASS}'",
  "SELECT '${MIGRATOR_USER_NAME}@${MIGRATOR_HOST} ya existe, no se recrea' AS status"
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Siempre reseteamos la password (idempotente): si el user existía, esto
-- actualiza al valor de MYSQL_MIGRATOR_PASS. Útil para rotación.
ALTER USER '${MIGRATOR_USER_NAME}'@'${MIGRATOR_HOST}' IDENTIFIED BY '${MYSQL_MIGRATOR_PASS}';
EOF

# --- Grants (idempotente: GRANT es idempotente en MySQL 8.x) ----------------
echo "==> Aplicando grants..."
${MYSQL_CMD} <<EOF
-- SYSTEM_VARIABLES_ADMIN para SET GLOBAL (time_zone, log_bin_trust_function_creators, etc.)
GRANT SYSTEM_VARIABLES_ADMIN ON *.* TO '${MIGRATOR_USER_NAME}'@'${MIGRATOR_HOST}';

-- Grants completos sobre la BD del proyecto (necesarios para CREATE TRIGGER,
-- CREATE VIEW, ALTER TABLE, etc.). NO usar ALL PRIVILEGES para que el GRANT
-- quede explícito y revisable.
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, INDEX, REFERENCES,
      CREATE VIEW, SHOW VIEW, CREATE ROUTINE, ALTER ROUTINE, EXECUTE, TRIGGER,
      CREATE TEMPORARY TABLES, LOCK TABLES, EVENT
  ON \`extragas\`.* TO '${MIGRATOR_USER_NAME}'@'${MIGRATOR_HOST}';

FLUSH PRIVILEGES;

SELECT 'Grants aplicados correctamente' AS status;

-- Verificación: mostrar grants del user
SHOW GRANTS FOR '${MIGRATOR_USER_NAME}'@'${MIGRATOR_HOST}';
EOF

# --- Post-install: instrucciones ---------------------------------------------
echo ""
echo "================================================================"
echo "  User ${MIGRATOR_USER_NAME}@${MIGRATOR_HOST} listo."
echo ""
echo "  Uso en install.sh:"
echo "    MYSQL_USER=root ./db/scripts/install.sh \\"
echo "      # modo root (todo el server, no recomendado en prod)"
echo ""
echo "    MYSQL_MIGRATOR_USER=${MIGRATOR_USER_NAME} \\"
echo "    MYSQL_MIGRATOR_PASS='<esta-password>' \\"
echo "    ./db/scripts/install.sh"
echo ""
echo "  Próximo paso (recomendado, manual como root):"
echo "    REVOKE SYSTEM_VARIABLES_ADMIN ON *.* FROM 'extragas'@'%';"
echo "    FLUSH PRIVILEGES;"
echo "  (deja al user de la app con privilegios mínimos sobre extragas.*)."
echo "================================================================"
