#!/bin/sh
set -eu

schema="${KEYCLOAK_DB_SCHEMA:-public}"
owner="${KEYCLOAK_DB_USER:-keycloak}"

case "$schema" in
  ''|*[!a-zA-Z0-9_]*)
    echo "KEYCLOAK_DB_SCHEMA must contain only letters, numbers, and underscores" >&2
    exit 1
    ;;
esac

case "$owner" in
  ''|*[!a-zA-Z0-9_]*)
    echo "KEYCLOAK_DB_USER must contain only letters, numbers, and underscores" >&2
    exit 1
    ;;
esac

psql -v ON_ERROR_STOP=1 -v schema="$schema" -v owner="$owner" <<'SQL'
SELECT format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I', :'schema', :'owner') \gexec
SELECT format('GRANT ALL ON SCHEMA %I TO %I', :'schema', :'owner') \gexec
SQL

echo "Keycloak schema ready."
