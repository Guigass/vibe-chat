#!/usr/bin/env bash
# Wait until selected Compose services report healthy (or exited successfully).
# Usage:
#   ./infra/scripts/wait-for-healthy.sh
#   ./infra/scripts/wait-for-healthy.sh postgres redis keycloak minio
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

SERVICES=("$@")
if [[ ${#SERVICES[@]} -eq 0 ]]; then
  SERVICES=(postgres redis keycloak minio)
fi

TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-180}"
INTERVAL_SECONDS="${WAIT_INTERVAL_SECONDS:-3}"
DEADLINE=$((SECONDS + TIMEOUT_SECONDS))

echo "Waiting for healthy services (timeout=${TIMEOUT_SECONDS}s): ${SERVICES[*]}"

is_ready() {
  local service="$1"
  local status
  status="$(docker compose ps --format json "$service" 2>/dev/null | head -n1 || true)"
  if [[ -z "$status" ]]; then
    # Fallback for older compose without json format
    local line
    line="$(docker compose ps "$service" 2>/dev/null | tail -n1 || true)"
    [[ "$line" == *"(healthy)"* ]] && return 0
    [[ "$line" == *"exited"* ]] && [[ "$line" == *"0"* ]] && return 0
    return 1
  fi

  # Prefer Health when present; otherwise State==running is accepted for one-shots handled below
  if command -v jq >/dev/null 2>&1; then
    local health state
    health="$(echo "$status" | jq -r '.Health // empty')"
    state="$(echo "$status" | jq -r '.State // empty')"
    if [[ "$health" == "healthy" ]]; then
      return 0
    fi
    if [[ "$service" == "createbucket" && "$state" == "exited" ]]; then
      return 0
    fi
    return 1
  fi

  [[ "$status" == *'"Health":"healthy"'* ]] && return 0
  return 1
}

pending=("${SERVICES[@]}")
while (( SECONDS < DEADLINE )); do
  still_pending=()
  for svc in "${pending[@]}"; do
    if is_ready "$svc"; then
      echo "  ✓ $svc"
    else
      still_pending+=("$svc")
    fi
  done

  if [[ ${#still_pending[@]} -eq 0 ]]; then
    echo "All services healthy."
    exit 0
  fi

  pending=("${still_pending[@]}")
  sleep "$INTERVAL_SECONDS"
done

echo "Timed out waiting for: ${pending[*]}" >&2
docker compose ps "${SERVICES[@]}" || true
exit 1
