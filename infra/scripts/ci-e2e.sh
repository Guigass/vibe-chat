#!/usr/bin/env bash
# W7-1 / B-075 — run Playwright E2E against a local DevAuth stack (CI or lab).
#
# Starts data plane via compose.yaml (bridge; ignores host-network override),
# boots API + Web on the host, then runs tests/e2e with E2E_AUTH_MODE=devauth.
#
# Usage:
#   ./infra/scripts/ci-e2e.sh
# Env (optional):
#   SKIP_COMPOSE=1     — reuse already-running postgres/redis/minio
#   SKIP_APPS=1        — reuse already-running API (:5080) and Web (:4200)
#   BOOT_ONLY=1        — boot the stack, skip Playwright, leave it running
#                        (used by the UX review automation to drive the browser)
#   E2E_AUTH_MODE      — default devauth
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

export COMPOSE_FILE="${COMPOSE_FILE:-compose.yaml}"
export E2E_AUTH_MODE="${E2E_AUTH_MODE:-devauth}"
export WEB_BASE_URL="${WEB_BASE_URL:-http://localhost:4200}"
export API_BASE_URL="${API_BASE_URL:-http://localhost:5080}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5080}"
export ConnectionStrings__Database="${ConnectionStrings__Database:-Host=localhost;Port=5432;Database=vibechat;Username=vibechat;Password=vibechat_dev_password_change_me}"
# StackExchange.Redis format (not redis://…)
export ConnectionStrings__Redis="${ConnectionStrings__Redis:-localhost:6379}"
export Seed__Enabled="${Seed__Enabled:-true}"
export Authentication__Authority="${Authentication__Authority:-http://localhost:8080/realms/vibechat}"
export Authentication__RequireHttpsMetadata="${Authentication__RequireHttpsMetadata:-false}"
export Minio__Endpoint="${Minio__Endpoint:-localhost:9000}"
export Minio__PublicEndpoint="${Minio__PublicEndpoint:-http://localhost:9000}"
export Minio__AccessKey="${Minio__AccessKey:-minioadmin}"
export Minio__SecretKey="${Minio__SecretKey:-minioadmin_dev_password_change_me}"
export Minio__Bucket="${Minio__Bucket:-vibechat}"
export Minio__UseSsl="${Minio__UseSsl:-false}"

API_PID=""
WEB_PID=""
cleanup() {
  if [[ -n "${API_PID}" ]] && kill -0 "${API_PID}" 2>/dev/null; then
    kill "${API_PID}" 2>/dev/null || true
  fi
  if [[ -n "${WEB_PID}" ]] && kill -0 "${WEB_PID}" 2>/dev/null; then
    kill "${WEB_PID}" 2>/dev/null || true
  fi
}
BOOT_ONLY="${BOOT_ONLY:-0}"
# With BOOT_ONLY the whole point is to leave API and Web up for someone else to use.
if [[ "${BOOT_ONLY}" != "1" ]]; then
  trap cleanup EXIT INT TERM
fi

# O Angular CLI recusa Node abaixo deste piso, independente do "engines" do package.json.
WEB_NODE_MIN="${WEB_NODE_MIN:-22.22.3}"

# true quando $1 >= $2
version_ge() { printf '%s\n%s\n' "$2" "$1" | sort -V -C; }

