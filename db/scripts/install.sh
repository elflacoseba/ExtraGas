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
# migraciones. Las migraciones estructurales usan IF NOT EXISTS / IF EXISTS /
# CREATE OR REPLACE / INSERT IGNORE / DROP IF EXISTS, según corresponda.
# Si una migración nueva no sigue este patrón, falla en re-run.
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
# Mostramos el comando sin el password (queda -p solo, así el shell lo pide).
echo "Para conectarte: ${MYSQL_CMD%${MYSQL_PASS}} ${DB_NAME}"
