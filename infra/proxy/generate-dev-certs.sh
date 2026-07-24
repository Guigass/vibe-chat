#!/usr/bin/env bash
# Idempotent self-signed TLS certs for local Compose profile `proxy`.
# Output: infra/proxy/certs/{fullchain.pem,privkey.pem}
# NEVER use these certificates in production — replace with real certs / ACME.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CERT_DIR="${ROOT_DIR}/infra/proxy/certs"
DAYS="${TLS_DEV_CERT_DAYS:-825}"
CN="${TLS_DEV_CERT_CN:-localhost}"

mkdir -p "${CERT_DIR}"

if [[ -f "${CERT_DIR}/fullchain.pem" && -f "${CERT_DIR}/privkey.pem" ]]; then
  echo "Dev TLS certs already present in ${CERT_DIR}"
  exit 0
fi

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate dev certificates" >&2
  exit 1
fi

echo "Generating self-signed TLS cert for CN=${CN} (${DAYS} days)..."
openssl req -x509 -nodes -newkey rsa:2048 \
  -keyout "${CERT_DIR}/privkey.pem" \
  -out "${CERT_DIR}/fullchain.pem" \
  -days "${DAYS}" \
  -subj "/CN=${CN}" \
  -addext "subjectAltName=DNS:localhost,DNS:*.localhost,IP:127.0.0.1"

chmod 600 "${CERT_DIR}/privkey.pem"
chmod 644 "${CERT_DIR}/fullchain.pem"
echo "Wrote ${CERT_DIR}/fullchain.pem and privkey.pem"
echo "Replace with real certificates before any public deploy (placeholders only)."
