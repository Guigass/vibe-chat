# Roadmap Executável — VibeChat

Roadmap para **times de agentes** trabalharem em paralelo. Cada item tem ID, dependências e trilha.

## Legend

| Trilha | Foco |
|--------|------|
| A | Infra / Compose / DX |
| B | Backend Platform + Identity + Directory |
| C | Backend Messaging + Realtime + Worker |
| D | Frontend Angular |
| E | Security + QA |
| F | Observabilidade |
| G | Docs / Design system |

Dependências: itens só começam quando deps = done.

---

## Wave 0 — Fundação (paralelo total)

| ID | Trilha | Tarefa | Deps |
|----|--------|--------|------|
| W0-1 | A | Compose: Postgres, Redis, Keycloak, MinIO | — |
| W0-2 | A | Scripts seed-dev + .env.example | W0-1 |
| W0-3 | B | Skeleton solução .NET 10 (Api, Worker, Contracts, Platform) | — |
| W0-4 | D | Skeleton Angular 22 standalone + tokens CSS | — |
| W0-5 | G | Design system tokens aplicados no web | W0-4 |
| W0-6 | F | OTel collector + Prometheus + Grafana + Loki + Tempo no Compose | W0-1 |
| W0-7 | E | Pipeline CI: build + unit vazio + arch test stub | W0-3 |

## Wave 1 — Identidade e Directory

| ID | Trilha | Tarefa | Deps |
|----|--------|--------|------|
| W1-1 | B | OIDC validation + TenantContext middleware | W0-3, W0-1 |
| W1-2 | B | Módulo Directory: Tenant, Workspace, Space, Channel, Membership | W1-1 |
| W1-3 | B | Seed dados acme + alice/bob | W1-2, W0-2 |
| W1-4 | D | Login OIDC PKCE + guard de rotas | W0-4, W1-1 |
| W1-5 | E | Testes auth negativos (401/403) | W1-1 |

## Wave 2 — Fatia de mensagem

| ID | Trilha | Tarefa | Deps |
|----|--------|--------|------|
| W2-1 | C | Conversation/Message/seq/idempotency + migrations | W1-2 |
| W2-2 | C | Outbox writer + worker processor | W2-1, W0-3 |
| W2-3 | C | SignalR hub + Redis backplane config | W2-2, W1-1 |
| W2-4 | C | History API + gap model | W2-1 |
| W2-5 | D | UI channel: lista + composer + hub client | W1-4, W2-3, W2-4 |
| W2-6 | E | Testes integração send+idempotency+seq | W2-1 |
| W2-7 | E | E2E Playwright dois usuários | W2-5, W1-3 |

## Wave 3 — Hardening multi-tenant

| ID | Trilha | Tarefa | Deps |
|----|--------|--------|------|
| W3-1 | B | RLS policies em tabelas de negócio | W2-1, W1-2 |
| W3-2 | E | Suíte tests/security cross-tenant (API+hub) | W3-1, W2-3 |
| W3-3 | C | Rate-limit Redis em send/hub (**Done**) | W2-3 |
| W3-4 | F | Dashboards: requests, outbox lag, SignalR (**Done**) | W0-6, W2-2 |
| W3-5 | E | Critérios de aceite fatia — sign-off | W2-7, W3-2 |

## Wave 4 — Extensões pós-fatia (paralelizável)

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W4-0 | G | Fechar D-01…D-10 + ADR-018 retenção | — | **Done** |
| W4-0b | C/D | Editar / soft-delete (B-023) + DMs 1:1 (B-021) | W3-5 | **Done** |
| W4-1 | C/D | Threads | W3-5 | **Done** |
| W4-2 | C/D | Anexos MinIO | W3-5, W0-1 | **Done** |
| W4-3 | C/D | Presence + typing polidos | W3-5 | **Done** |
| W4-3b | B/D | Spaces UI + criar channel (B-020) | W3-5 | **Done** |
| W4-4 | C | Search FTS Postgres | W3-5 | **Done** |
| W4-5 | C | AI NoOp + OpenRouter adapter + flag | W3-5 | **Done** — Mock default em lab; `Ai:Enabled=false` prod; OpenRouter opt-in+key; summarize authZ/503; fora do hot path SendMessage (D-06) |
| W4-6 | B | Audit log admin | W3-5 | **Done** — `GET /admin/audit-events` + UI admin + authZ `admin.dashboard` |
| W4-7 | D | PWA installability + offline shell (B-029) | W3-5 | **Done** |
| W4-8 | C/D | Reações (B-024) | W3-5 | **Done** |

