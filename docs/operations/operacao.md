# Guia de Operação — VibeChat

## Escopo

Operação de uma instância self-hosted em fase 1 (**Docker Compose** oficial — D-05). Kubernetes não é obrigatório (ADR-017). Sem SLA comercial; RPO/RTO best effort com backup diário Postgres (D-08).

**Runbooks acionáveis (W5-4):** [`runbooks/README.md`](./runbooks/README.md) — incidentes, backup/restore drill, TLS/proxy, upgrade.

## Compose apps — caminho oficial (B-074 / W6-8)

Deploy self-host/demo sobe **data plane + api + web + worker** via profile `apps` (fonte: `compose.yaml`, rede bridge). Dev com hot reload continua em `task setup` + `task dev`.

```bash
cp .env.example .env
task apps
# equivalente: docker compose -f compose.yaml --profile apps up -d --build
# espera healthy: postgres redis keycloak minio api web worker
```

| Serviço | Porta host (default) | Health |
|---------|----------------------|--------|
| api | `API_PORT` 5080 | `GET /health`, `GET /ready` |
| web | `WEB_PORT` 4200 | `GET /healthz` (nginx) |
| worker | — | process liveness (PID 1) |

- OIDC: `Authentication__Authority` = issuer público (`KEYCLOAK_ISSUER_URL`); discovery in-network via `Authentication__MetadataAddress` → `keycloak:8080`
- Lab/demo: `ASPNETCORE_ENVIRONMENT=Development` + `SEED_ENABLED=true` (default) aplica migrate/seed no startup
- Produção: `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false`, secrets reais só via `.env` / secret manager (D-04)
- `compose.override.yaml` (host networking) é opcional para DX em ambientes restritos — **não** faz parte do caminho oficial; `task apps` usa só `compose.yaml`

## Componentes críticos

| Componente | Criticidade | Notas |
|------------|-------------|-------|
| PostgreSQL | P0 | Source of truth |
| API + Worker | P0 | App (profile `apps`) |
| Web | P0 | nginx estático (profile `apps`) |
| Keycloak | P0 | Login |
| Redis | P1 | Degrada presence/realtime multi-instância se cair |
| MinIO | P1 | Anexos |
| Observabilidade | P2 | Cego sem ela, app funciona |

## Health checks

- API: `GET /health` (checks), `GET /ready` e `GET /health/ready` (alias ops)
- Web: `GET /healthz`
- Worker: healthcheck Compose (processo vivo); métrica `worker_alive` quando OTel ativo
- Postgres/Redis/Keycloak/MinIO: healthchecks do Compose

## Escala (fase 1)

1. **Vertical** na API/Postgres primeiro
2. **Réplicas de API** com Redis backplane SignalR
3. Worker: 1–N com claim `SKIP LOCKED` no outbox
4. Web: estático atrás de nginx/CDN interno

Sticky sessions: preferir backplane Redis em vez de sticky-only.

## TLS / proxy de referência (W5-2)

Compose profile `proxy` (nginx) termina TLS e encaminha `/`, `/api/`, `/hubs/`. Procedimento completo: [`runbooks/tls-proxy.md`](./runbooks/tls-proxy.md).

```bash
./infra/proxy/generate-dev-certs.sh   # ou: task proxy:certs
docker compose --profile apps --profile proxy up -d
# HTTPS em https://localhost:8443  (HTTP :8088 redireciona)
```

- Config: `infra/proxy/nginx.conf`
- Certs locais em `infra/proxy/certs/` (gitignore; self-signed só para lab)
- Produção: montar `fullchain.pem` + `privkey.pem` reais; placeholders `TLS_*` / `CHANGE_ME` no `.env.example`
- API honra `X-Forwarded-*` (`UseForwardedHeaders`)
- Não expor Postgres/Redis/MinIO publicamente

## Load smoke k6 (W5-3)

```bash
# API Development + seed; caminho DevAuth (X-Dev-User)
task load:smoke
# ou: k6 run -e API_BASE=http://localhost:5080 tests/load/smoke.js
```

Cobre `GET /health`, `GET /me`, history e `POST .../messages` no canal demo.

## Configuração

- Tudo via variáveis de ambiente
- Separar: `ConnectionStrings__`, `OIDC__`, `Redis__`, `S3__`, `Otel__`, `AI__`
- Feature flags: `AI__Enabled=false` default

## Rotina operacional

### Diária

- Alertas Grafana (5xx, outbox lag, saturação Redis/Postgres)
- Disco MinIO e Postgres

### Semanal

- Revisar dead-letter do outbox
- Atualizações de segurança de imagens

### Mensal

- Restaurar backup em ambiente de drill (`backup-restore.md`)
- Revisar usuários admin Keycloak

## Alertas mínimos recomendados

| Alerta | Condição |
|--------|----------|
| API down | health fails 2m |
| Outbox lag | p95 lag > limiar (ex.: 30s) por 5m |
| Postgres connections | > 80% max |
| Redis memory | > 80% |
| Disk | > 85% |
| Error rate | 5xx ratio alto |

## Incidentes comuns

- Diagnóstico por sintoma: [`troubleshooting.md`](./troubleshooting.md)
- Resposta estruturada (P0–P2, segurança, pós-mortem): [`runbooks/incidentes.md`](./runbooks/incidentes.md)

## Segurança operacional

- Rede: não expor Postgres/Redis/MinIO à internet
- Keycloak admin console restrito
- Secrets rotacionáveis
- Least privilege nas roles DB

## Upgrades

Procedimento completo: [`runbooks/upgrade.md`](./runbooks/upgrade.md).

1. Ler changelog / ADRs impactados
2. Backup Postgres + MinIO (`./infra/scripts/backup.sh`)
3. Migrar DB (job migrate)
4. Deploy Worker → API → Web
5. Verificar `/ready` e envio de mensagem de teste

## Multi-tenant ops

- Rate-limit por tenant
- Quotas de storage (fase 2)
- Offboarding de tenant: export compliance via `GET /api/v1/admin/workspaces/{id}/export` (B-046); hard-delete de soft-deletes via retenção configurável (B-047) — `MessageRetention__Enabled=true` no worker + `retention.enabled` no admin settings (default off; sugerido 90 dias)
