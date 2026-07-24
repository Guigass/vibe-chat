# Plano Inicial de Implementação — VibeChat

## Meta da fase 1

Entregar a **fatia vertical** (`docs/product/criterios-aceite-fatia-vertical.md`) com base sólida: monólito modular, Compose, multi-tenant RLS, outbox, SignalR, observabilidade.

## Fases de entrega

### Fase 0 — Fundação (paralelizável)

| Trilha | Entregáveis |
|--------|-------------|
| Repo / DX | Estrutura `apps/`, `modules/`, `infra/`, `tests/`; EditorConfig; CI mínima |
| Infra Compose | Postgres, Redis, Keycloak, MinIO, OTel stack |
| Docs | Este conjunto de docs + ADRs (este trabalho) |
| Design system | Tokens CSS, temas light/dark (`design-system.md`) |

### Fase 1a — Identidade e Directory

- Integração OIDC Keycloak
- TenantContext + middleware
- Tenant, Workspace, Space, Channel seed
- Membership + autorização básica

### Fase 1b — Messaging + Realtime

- Conversation + Message + seq + idempotency
- Outbox + Worker
- Hub SignalR + grupos
- History API + gap-fill no cliente

### Fase 1c — Web

- Login OIDC (Angular 22 standalone + signals)
- Shell workspace/channel
- Lista de mensagens + composer
- PWA mínima (installable)

### Fase 1d — Hardening

- RLS policies + testes de isolamento
- Rate-limit Redis
- Health/ready
- Dashboards Grafana básicos
- Critérios de aceite QA

### Fase 2 (após fatia)

- Anexos MinIO
- Threads na UI
- Busca FTS
- Presence/typing polidos
- AI opcional (feature flag)
- Audit log de ações admin

## Riscos principais

| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| Vazamento cross-tenant | Crítico | RLS + testes security + TenantContext obrigatório |
| Duplicação de mensagens | Alto | Idempotency keys + unique constraints |
| Outbox lag / dual-write | Alto | Mesma TX; métricas de lag; retries |
| Complexidade Angular prematura | Médio | Standalone + signals; evitar state libs pesadas no MVP |
| Scope creep (microserviços, K8s, ES) | Alto | ADRs 015–017; fase 1 Compose only |
| Keycloak misconfig | Alto | Realm export versionado; seed documentado |
| Credenciais em repo | Alto | Secrets via env; checklist em decisoes-pendentes |

## Decisões já tomadas (ver ADRs)

- Monólito modular, Angular 22, .NET 10, SignalR, Postgres, Redis, Keycloak, MinIO
- Multi-tenancy RLS, Outbox, busca Postgres, AI atrás de interface
- OTel + Prometheus + Grafana + Loki + Tempo
- Sem K8s / bus externo / OpenSearch na fase 1

## Decisões pendentes (humanas)

Ver `docs/roadmap/decisoes-pendentes.md`:

- Licença open-source
- Marca / trademark
- Política legal de retenção
- Credenciais e realms de produção

## Ordem sugerida de PRs

1. Skeleton solução .NET + Angular + Compose
2. Platform (tenancy, outbox infra, health)
3. Identity + Keycloak realm
4. Directory + seed
5. Messaging persistência + API
6. Worker outbox + Realtime
7. Web chat mínimo
8. RLS + testes segurança
9. Observabilidade dashboards
10. Anexos / busca / polish

## Critério de saída da fase 1

Checklist de aceite da fatia vertical 100% verde + ADRs 001–014 refletindo o código + operação documentada em `docs/operations/`.
