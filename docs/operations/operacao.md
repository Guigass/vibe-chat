# Guia de Operação — VibeChat

## Escopo

Operação de uma instância self-hosted em fase 1 (**Docker Compose** oficial — D-05). Kubernetes não é obrigatório (ADR-017). Sem SLA comercial; RPO/RTO best effort com backup diário Postgres (D-08).

**Runbooks acionáveis (W5-4):** [`runbooks/README.md`](./runbooks/README.md) — incidentes, backup/restore drill, TLS/proxy, upgrade.

## Componentes críticos

| Componente | Criticidade | Notas |
|------------|-------------|-------|
| PostgreSQL | P0 | Source of truth |
| API + Worker | P0 | App |
| Keycloak | P0 | Login |
| Redis | P1 | Degrada presence/realtime multi-instância se cair |
| MinIO | P1 | Anexos |
| Observabilidade | P2 | Cego sem ela, app funciona |

## Health checks

- API: `GET /health` (liveness), `GET /ready` (Postgres + Redis reachability)
- Worker: heartbeat métrica `worker_alive` / health endpoint se exposto
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
- Procedimento de offboarding de tenant (export + purge — política legal pendente)
