#!/usr/bin/env bash
# =============================================================================
# install.sh — Crea la base de datos, aplica todas las migraciones y carga seed.
#
# Uso:
#   ./db/scripts/install.sh                # usa root sin password
#   MYSQL_USER=miuser MYSQL_PASS=mipass ./db/scripts/install.sh
#
# Idempotencia:
#   - Doble capa: cada migración .sql es idempotente por sí misma (guards de
#     information_schema), y además install.sh mantiene una tabla
#     `schema_migrations` con checksum SHA256 de cada archivo aplicado.
#   - Si filename + checksum ya están registrados → skip (no se ejecuta el .sql).
#   - Si filename no está registrado → ejecutar y registrar.
#   - Si filename está registrado pero el checksum cambió → ERROR (drift
#     detection): la migración fue editada después de aplicarse. Hay que
#     restaurar el archivo o escribir una migración nueva.
#
# Migrator user opcional:
#   MYSQL_MIGRATOR_USER=extragas_migrator MYSQL_MIGRATOR_PASS=xxx ./db/scripts/install.sh
#   Si se configura, install.sh usa este user para todas las operaciones
#   (incluyendo SET GLOBAL y CREATE TRIGGER). El user debe tener
#   SYSTEM_VARIABLES_ADMIN + grants sobre extragas.*. Sin esta config,
#   install.sh usa MYSQL_USER (que necesita esos privilegios por su cuenta).
#   Para crear el migrator user, correr una vez como root:
#     MYSQL_USER=root MYSQL_MIGRATOR_PASS=xxx ./db/scripts/setup_migrator_user.sh
#
# Pre-requisito: MySQL corriendo (`brew services start mysql`).
# =============================================================================

set -euo pipefail

# --- Configuración -----------------------------------------------------------
# Si solo se configura MYSQL_MIGRATOR_USER (sin MYSQL_USER), lo usamos también
# para los pasos pre-loop (pre-check + CREATE DATABASE). Así, en un homelab
# donde solo hay user de migraciones, no hace falta duplicar la config.
if [ -z "${MYSQL_USER:-}" ] && [ -n "${MYSQL_MIGRATOR_USER:-}" ]; then
  MYSQL_USER="${MYSQL_MIGRATOR_USER}"
  MYSQL_PASS="${MYSQL_MIGRATOR_PASS:-}"
fi
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASS="${MYSQL_PASS:-}"
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_CMD="mysql -u${MYSQL_USER} -h${MYSQL_HOST}"
if [ -n "${MYSQL_PASS}" ]; then
  MYSQL_CMD="${MYSQL_CMD} -p${MYSQL_PASS}"
else
  MYSQL_CMD="${MYSQL_CMD}"  # sin password (instalación local de Homebrew)
fi

# User opcional para migraciones (con SYSTEM_VARIABLES_ADMIN). Si se configura,
# install.sh lo usa para SET GLOBAL y CREATE TRIGGER en vez de MYSQL_USER.
MYSQL_MIGRATOR_USER="${MYSQL_MIGRATOR_USER:-}"
MYSQL_MIGRATOR_PASS="${MYSQL_MIGRATOR_PASS:-}"
MYSQL_MIGRATOR_CMD=""
if [ -n "${MYSQL_MIGRATOR_USER}" ]; then
  MYSQL_MIGRATOR_CMD="mysql -u${MYSQL_MIGRATOR_USER} -h${MYSQL_HOST}"
  if [ -n "${MYSQL_MIGRATOR_PASS}" ]; then
    MYSQL_MIGRATOR_CMD="${MYSQL_MIGRATOR_CMD} -p${MYSQL_MIGRATOR_PASS}"
  fi
fi

DB_NAME="extragas"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
MIGRATIONS_DIR="${PROJECT_ROOT}/db/migrations"

# Helper: comando mysql efectivo (migrator si está configurado, si no MYSQL_USER).
mysql_cmd() {
  if [ -n "${MYSQL_MIGRATOR_CMD}" ]; then
    echo "${MYSQL_MIGRATOR_CMD}"
  else
    echo "${MYSQL_CMD}"
  fi
}

# --- Pre-checks --------------------------------------------------------------
echo "==> Verificando conexión a MySQL..."
ERROR_OUTPUT=$(${MYSQL_CMD} -e "SELECT VERSION();" 2>&1 >/dev/null)
EXIT_CODE=$?
if [ "${EXIT_CODE}" -ne 0 ]; then
  echo "    ERROR: no se puede conectar a MySQL." >&2
  if echo "${ERROR_OUTPUT}" | grep -qi "Access denied"; then
    echo "    Autenticación rechazada. Verificá MYSQL_USER y MYSQL_PASS." >&2
  elif echo "${ERROR_OUTPUT}" | grep -qi "Can't connect to MySQL server"; then
    echo "    El servicio MySQL no responde en ${MYSQL_HOST}:3306." >&2
    if [ "${MYSQL_HOST}" = "localhost" ] || [ "${MYSQL_HOST}" = "127.0.0.1" ]; then
      echo "    Si es local: brew services start mysql" >&2
    else
      echo "    Verificá conectividad de red y que el server escuche en TCP." >&2
    fi
  else
    echo "    Detalle: ${ERROR_OUTPUT}" >&2
  fi
  exit 1