## Wave 5 — Produção self-host

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W5-1 | A | Backup automatizado + drill doc validado | W3-5 | **Done** — scripts + doc |
| W5-2 | A | TLS / proxy reference config | W3-5 | **Done** — nginx Compose profile `proxy` + certs script |
| W5-3 | E | Load smoke k6 | W3-5 | **Done** — `tests/load/smoke.js` + `task load:smoke` |
| W5-4 | G | Runbooks finais ops | W5-1 | **Done** — `docs/operations/runbooks/` (incidentes, backup/restore, TLS/proxy, upgrade) |

## P2 — Admin / diferenciação

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| P2-1 | B/D | Papéis granulares (B-041) | Wave 5, D-07 | **Done** — `PUT .../members/{userId}/role` + UI admin + authZ `workspace.admin` |
| P2-2 | A/B | Notificações email SMTP (B-043) | D-10, P2-1 | **Done** — Null/SMTP; off default; e-mail role-change via outbox |

## Wave 6 — Refinamento UX + Admin (próximo)

Prioridade: corrigir tempo real / typing / scroll; depois clareza de cadastro e admin sensível.

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W6-1 | C/D | Realtime estável: MessageCreated/edit/delete/reações + gap-fill reconnect (B-070) | Wave 5 | **Done** |
| W6-2 | C/D | Typing: não mostrar indicador para o próprio usuário (B-071) | W6-1 ou paralelo | Planned |
| W6-3 | D | Layout: scroll só no container da conversa (B-072) | — | Planned |
| W6-4 | B/D/G | Cadastro de usuário + diretivas documentados e fluxo admin (B-068) | P2-1 | Planned |
| W6-5 | B/D/E | Settings sensíveis (tokens, webhooks, AI/SMTP) só admin (B-069, B-048) | W6-4 | Planned |
| W6-6 | B/D/E | Auditoria completa de conversas no ADMIN (B-067) | W4-6, W6-4 | Planned |
| W6-7 | D/G | UI polish com PrimeNG + tema tokens (B-073; emenda ADR-002) | W6-3 | Planned |
| W6-8 | A/G | API + Web (+ Worker) containerizados no Compose como caminho oficial (B-074) | W5-2 | Planned |

### Critérios de aceite Wave 6 (resumo)

- Dois usuários: mensagem/edit/delete/reação aparecem sem F5; reconnect preenche lacunas de `seq`
- Autor de typing não vê o próprio “digitando…”
- Página do shell não rola inteira; só a timeline
- Docs/glossário deixam claro: Keycloak autentica; membership/diretivas autorizam
- Tokens/webhooks/keys inacessíveis a não-admin
- Admin consegue auditar conversa (não só `audit_events`)
- PrimeNG só após emenda ADR-002; identidade visual VibeChat preservada
- `docker compose --profile apps up -d` sobe **api** + **web** (+ worker) healthy; caminho self-host documentado (dev hot-reload continua via `task dev`)

---

## Parallelismo sugerido por time de agentes

```text
Agent-Infra     → W0-1, W0-2, W0-6, W5-*, W6-8
Agent-Backend   → W0-3, W1-*, W2-1..W2-4, W3-1, W3-3, W4-*, W6-1, W6-2, W6-4..W6-6
Agent-Frontend  → W0-4, W0-5, W1-4, W2-5, W4-7, W6-1..W6-3, W6-7
Agent-QA        → W0-7, W1-5, W2-6, W2-7, W3-2, W3-5, W5-3, W6-1 E2E, W6-8 smoke
Agent-Security  → W3-1/W3-2 review, W6-5/W6-6 authZ + threat model
Agent-Obs       → W0-6, W3-4
```

## Definição de “Wave 3 completa”

Fatia vertical aceita (`criterios-aceite-fatia-vertical.md`) + RLS testado + dashboards mínimos.
