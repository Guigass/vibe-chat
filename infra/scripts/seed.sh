#!/usr/bin/env bash
# Placeholder seed script — calls the API development seed endpoint.
# Expected to create: tenant acme, workspace, #geral channel, alice/bob memberships.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  set -a && source .env && set +a
fi

SEED_API_URL="${SEED_API_URL:-http://localhost:5080/api/dev/seed}"
WAIT_FIRST="${SEED_WAIT_FOR_HEALTHY:-1}"

if [[ "$WAIT_FIRST" == "1" ]]; then
  echo "Ensuring infra is healthy before seed..."
  ./infra/scripts/wait-for-healthy.sh postgres redis keycloak minio || {
    echo "Warning: health wait failed; attempting seed anyway." >&2
  }
fi

echo "Seeding via POST ${SEED_API_URL}"
HTTP_CODE="$(curl -sS -o /tmp/vibechat-seed-response.json -w '%{http_code}' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json' \
  "${SEED_API_URL}" \
  -d '{}' || true)"

if [[ "$HTTP_CODE" =~ ^2 ]]; then
  echo "Seed succeeded (HTTP ${HTTP_CODE})."
  cat /tmp/vibechat-seed-response.json 2>/dev/null || true
  echo
  exit 0
fi

echo "Seed endpoint not ready or failed (HTTP ${HTTP_CODE:-none})." >&2
echo "This is expected until the API implements POST /api/dev/seed." >&2
if [[ -f /tmp/vibechat-seed-response.json ]]; then
  cat /tmp/vibechat-seed-response.json >&2 || true
  echo >&2
fi
exit 1