ensure_web_node() {
  local current=""
  command -v node >/dev/null 2>&1 && current="$(node --version 2>/dev/null | tr -d 'v')"
  if [[ -n "${current}" ]] && version_ge "${current}" "${WEB_NODE_MIN}"; then
    echo "==> node ${current} (>= ${WEB_NODE_MIN})"
    return 0
  fi

  echo "==> node ${current:-ausente} não atende ao piso ${WEB_NODE_MIN}; tentando nvm"
  export NVM_DIR="${NVM_DIR:-${HOME}/.nvm}"
  if [[ ! -s "${NVM_DIR}/nvm.sh" ]]; then
    echo "nvm não encontrado; instale Node >= ${WEB_NODE_MIN} para rodar o web" >&2
    return 1
  fi
  # shellcheck disable=SC1091
  source "${NVM_DIR}/nvm.sh"

  # Só versões realmente instaladas — "nvm ls" também imprime aliases não instalados.
  local candidate
  candidate="$(ls -1 "${NVM_DIR}/versions/node" 2>/dev/null | tr -d 'v' | sort -V -u | tail -1 || true)"

  if [[ -z "${candidate}" ]] || ! version_ge "${candidate}" "${WEB_NODE_MIN}"; then
    echo "==> instalando Node ${WEB_NODE_MIN} via nvm"
    nvm install "${WEB_NODE_MIN}" || return 1
    candidate="${WEB_NODE_MIN}"
  fi

  nvm use "${candidate}" >/dev/null || return 1
  hash -r
  echo "==> node $(node --version) via nvm"
}

wait_http() {
  local url="$1"
  local label="$2"
  local timeout="${3:-120}"
  local deadline=$((SECONDS + timeout))
  echo "Waiting for ${label} at ${url} (timeout=${timeout}s)…"
  while (( SECONDS < deadline )); do
    if curl -sf "$url" >/dev/null 2>&1; then
      echo "  ✓ ${label}"
      return 0
    fi
    sleep 2
  done
  echo "Timed out waiting for ${label}: ${url}" >&2
  return 1
}

if [[ "${SKIP_COMPOSE:-0}" != "1" ]]; then
  echo "==> Data plane (COMPOSE_FILE=${COMPOSE_FILE})"
  docker compose up -d postgres redis minio
  ./infra/scripts/wait-for-healthy.sh postgres redis minio
  # One-shot bucket ensure (waits for exit)
  docker compose up --no-deps createbucket
fi

if [[ "${SKIP_APPS:-0}" != "1" ]]; then
  if ! curl -sf "${API_BASE_URL}/health" >/dev/null 2>&1; then
    echo "==> Starting API on ${ASPNETCORE_URLS}"
    (
      cd apps/api
      dotnet run --no-launch-profile -c Release
    ) > /tmp/vibechat-e2e-api.log 2>&1 &
    API_PID=$!
  else
    echo "==> Reusing API at ${API_BASE_URL}"
  fi

  if ! curl -sf "${WEB_BASE_URL}" >/dev/null 2>&1; then
    echo "==> Starting Web on ${WEB_BASE_URL}"
    ensure_web_node
    if [[ ! -d apps/web/node_modules ]]; then
      npm ci --prefix apps/web
    fi
    (
      cd apps/web
      export NG_CLI_ANALYTICS=false
      npm start -- --host 0.0.0.0 --port 4200
    ) > /tmp/vibechat-e2e-web.log 2>&1 &
    WEB_PID=$!
  else
    echo "==> Reusing Web at ${WEB_BASE_URL}"
  fi

  wait_http "${API_BASE_URL}/health" "API /health" 180
  wait_http "${WEB_BASE_URL}" "Web" 180
fi

if [[ "${BOOT_ONLY}" == "1" ]]; then
  echo "==> Stack no ar (BOOT_ONLY=1) — Playwright não roda e os processos seguem vivos"
  echo "    API: ${API_BASE_URL}  (log /tmp/vibechat-e2e-api.log)"
  echo "    Web: ${WEB_BASE_URL}  (log /tmp/vibechat-e2e-web.log)"
  echo "    Login sem Keycloak: botões DevAuth ou header X-Dev-User: alice|bob|demo"
  exit 0
fi

echo "==> Playwright (${E2E_AUTH_MODE})"
if [[ ! -d tests/e2e/node_modules ]]; then
  npm ci --prefix tests/e2e
fi
if [[ "${CI:-}" == "true" ]]; then
  npx --prefix tests/e2e playwright install --with-deps chromium
else
  npx --prefix tests/e2e playwright install chromium
fi
npm test --prefix tests/e2e

echo "E2E OK."
