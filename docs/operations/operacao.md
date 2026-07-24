# Guia de Operação — VibeChat

## Escopo

Operação de uma instância self-hosted em fase 1 (**Docker Compose** oficial — D-05). Kubernetes não é obrigatório (ADR-017). Sem SLA comercial; RPO/RTO best effort com backup diário Postgres (D-08).

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

Ver `troubleshooting.md`.

## Segurança operacional

- Rede: não expor Postgres/Redis/MinIO à internet
- Keycloak admin console restrito
- Secrets rotacionáveis
- Least privilege nas roles DB

## Upgrades

1. Ler changelog / ADRs impactados
2. Backup Postgres + MinIO
3. Migrar DB (job migrate)
4. Deploy Worker → API → Web
5. Verificar `/ready` e envio de mensagem de teste

## Multi-tenant ops

- Rate-limit por tenant
- Quotas de storage (fase 2)
- Procedimento de offboarding de tenant (export + purge — política legal pendente)
