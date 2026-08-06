# Runbook — TLS / proxy (W5-2)

Compose profile `proxy` (nginx) termina TLS e encaminha `/`, `/api/`, `/hubs/`.  
Detalhe de referência também em [`../operacao.md`](../operacao.md).

## Lab local

```bash
task proxy:certs          # ou: ./infra/proxy/generate-dev-certs.sh
docker compose --profile apps --profile proxy up -d
# HTTPS: https://localhost:8443
# HTTP :8088 → redirect HTTPS
curl -k https://localhost:8443/proxy-healthz   # via host mapeado — ver compose
```

Arquivos:

| Path | Papel |
|------|-------|
| `infra/proxy/nginx.conf` | Config referência |
| `infra/proxy/certs/` | Self-signed local (gitignore) |
| `.env.example` | Placeholders `TLS_*` / `CHANGE_ME` |

## Produção (Compose)

1. Obter `fullchain.pem` + `privkey.pem` (Let's Encrypt, PKI interna, etc.)
2. Montar no serviço proxy (substituir self-signed)
3. Garantir API com forwarded headers (`X-Forwarded-*` — já habilitado)
4. **Não** expor Postgres / Redis / MinIO publicamente
5. Restringir Keycloak admin console à rede ops
6. Validar:
   - HTTPS na SPA
   - `POST /api/v1/...` via proxy
   - SignalR `/hubs/chat` com WebSocket upgrade
   - HSTS presente (nginx referência já envia)
   - `Content-Security-Policy` via `infra/nginx/security-headers.conf` (B-077); em produção,
     estender `connect-src`/`frame-src` com URLs HTTPS públicas de Keycloak e MinIO

## Sintomas comuns

| Sintoma | Ação |
|---------|------|
| Cert inválido no browser | Esperado com self-signed; em prod montar cert real |
| 502 no `/api/` | API não healthy / nome upstream / profile `apps` |
| SignalR não conecta | Conferir `Upgrade` / `Connection` no location `/hubs/` |
| Mixed content | SPA e API devem usar mesmo scheme HTTPS atrás do proxy |
| Issuer Keycloak errado | URL pública do realm vs URL interna do container |

## Rollback

```bash
# Remover profile proxy; voltar a expor API/Web só na rede interna/lab
docker compose --profile apps up -d
# Usuários passam a usar portas diretas (5080/4200) — só em lab
```

Em produção: reverter para o cert/config anterior mantendo o proxy no ar se possível.
