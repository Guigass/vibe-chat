#!/bin/bash
# Creates a dedicated Keycloak database/role using compose env vars.
# Passwords are DEMO values from .env — never production secrets.
set -euo pipefail

KEYCLOAK_DB="${KEYCLOAK_DB:-keycloak}"
KEYCLOAK_DB_USER="${KEYCLOAK_DB_USER:-keycloak}"
KEYCLOAK_DB_PASSWORD="${KEYCLOAK_DB_PASSWORD:-keycloak_dev_password_change_me}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
  DO \$\$
  BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${KEYCLOAK_DB_USER}') THEN
      CREATE ROLE ${KEYCLOAK_DB_USER} LOGIN PASSWORD '${KEYCLOAK_DB_PASSWORD}';
    END IF;
  END
  \$\$;

  SELECT 'CREATE DATABASE ${KEYCLOAK_DB} OWNER ${KEYCLOAK_DB_USER}'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${KEYCLOAK_DB}')\gexec

  GRANT ALL PRIVILEGES ON DATABASE ${KEYCLOAK_DB} TO ${KEYCLOAK_DB_USER};
EOSQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$KEYCLOAK_DB" <<-EOSQL
  GRANT ALL ON SCHEMA public TO ${KEYCLOAK_DB_USER};
  ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO ${KEYCLOAK_DB_USER};
  ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO ${KEYCLOAK_DB_USER};
EOSQL

echo "Keycloak database '${KEYCLOAK_DB}' ready."
