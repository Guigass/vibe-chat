# VibeChat

Plataforma de **chat corporativo open-source e self-hosted**. Organizações instalam, operam e controlam a própria infraestrutura — canais, mensagens em tempo real, arquivos e integrações, sem lock-in de SaaS.

> Licença: **Apache-2.0** (D-01).

## O que é

- Monólito modular (.NET 10 API + Worker, Angular 22)
- Identidade OIDC via Keycloak
- PostgreSQL como source of truth (RLS multi-tenant)
- Redis para presença / backplane SignalR (não é SoT)
- MinIO para anexos (S3-compatible)
- Observabilidade opcional (OTel, Prometheus, Grafana, Loki, Tempo)

Visão de produto: [`docs/product/visao.md`](docs/product/visao.md)  
Arquitetura: [`docs/architecture/visao-geral.md`](docs/architecture/visao-geral.md)

## Quick start

### Pré-requisitos

- Docker + Docker Compose v2
- .NET 10 SDK
- Node.js 22+
- [Task](https://taskfile.dev/) (`go-task`)

### Subir o ambiente

**Self-host / demo (caminho oficial — B-074):** API + Web + Worker em containers:

```bash
cp .env.example .env   # se ainda não existir
task apps              # docker compose -f compose.yaml --profile apps up -d --build
# Web :4200 · API :5080 · seed automático em Development (Seed:Enabled)
```

**DX com hot reload** (API/Web no host; data plane no Compose):

```bash
task setup             # postgres/redis/keycloak/minio + migrate
task dev               # API (:5080) + Web (:4200) em paralelo
task seed              # tenant demo + #geral + alice/bob (se a API ainda não seedou)
```

Abra http://localhost:4200

### Usuários demo

| Usuário | E-mail | Senha (Keycloak) |
|---------|--------|------------------|
| Alice | `alice@vibechat.local` | `Demo123!` |
| Bob | `bob@vibechat.local` | `Demo123!` |
| Demo | `demo@vibechat.local` | `Demo123!` |

- **Keycloak admin:** `admin` / valor de `KEYCLOAK_ADMIN_PASSWORD` no `.env`
- **Modo demo local (UI):** botão “Explorar demo local” na tela de login (sem Keycloak)
- **DevAuth (API Development):** header `X-Dev-User: alice|bob|demo`

Seed cria workspace demo, canal `#geral` e memberships para alice/bob.

## Comandos Task

| Comando | Descrição |
|---------|-----------|
| `task setup` | `.env`, Compose up, health, migrate |
| `task apps` | Self-host oficial: build/up api+web+worker (`profile apps`, B-074) |
| `task dev` | API + Web em paralelo (hot reload) |
| `task stop` | `docker compose stop` |
| `task reset` | `down -v` + setup |
| `task lint` | `dotnet format --verify` + lint/tsc web |
| `task test` | Testes unitários |
| `task test:integration` | Integração (Testcontainers) |
| `task test:e2e` | Playwright (duas sessões; stack já no ar) |
| `task test:e2e:ci` | E2E caminho CI — compose + API/Web + Playwright (W7-1) |
| `task ux:stack` | Boot do E2E CI sem Playwright — stack fica no ar (UX Review) |
| `task test:architecture` | Fronteiras de módulo |
| `task test:security` | Isolamento multi-tenant |
| `task verify` | lint + todos os testes relevantes |
| `task proxy:certs` | Certs self-signed para profile `proxy` (W5-2) |
| `task load:smoke` | k6 smoke DevAuth (W5-3) |
| `task migrate` | EF Core `database update` |
| `task seed` | `POST /api/v1/dev/seed` |
| `task logs` | Tail dos logs Compose |
| `task build` | Build Release .NET + Angular |

## Estrutura

```text
apps/api          HTTP + SignalR (composition root)
apps/worker       Outbox / jobs
apps/web          Angular 22
modules/*         Domínio modular
src/*             SharedKernel + Infrastructure
infra/            Compose helpers, Keycloak, observability
docs/             Produto, arquitetura, ADRs, ops
tests/            unit, integration, architecture, security, e2e
```

## Documentação

| Área | Caminho |
|------|---------|
| Portal e estado atual | `docs/README.md`, `docs/roadmap/estado-atual.md` |
| Guias de uso | `docs/product/guias/README.md` |
| Desenvolvimento | `docs/operations/desenvolvimento.md` |
| Operação / runbooks | `docs/operations/operacao.md`, `docs/operations/runbooks/` |
| Release e suporte | `docs/operations/release-versionamento-suporte.md` |
| Agentes / Cloud Agents | `AGENTS.md`, `docs/agents/orientacoes.md` |
| ADRs | `docs/adrs/` |
| Segurança multi-tenant | `docs/security/multi-tenant.md` |
| Roadmap / decisões | `docs/roadmap/` |
| Contribuindo | `CONTRIBUTING.md` |
| Segurança (reporte) | `SECURITY.md` |

## Cloud Agents

- Setup: `.cursor/environment.json` → `bash infra/scripts/agent-setup.sh`
- Regras: `.cursor/rules/*.mdc` + `AGENTS.md`
- Automações (Build → QA+Merge → Docs): `.cursor/automations/`

## Licença

Apache License 2.0 — ver `LICENSE` (D-01).
