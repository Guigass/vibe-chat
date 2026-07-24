#!/usr/bin/env bash
# VibeChat backup helper — Postgres dump + optional MinIO mirror.
# Idempotent directory creation. Uses env from .env / Compose defaults.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
OUT_DIR="${BACKUP_DIR:-$ROOT_DIR/.backups}/$TIMESTAMP"
mkdir -p "$OUT_DIR"

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-$(docker compose -f "$ROOT_DIR/compose.yaml" ps -q postgres 2>/dev/null | head -n1 || true)}"
POSTGRES_DB="${POSTGRES_DB:-vibechat}"
POSTGRES_USER="${POSTGRES_USER:-vibechat}"
MINIO_BUCKET="${MINIO_BUCKET:-vibechat}"
MINIO_ALIAS="${MINIO_ALIAS:-vibechatlocal}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://localhost:9000}"
MINIO_ROOT_USER="${MINIO_ROOT_USER:-minioadmin}"
MINIO_ROOT_PASSWORD="${MINIO_ROOT_PASSWORD:-minioadmin_dev_password_change_me}"

echo "==> Backup dir: $OUT_DIR"

if [[ -n "$POSTGRES_CONTAINER" ]]; then
  echo "==> Dumping Postgres from container $POSTGRES_CONTAINER"
  docker exec "$POSTGRES_CONTAINER" pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB" > "$OUT_DIR/postgres.dump"
else
  echo "==> Dumping Postgres via local pg_dump ($DATABASE_URL / localhost)"
  if [[ -n "${DATABASE_URL:-}" ]]; then
    pg_dump "$DATABASE_URL" -Fc -f "$OUT_DIR/postgres.dump"
  else
    pg_dump -h "${POSTGRES_HOST:-localhost}" -p "${POSTGRES_PORT:-5432}" -U "$POSTGRES_USER" -Fc -f "$OUT_DIR/postgres.dump" "$POSTGRES_DB"
  fi
fi

if command -v mc >/dev/null 2>&1; then
  echo "==> Mirroring MinIO bucket $MINIO_BUCKET"
  mc alias set "$MINIO_ALIAS" "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
  mkdir -p "$OUT_DIR/minio"
  mc mirror --overwrite "$MINIO_ALIAS/$MINIO_BUCKET" "$OUT_DIR/minio" || echo "WARN: MinIO mirror skipped/failed"
else
  echo "WARN: mc (MinIO client) not found — skipping object storage backup"
fi

cat > "$OUT_DIR/MANIFEST.txt" <<EOF
created_at=$TIMESTAMP
postgres_db=$POSTGRES_DB
minio_bucket=$MINIO_BUCKET
host=$(hostname)
EOF

echo "==> Done: $OUT_DIR"
ls -lah "$OUT_DIR"
