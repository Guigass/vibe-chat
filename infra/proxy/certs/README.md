# Certificados TLS (local / referência)

Arquivos gerados por `infra/proxy/generate-dev-certs.sh` (self-signed) **não** são versionados.

Em produção, monte certificados reais (Let's Encrypt / PKI interna) como:

- `fullchain.pem`
- `privkey.pem`

Secrets: nunca commitar chaves; use placeholders `CHANGE_ME` / `*_change_me` no `.env`.
