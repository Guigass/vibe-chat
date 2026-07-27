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

### Fase 3 — Refinamento UX + Admin (Wave 6)

Prioridade sobre P2 novo de diferenciação. Detalhe em `docs/roadmap/roadmap.md` (Wave 6) e `docs/roadmap/backlog.md` (P1.5 / B-067+).

| Tema | Entregáveis | Status |
|------|-------------|--------|
| Tempo real | Mensagens/edit/delete/reações ao vivo; gap-fill por `seq` no reconnect (B-070) | **Done** |
| Typing | Não mostrar “digitando…” para o próprio usuário (B-071) | **Done** |
| Layout chat | Scroll apenas no bloco da conversa; shell sem rolagem da página (B-072) | **Done** |
| Cadastro + diretivas | Keycloak autentica → perfil (stub pending ou espelho) → membership via invite admin; diretivas = papéis (B-068) | **Done** |
| Secrets / integrações | Tokens, webhooks, AI/SMTP só para administradores (B-069, B-048) | **Done** (B-069 + B-048 MessageCreated) |
| Auditoria de conversas | Viewer ADMIN completo por canal/DM/thread, além do audit log de ações (B-067) | **Done** |
| UI | Polish admin com tokens VibeChat (B-073 histórico; **B-104** remove PrimeNG / D-15; **B-106** admin shell) | B-073 Done; **B-104 Planned** — sair do PrimeNG; **B-106 Planned** — nav/toolbars/listagens/filtros após B-104 |
| Compose apps | API + Web (+ Worker) em containers no Compose como deploy oficial self-host (B-074); `task dev` só para DX com hot reload | **Done** — `task apps` |

### Fase 4 — Admin mínimo via `.env` (Wave 7 / B-105)

Operação self-host com o menor atrito possível: o operador de infra não precisa da UI `/admin` para subir a plataforma nem para integrações sensíveis (AI, SMTP, retenção global).

| Tema | Entregáveis | Status |
|------|-------------|--------|
| Inventário | Todas as variáveis de Compose + API + Worker + Web catalogadas | Planned (W7-7) |
| Contrato `.env` | `.env.example` completo, comentado, alinhado ao `compose.yaml` | Planned |
| Matriz env vs admin | O que é infra (env) vs política por workspace (`/admin/settings`) | Planned — ver `docs/operations/configuracao-env.md` |
| Gaps | `Files`, `RateLimit`, `Cors`, etc. expostos ou documentados como fixos | Planned |
| Ops | `operacao.md` + runbooks referenciam o catálogo | Planned |

**Fora de escopo:** substituir convites, papéis, auditoria de conversas ou export — permanecem em `/admin`.

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

## Decisões humanas

Todas fechadas em 2026-07-24 — registros e impacto em `docs/roadmap/decisoes-pendentes.md`:

- Licença open-source — Apache-2.0 (D-01)
- Marca / trademark — produto “VibeChat”; assets em `apps/web/public/` (D-02; inventário no design-system)
- Política legal de retenção — soft-delete + purge configurável (D-03 / ADR-018)
- Credenciais e realms de produção — só via `.env` / secrets manager (D-04)

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
