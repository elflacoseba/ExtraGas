#!/usr/bin/env bash
# =============================================================================
# sonar-analyze.sh — SonarQube analysis with real .NET coverage
# =============================================================================
#
# Por qué este script existe (issue #134):
#
#   El plugin `opencode-sonarqube` corre el scanner Java estándar. Ese scanner:
#     - Sabe analizar .cs/.js/.cshtml
#     - NO genera .sonarqube/bin/targets/SonarQube.Integration.targets
#     - NO dispara coverlet.collector durante el build
#     - Reporta new_coverage=0% aunque el coverage.opencover.xml exista
#
#   Este script usa `dotnet-sonarscanner` 11.x, que SÍ integra con MSBuild:
#     1. `begin` injecta el target SonarQube.Integration.targets en el build
#     2. `dotnet build` ejecuta el target → cubre los .cs compilados
#     3. `dotnet test --collect:"XPlat Code Coverage"` corre los tests y
#        genera coverage.opencover.xml vía coverlet.collector
#     4. `end` sube el reporte al server junto con la cobertura
#
# Requisitos:
#   - dotnet-sonarscanner 11.x instalado globalmente (dotnet tool install -g dotnet-sonarscanner)
#   - SONAR_TOKEN o SONAR_PASSWORD configurado en el entorno (recomendado: SONAR_TOKEN;
#     password legacy puede fallar en `end` con "Not authorized" — ver issue #134)
#   - SONAR_HOST_URL apuntando al server (default: https://sonarqube.elflacoseba.online)
#
# Uso:
#   ./scripts/sonar-analyze.sh                                    # rama actual contra develop
#   ./scripts/sonar-analyze.sh feature/mi-rama develop             # branch y base explícitos
#   SONAR_HOST_URL=http://localhost:9000 ./scripts/sonar-analyze.sh
#
# Salida esperada:
#   - .sonarqube/bin/targets/SonarQube.Integration.targets (generado)
#   - tests/ExtraGasMVC.Tests/TestResults/<guid>/coverage.opencover.xml
#   - Quality Gate actualizado en el server
# =============================================================================

set -euo pipefail

# ---------- Configuración ----------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

HEAD_BRANCH="${1:-$(git rev-parse --abbrev-ref HEAD)}"
BASE_BRANCH="${2:-develop}"

# Resolver token: SONAR_TOKEN tiene prioridad; SONAR_PASSWORD es fallback legacy.
#
# dotnet-sonarscanner 11.x en SonarQube exige `sonar.token` (no `sonar.login`,
# que es para SonarCloud). Si solo tenés password, primero generá un token en
# el server: User > Security > Generate Tokens, y exportá SONAR_TOKEN.
if [[ -n "${SONAR_TOKEN:-}" ]]; then
    SONAR_AUTH="/d:sonar.token=${SONAR_TOKEN}"
    AUTH_SOURCE="SONAR_TOKEN"
elif [[ -n "${SONAR_PASSWORD:-}" ]]; then
    # Fallback legacy: tratamos el password como si fuera token. En la práctica
    # las passwords no funcionan en `end` (devuelve "Not authorized"). Es solo
    # un puente hasta que generes SONAR_TOKEN.
    SONAR_AUTH="/d:sonar.token=${SONAR_PASSWORD}"
    AUTH_SOURCE="SONAR_PASSWORD (legacy — puede fallar en 'end')"
else
    echo "ERROR: ni SONAR_TOKEN ni SONAR_PASSWORD están definidos." >&2
    echo "       Generar token en el server (User > Security > Generate Tokens) y" >&2
    echo "       export SONAR_TOKEN=squ_..." >&2
    exit 1
fi

SONAR_HOST_URL="${SONAR_HOST_URL:-https://sonarqube.elflacoseba.online}"

# ---------- Pre-flight ----------
command -v dotnet-sonarscanner >/dev/null 2>&1 || {
    echo "ERROR: dotnet-sonarscanner no está instalado." >&2
    echo "       dotnet tool install -g dotnet-sonarscanner" >&2
    exit 1
}

echo "=== SonarQube Analysis ==="
echo "Repo:           ${REPO_ROOT}"
echo "Branch:         ${HEAD_BRANCH} (base: ${BASE_BRANCH})"
echo "Server:         ${SONAR_HOST_URL}"
echo "Auth:           ${AUTH_SOURCE}"
echo ""

# ---------- Renombrar sonar-project.properties temporalmente ----------
# El .NET scanner falla si encuentra `sonar-project.properties` en la raíz
# (asume que es un proyecto Java). Lo movemos a un nombre no conflictivo y
# lo restauramos al final, incluso si el script falla mid-flight.
PROPS_FILE="sonar-project.properties"
PROPS_BACKUP=".sonar-project.properties.scanner-backup"
PROPS_RENAMED=false

rename_props() {
    if [[ -f "${PROPS_FILE}" && "${PROPS_RENAMED}" == "false" ]]; then
        mv "${PROPS_FILE}" "${PROPS_BACKUP}"
        PROPS_RENAMED=true
        echo "[1/5] sonar-project.properties renombrado a ${PROPS_BACKUP}"
    fi
}

restore_props() {
    if [[ "${PROPS_RENAMED}" == "true" && -f "${PROPS_BACKUP}" ]]; then
        mv "${PROPS_BACKUP}" "${PROPS_FILE}"
        echo "[cleanup] ${PROPS_FILE} restaurado"
    fi
}
trap restore_props EXIT

# ---------- Flujo begin → build → test → end ----------
rename_props

echo "[2/5] dotnet sonarscanner begin"
dotnet sonarscanner begin \
    /k:"extragas" \
    /n:"extragas" \
    /v:"1.0" \
    /d:sonar.host.url="${SONAR_HOST_URL}" \
    /d:sonar.branch.name="${HEAD_BRANCH}" \
    /d:sonar.branch.target="${BASE_BRANCH}" \
    /d:sonar.sources="src" \
    /d:sonar.tests="tests" \
    /d:sonar.test.inclusions="**/*Tests.cs,**/*Tests.csproj" \
    /d:sonar.cs.opencover.reportsPaths="tests/ExtraGasMVC.Tests/TestResults/*/coverage.opencover.xml" \
    ${SONAR_AUTH}

echo "[3/5] dotnet build"
dotnet build "${REPO_ROOT}/ExtraGasMVC.sln" --nologo

echo "[4/5] dotnet test --collect:\"XPlat Code Coverage\""
dotnet test "${REPO_ROOT}/tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj" \
    --nologo \
    --collect:"XPlat Code Coverage"

echo "[5/5] dotnet sonarscanner end"
dotnet sonarscanner end ${SONAR_AUTH}

echo ""
echo "=== Análisis subido a ${SONAR_HOST_URL}/dashboard?id=extragas ==="
