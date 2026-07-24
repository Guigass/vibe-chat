#!/usr/bin/env bash
# VibeChat restore drill — restores a backup produced by backup.sh into a staging DB.
# NEVER point this at production without an explicit confirm.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BACKUP_PATH="${1:-}"
CONFIRM="${CONFIRM_RESTORE_DRILL:-}"

if [[ -z "$BACKUP_PATH" || ! -d "$BACKUP_PATH" ]]; then
  echo "Usage: CONFIRM_RESTORE_DRILL=yes $0 /path/to/backup_dir"
  echo "Backup dir must contain postgres.dump (from infra/scripts/backup.sh)."
  exit 1
fi

if [[ "$CONFIRM" != "yes" ]]; then
  echo "Refusing to run without CONFIRM_RESTORE_DRILL=yes"
  exit 1
fi

DUMP="$BACKUP_PATH/postgres.dump"
if [[ ! -f "$DUMP" ]]; then
  echo "Missing $DUMP"
  exit 1
fi

TARGET_DB="${RESTORE_DB:-vibechat_restore_drill}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-$(docker compose -f "$ROOT_DIR/compose.yaml" ps -q postgres 2>/dev/null | head -n1 || true)}"
POSTGRES_USER="${POSTGRES_USER:-vibechat}"

echo "==> Restore drill into database: $TARGET_DB"

if [[ -n "$POSTGRES_CONTAINER" ]]; then
  docker exec -i "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TARGET_DB' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $TARGET_DB;
CREATE DATABASE $TARGET_DB OWNER $POSTGRES_USER;
SQL
  docker exec -i "$POSTGRES_CONTAINER" pg_restore -U "$POSTGRES_USER" -d "$TARGET_DB" --clean --if-exists < "$DUMP"
else
  psql -h "${POSTGRES_HOST:-localhost}" -p "${POSTGRES_PORT:-5432}" -U "$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TARGET_DB' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $TARGET_DB;
CREATE DATABASE $TARGET_DB OWNER $POSTGRES_USER;
SQL
  pg_restore -h "${POSTGRES_HOST:-localhost}" -p "${POSTGRES_PORT:-5432}" -U "$POSTGRES_USER" -d "$TARGET_DB" --clean --if-exists "$DUMP"
fi

if [[ -d "$BACKUP_PATH/minio" ]] && command -v mc >/dev/null 2>&1; then
  MINIO_ALIAS="${MINIO_ALIAS:-vibechatlocal}"
  MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://localhost:9000}"
  MINIO_ROOT_USER="${MINIO_ROOT_USER:-minioadmin}"
  MINIO_ROOT_PASSWORD="${MINIO_ROOT_PASSWORD:-minioadmin_dev_password_change_me}"
  DRILL_BUCKET="${RESTORE_MINIO_BUCKET:-vibechat-restore-drill}"
  mc alias set "$MINIO_ALIAS" "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
  mc mb -p "$MINIO_ALIAS/$DRILL_BUCKET" || true
  mc mirror --overwrite "$BACKUP_PATH/minio" "$MINIO_ALIAS/$DRILL_BUCKET"
  echo "==> MinIO drill bucket: $DRILL_BUCKET"
fi

echo "==> Restore drill complete."
echo "Checklist: login, history, attachment open, send message (see docs/operations/backup-restore.md)."