fi
${MYSQL_CMD} -e "SELECT VERSION();" 2>/dev/null
if [ -n "${MYSQL_MIGRATOR_USER}" ]; then
  echo "    Migrator user: ${MYSQL_MIGRATOR_USER}@${MYSQL_HOST} (con SYSTEM_VARIABLES_ADMIN)"
else
  echo "    Modo: MYSQL_USER (${MYSQL_USER}) ejecuta también SET GLOBAL — debe tener SYSTEM_VARIABLES_ADMIN."
fi

# --- Crear BD ----------------------------------------------------------------
echo "==> Creando base de datos ${DB_NAME} (si no existe)..."
${MYSQL_CMD} -e "CREATE DATABASE IF NOT EXISTS ${DB_NAME} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"

# --- Bootstrap defensivo: tabla schema_migrations ----------------------------
# La migración 20260824_000001_create_schema_migrations.sql también la crea,
# pero necesitamos que exista ANTES del loop para poder consultarla.
EFFECTIVE_CMD=$(mysql_cmd)
echo "==> Asegurando tabla schema_migrations (bootstrap defensivo)..."
${EFFECTIVE_CMD} "${DB_NAME}" <<'EOF'
CREATE TABLE IF NOT EXISTS `schema_migrations` (
  `filename`    VARCHAR(255) NOT NULL,
  `checksum`    CHAR(64)     NOT NULL,
  `applied_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`filename`),
  KEY `idx_schema_migrations_applied_at` (`applied_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Registro de migraciones aplicadas (idempotencia real)';
EOF

# --- Detectar herramienta SHA256 --------------------------------------------
if command -v sha256sum >/dev/null 2>&1; then
  compute_sha256() { sha256sum "$1" | awk '{print $1}'; }
elif command -v shasum >/dev/null 2>&1; then
  compute_sha256() { shasum -a 256 "$1" | awk '{print $1}'; }
else
  echo "    ERROR: no se encontró sha256sum ni shasum — instalá coreutils (Linux) o usá macOS" >&2
  exit 1
fi

# --- Correr migraciones con skip-by-checksum ---------------------------------
echo "==> Aplicando migraciones desde ${MIGRATIONS_DIR}..."
shopt -s nullglob
for migration_file in "${MIGRATIONS_DIR}"/*.sql; do
  filename="$(basename "${migration_file}")"
  checksum=$(compute_sha256 "${migration_file}")

  # Consultar schema_migrations (registrado o no).
  # Usamos -N para output sin cabecera y || true para tolerar tabla vacía / error transitorio.
  applied_checksum=$(${EFFECTIVE_CMD} "${DB_NAME}" -N -e \
    "SELECT checksum FROM schema_migrations WHERE filename = '${filename}';" 2>/dev/null || true)

  if [ -z "${applied_checksum}" ]; then
    # No aplicada: ejecutar y registrar.
    echo "    -> ${filename}"
    (cd "${PROJECT_ROOT}" && ${EFFECTIVE_CMD} "${DB_NAME}" < "${migration_file}")
    ${EFFECTIVE_CMD} "${DB_NAME}" -e \
      "INSERT INTO schema_migrations (filename, checksum) VALUES ('${filename}', '${checksum}') \
       ON DUPLICATE KEY UPDATE checksum=VALUES(checksum), applied_at=CURRENT_TIMESTAMP;" \
      2>/dev/null
  elif [ "${applied_checksum}" = "${checksum}" ]; then
    # Aplicada con mismo checksum: skip.
    echo "    -> ${filename} (already applied)"
  else
    # Drift: el archivo fue modificado después de aplicarse.
    echo "    -> ${filename} ERROR: checksum drift detected" >&2
    echo "       registrado: ${applied_checksum}" >&2
    echo "       actual:     ${checksum}" >&2
    echo "       La migración fue modificada después de aplicarse." >&2
    echo "       Para continuar: restaurar el archivo original o escribir una migración nueva." >&2
    exit 1
  fi
done
shopt -u nullglob

# --- Verificación final ------------------------------------------------------
echo "==> Verificación final..."
TABLE_COUNT=$(${EFFECTIVE_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='${DB_NAME}' AND table_type='BASE TABLE';" 2>/dev/null)
VIEW_COUNT=$(${EFFECTIVE_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='${DB_NAME}';" 2>/dev/null)
TRIGGER_COUNT=$(${EFFECTIVE_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema='${DB_NAME}';" 2>/dev/null)
APPLIED_MIGRATIONS=$(${EFFECTIVE_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM schema_migrations;" 2>/dev/null)

echo ""
echo "================================================================"
echo "  Instalación completa"
echo "  BD:                ${DB_NAME}"
echo "  Tablas:            ${TABLE_COUNT}"
echo "  Vistas:            ${VIEW_COUNT}"
echo "  Triggers:          ${TRIGGER_COUNT}"
echo "  Migraciones:       ${APPLIED_MIGRATIONS} registradas"
echo "================================================================"
echo ""
# Mostramos el comando sin el password (queda -p solo, así el shell lo pide).
echo "Para conectarte: ${MYSQL_CMD%${MYSQL_PASS}} ${DB_NAME}"
