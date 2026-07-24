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
| W4-5 | C | AI NoOp + OpenRouter adapter + flag | W3-5 | parcial (mock) |
| W4-6 | B | Audit log admin | W3-5 | parcial |
| W4-7 | D | PWA installability + offline shell (B-029) | W3-5 | **Done** |
| W4-8 | C/D | Reações (B-024) | W3-5 | **Done** |

## Wave 5 — Produção self-host

| ID | Trilha | Tarefa | Deps |
|----|--------|--------|------|
| W5-1 | A | Backup automatizado + drill doc validado (**Done** — scripts + doc) | W3-5 |
| W5-2 | A | TLS / proxy reference config | W3-5 |
| W5-3 | E | Load smoke k6 | W3-5 |
| W5-4 | G | Runbooks finais ops | W5-1 |

---

## Parallelismo sugerido por time de agentes

```text
Agent-Infra     → W0-1, W0-2, W0-6, W5-*
Agent-Backend   → W0-3, W1-*, W2-1..W2-4, W3-1, W3-3, W4-*
Agent-Frontend  → W0-4, W0-5, W1-4, W2-5, W4-7
Agent-QA        → W0-7, W1-5, W2-6, W2-7, W3-2, W3-5, W5-3
Agent-Security  → W3-1/W3-2 review, threat model updates
Agent-Obs       → W0-6, W3-4
```

## Definição de “Wave 3 completa”

Fatia vertical aceita (`criterios-aceite-fatia-vertical.md`) + RLS testado + dashboards mínimos.
