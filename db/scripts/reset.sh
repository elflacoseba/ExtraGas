#!/usr/bin/env bash
# =============================================================================
# reset.sh — DROP + CREATE de la base de datos. DESTRUCTIVO. Solo desarrollo.
#
# Uso:
#   ./db/scripts/reset.sh                  # reset y re-instala
#   ./db/scripts/reset.sh --keep           # solo borra, no reinstala
#
# Pre-requisito: MySQL corriendo (`brew services start mysql`)
# =============================================================================

set -euo pipefail

MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASS="${MYSQL_PASS:-}"
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_CMD="mysql -u${MYSQL_USER} -h${MYSQL_HOST}"
if [ -n "${MYSQL_PASS}" ]; then
  MYSQL_CMD="${MYSQL_CMD} -p${MYSQL_PASS}"
fi

DB_NAME="extragas"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_SH="${SCRIPT_DIR}/install.sh"

echo "==> ATENCIÓN: este script BORRA todos los datos de '${DB_NAME}'."
read -r -p "    ¿Continuar? (escribí 'si' para confirmar): " CONFIRM
if [ "${CONFIRM}" != "si" ]; then
  echo "    Cancelado."
  exit 0
fi

echo "==> DROP DATABASE ${DB_NAME}..."
${MYSQL_CMD} -e "DROP DATABASE IF EXISTS ${DB_NAME};"

if [ "${1:-}" = "--keep" ]; then
  echo "    --keep: no se reinstalan las migraciones."
  exit 0
fi

echo "==> Re-instalando..."
"${INSTALL_SH}"
