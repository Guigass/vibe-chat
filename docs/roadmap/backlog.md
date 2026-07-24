# Backlog Priorizado — VibeChat

Prioridade: **P0** (fatia vertical) → **P1** (MVP usável) → **P2** (diferenciação) → **P3** (depois).

## P0 — Bloqueadores da fatia vertical

| ID | Item | Notas |
|----|------|-------|
| B-001 | Compose + seeds Keycloak/Postgres | DX |
| B-002 | OIDC login web + API JWT | |
| B-003 | TenantContext + Directory mínimo | |
| B-004 | Send message + seq + idempotency | |
| B-005 | Outbox + Worker | |
| B-006 | SignalR deliver message.created | |
| B-007 | History API | |
| B-008 | UI channel + composer | |
| B-009 | RLS + testes security | |
| B-010 | Health/ready + CI básica | |

## P1 — MVP usável em empresa pequena

| ID | Item | Notas |
|----|------|-------|
| B-020 | Spaces na UI + criar channel | |
| B-021 | DMs 1:1 | |
| B-022 | Threads | |
| B-023 | Editar / soft-delete mensagem | |
| B-024 | Reações | |
| B-025 | Anexos MinIO | |
| B-026 | Presence + typing | |
| B-027 | Busca FTS | |
| B-028 | Rate-limit | |
| B-029 | PWA | |
| B-030 | Dashboards Grafana | |
| B-031 | Backup scripts | |

## P2 — Diferenciação e admin

| ID | Item | Notas |
|----|------|-------|
| B-040 | Guest users / link de canal | |
| B-041 | Papéis granulares | |
| B-042 | Audit log | |
| B-043 | Notificações email (opcional) | |
| B-044 | AI: resumo de thread (flag) | |
| B-045 | AI: sugerir resposta | |
| B-046 | Export de workspace | |
| B-047 | Políticas de retenção configuráveis | depende decisão legal |
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

Sempre esvaziar **P0** antes de P1. Em P1, preferir: DMs → anexos → busca → threads → AI.

## Itens explicitamente rejeitados na fase 1

- Microserviços por módulo
- Clonar UI Slack/Discord
- Kubernetes obrigatório
- Elasticsearch obrigatório
- IA ligada por default
