# Backlog Priorizado — VibeChat

Prioridade: **P0** (fatia vertical) → **P1** (MVP usável) → **P2** (diferenciação) → **P3** (depois).

## P0 — Bloqueadores da fatia vertical

| ID | Item | Notas |
|----|------|-------|
| B-001 | Compose + seeds Keycloak/Postgres | **Done** |
| B-002 | OIDC login web + API JWT | **Done** |
| B-003 | TenantContext + Directory mínimo | **Done** |
| B-004 | Send message + seq + idempotency | **Done** |
| B-005 | Outbox + Worker | **Done** |
| B-006 | SignalR deliver message.created | **Done** |
| B-007 | History API | **Done** |
| B-008 | UI channel + composer | **Done** |
| B-009 | RLS + testes security | **Done** |
| B-010 | Health/ready + CI básica | **Done** |

## P1 — MVP usável em empresa pequena

| ID | Item | Notas |
|----|------|-------|
| B-020 | Spaces na UI + criar channel | **Done (Wave 4)** — `Channel.SpaceId`, API spaces + create channel, sidebar agrupada, membership/`channel.create`, testes |
| B-021 | DMs 1:1 | **Done (Wave 4)** — Directory members + get-or-create DM + UI |
| B-022 | Threads | **Done (Wave 4)** — API get-or-create + replies com seq próprio, authZ membership, outbox/hub `threadId`, UI painel + testes |
| B-023 | Editar / soft-delete mensagem | **Done (Wave 4)** — API authZ + hub + UI + testes |
| B-024 | Reações | **Done (Wave 4)** — toggle API + outbox/hub `ReactionChanged`, UI na bubble, authZ `message.react`, testes |
| B-025 | Anexos MinIO | **Done (Wave 4)** — presign upload/download, authZ, UI composer + bubble, testes |
| B-026 | Presence + typing | **Done (Wave 4)** — typing + reconnect rejoin; presence online/away Redis TTL + hub Heartbeat/SetAway + UI indicadores |
| B-027 | Busca FTS | **Done (Wave 4)** — tsvector/GIN, API Search, membership ACL, UI shell, testes |
| B-028 | Rate-limit | **Done (Wave 4)** — Redis INCR send/hub |
| B-029 | PWA | **Done (Wave 4)** — manifest/icons + SW (ngsw) installability + offline shell/banner |
| B-030 | Dashboards Grafana | **Done** — overview (requests, outbox, SignalR) provisionado |
| B-031 | Backup scripts | **Done (Wave 4)** — `backup.sh` + `restore-drill.sh` + doc drill |

## P2 — Diferenciação e admin

| ID | Item | Notas |
|----|------|-------|
| B-040 | Guest users / link de canal | fora do MVP P1 (D-07) |
| B-041 | Papéis granulares | |
| B-042 | Audit log | parcial (eventos básicos) |
| B-043 | Notificações email (opcional) | SMTP genérico (D-10) |
| B-044 | AI: resumo de thread (flag) | mock existe; provider externo off default (D-06) |
| B-045 | AI: sugerir resposta | |
| B-046 | Export de workspace | após D-03 |
| B-047 | Políticas de retenção configuráveis | ADR-018; purge/flag depois |
| B-048 | Webhooks outbound | |
| B-049 | Temas light/dark polish | design-system |

## P3 — Escala / futuro

| ID | Item | Gatilho |
|----|------|---------|
| B-060 | OpenSearch | ADR-016 |
| B-061 | Bus externo | ADR-015 |
| B-062 | Helm/K8s | ADR-017 |
| B-063 | Clientes mobile nativos | demanda |
| B-064 | E2EE (se produto decidir) | mudança de modelo |
| B-065 | Federation multi-instância | pesquisa |
| B-066 | Marketplace de bots | pós-contratos estáveis |

## Ordem de consumo sugerida

Sempre esvaziar **P0** antes de P1. Em P1, preferir: editar/delete → DMs → anexos → busca → threads → rate-limit → dashboards → backup → spaces → presence → reações → PWA. (B-020…B-031 já Done na Wave 4.)

## Itens explicitamente rejeitados na fase 1

- Microserviços por módulo
- Clonar UI Slack/Discord
- Kubernetes obrigatório
- Elasticsearch obrigatório
- IA ligada por default
