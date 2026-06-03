#!/usr/bin/env bash
# =============================================================================
# install.sh — Crea la base de datos, aplica todas las migraciones y carga seed.
#
# Uso:
#   ./db/scripts/install.sh                # usa root sin password
#   MYSQL_USER=miuser MYSQL_PASS=mipass ./db/scripts/install.sh
#
# Pre-requisito: MySQL corriendo (`brew services start mysql`)
#
# Idempotente: si la BD ya existe, conserva los datos y reaplica las
# migraciones (los CREATE usan IF NOT EXISTS, los INSERT de seed pueden
# fallar por UNIQUE — eso es esperable).
# =============================================================================

set -euo pipefail

# --- Configuración -----------------------------------------------------------
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASS="${MYSQL_PASS:-}"
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_CMD="mysql -u${MYSQL_USER} -h${MYSQL_HOST}"
if [ -n "${MYSQL_PASS}" ]; then
  MYSQL_CMD="${MYSQL_CMD} -p${MYSQL_PASS}"
else
  MYSQL_CMD="${MYSQL_CMD}"  # sin password (instalación local de Homebrew)
fi

DB_NAME="extragas"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
MIGRATIONS_DIR="${PROJECT_ROOT}/db/migrations"

# --- Pre-checks --------------------------------------------------------------
echo "==> Verificando conexión a MySQL..."
if ! ${MYSQL_CMD} -e "SELECT VERSION();" >/dev/null 2>&1; then
  echo "    ERROR: no se puede conectar a MySQL. ¿Está corriendo el servicio?" >&2
  echo "    Probá: brew services start mysql" >&2
  exit 1
fi
${MYSQL_CMD} -e "SELECT VERSION();" 2>/dev/null

# --- Crear BD ----------------------------------------------------------------
echo "==> Creando base de datos ${DB_NAME} (si no existe)..."
${MYSQL_CMD} -e "CREATE DATABASE IF NOT EXISTS ${DB_NAME} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"

# --- Correr migraciones en orden ---------------------------------------------
echo "==> Aplicando migraciones desde ${MIGRATIONS_DIR}..."
shopt -s nullglob
for migration_file in "${MIGRATIONS_DIR}"/*.sql; do
  filename="$(basename "${migration_file}")"
  echo "    -> ${filename}"
  (cd "${PROJECT_ROOT}" && ${MYSQL_CMD} "${DB_NAME}" < "${migration_file}")
done
shopt -u nullglob

# --- Verificación final ------------------------------------------------------
echo "==> Verificación final..."
TABLE_COUNT=$(${MYSQL_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='${DB_NAME}' AND table_type='BASE TABLE';" 2>/dev/null)
VIEW_COUNT=$(${MYSQL_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='${DB_NAME}';" 2>/dev/null)
TRIGGER_COUNT=$(${MYSQL_CMD} "${DB_NAME}" -N -e "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema='${DB_NAME}';" 2>/dev/null)

echo ""
echo "================================================================"
echo "  Instalación completa"
echo "  BD:                ${DB_NAME}"
echo "  Tablas:            ${TABLE_COUNT}"
echo "  Vistas:            ${VIEW_COUNT}"
echo "  Triggers:          ${TRIGGER_COUNT}"
echo "================================================================"
echo ""
echo "Para conectarte: ${MYSQL_CMD} ${DB_NAME}"
