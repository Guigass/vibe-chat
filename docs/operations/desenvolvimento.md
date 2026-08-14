# Guia de Desenvolvimento — VibeChat

## Pré-requisitos

- Docker + Docker Compose v2
- .NET 10 SDK
- Node.js **22.22.3+** na faixa do Angular CLI (`^22.22.3 || ^24.15 || >=26`)
- [Task](https://taskfile.dev/) (`go-task`)
- Acesso à rede para puxar imagens (Keycloak, Postgres, etc.)

Instalação assistida (agentes / máquina limpa):

```bash
bash infra/scripts/agent-setup.sh
```

## Subir o ambiente local

```bash
# Na raiz do repositório
cd /workspace   # ou clone local

task setup      # .env + docker compose up -d + wait healthy + migrate
task dev        # API (http://localhost:5080) + Web (http://localhost:4200)
task seed       # POST /api/v1/dev/seed — tenant demo, #geral, alice/bob
```

Outros comandos úteis:

```bash
task stop                 # docker compose stop
task reset                # down -v + setup
task logs                 # tail compose
task lint                 # dotnet format --verify + tsc/ng lint
task test                 # unit
task test:integration     # Testcontainers
task test:architecture
task test:security
task test:e2e             # Playwright
task test:e2e:ci          # compose + API/Web + Playwright (caminho CI)
task ux:stack             # mesmo boot do E2E CI, sem Playwright (stack fica no ar)
task proxy:certs          # TLS self-signed (Compose profile proxy)
task load:smoke           # k6 smoke (API Development + DevAuth)
task verify               # lint + testes relevantes
task build
task migrate
```

Runbooks de operação (incidentes, TLS, backup drill, upgrade): [`runbooks/README.md`](./runbooks/README.md).

Catálogo auditado de variáveis, gaps de injeção e matriz env vs admin UI:
[`configuracao-env.md`](./configuracao-env.md) (B-105). Integração configurável
no admin: [B-187](../product/specs/B-187-env-enxuto.md) (Planned).

## Serviços locais

| Serviço | Porta típica | Uso |
|---------|--------------|-----|
| Web | 4200 | Angular dev server |
| API | 5080 | HTTP + SignalR |
| Postgres | 5432 | SoT |
| Redis | 6379 | Presence/backplane |
| Keycloak | 8080 | OIDC |
| MinIO | 9000 / 9001 | S3 + console |
| Grafana | 3000 | Dashboards (`--profile observability`) |
| Prometheus | 9090 | Métricas |

Credenciais de **dev** vivem em `.env.example` (nunca secrets de produção).

Compose:

```bash
docker compose up -d                              # data plane
docker compose --profile tools up -d              # + mailpit
docker compose --profile observability up -d      # + otel stack
docker compose --profile apps up -d               # api + web + worker em containers
```

- **Self-host / demo / CI de stack completo:** profile `apps` (B-074) — API e front também sobem no Compose.
- **Dia a dia com hot reload:** `task dev` (API/Web no host) + data plane no Compose.

## Seed mínimo

1. Realm Keycloak `vibechat` com usuários `alice@` / `bob@` / `demo@` (`Demo123!`)
2. Workspace demo + canal `#geral`
3. Memberships para alice e bob

```bash
task seed
# ou: ./infra/scripts/seed.sh
```

Endpoint (Development): `POST /api/v1/dev/seed`

### Auth em desenvolvimento

| Modo | Como |
|------|------|
| Keycloak OIDC | “Entrar com Keycloak” |
| Demo UI | “Explorar demo local” |
| DevAuth (UI + API) | Botões Alice/Bob/Demo no login quando `ENABLE_DEV_AUTH=true` no build da web (`task apps` / `.env` lab). API: header `X-Dev-User: alice\|bob\|demo` (somente Development). Valor desconhecido sem `X-Dev-Email` → **401** (B-177; sem fallback demo). Convite dinâmico: `X-Dev-User` + `X-Dev-Email` (+ `X-Dev-Name` opcional) para testar claim de stub `pending:{email}` |

`ENABLE_DEV_AUTH` é **build-time** (Compose arg → `public-config`). Lab: `true`. Staging/prod/Coolify: `false` ou omitir (default). Mudança exige rebuild da imagem `web`.

### Cadastro e diretivas (B-068)

1. **IdP autentica** — crie o usuário no Keycloak (realm `vibechat`) com o e-mail desejado. Não há self-signup aberto no VibeChat.
2. **Admin provisiona membership** — em `/admin`, formulário “Convidar membro” (`POST /api/v1/workspaces/{id}/members`) com e-mail + papel (`Member`/`Moderator`/`Auditor`/`Admin`).
3. **Primeiro login vincula** — se o perfil ainda era stub `pending:{email}`, o SSO atualiza o `sub` e a membership já criada passa a valer.
4. **Diretivas** — papéis existentes continuam editáveis na tabela de membros (`PUT .../members/{userId}/role`).

**Roles no Keycloak ≠ papéis no VibeChat (B-176):** atribuir realm role `admin`
no IdP **não** eleva o usuário no produto. AuthZ de workspace vem de
`workspace_members.role` (convite/alteração em `/admin`). Realm roles são
opcionais para SSO futuro e não substituem membership.

DX sem Keycloak: DevAuth + seed já criam alice/bob/demo com membership.

## Fluxo de trabalho do desenvolvedor

1. Criar branch a partir da branch de fundação / `main`
2. Implementar no módulo correto + contratos se necessário
3. Testes unit/integration locais
4. Verificar lint/arch tests (`task verify`)
5. Atualizar docs se decisão mudar (preferir ADR)

## Estrutura mental do código

```text
apps/api          → composition root HTTP/SignalR
apps/worker       → outbox/jobs
apps/web          → Angular
modules/*         → domínio
docs/*            → verdade de produto/arquitetura
tests/e2e         → Playwright (duas sessões)
```

## Debugging útil

- **Mensagem não chega no outro cliente:** ver outbox `processed_at`, traces Tempo, grupos SignalR
- **401 loop:** clock skew, audience, redirect URIs no Keycloak
- **RLS vazio:** `app.tenant_id` não setado na conexão
- **Duplicata:** Idempotency-Key ausente/diferente

## Convenções

- PT-BR para docs de usuário/ops; código em inglês (ids, classes)
- Sem microserviços novos sem ADR
- Não commitar binários, `node_modules`, secrets

## Testes

```bash
task test
task test:integration
task test:architecture
task test:security
task test:e2e
```

E2E: ver `tests/e2e/README.md` (modos `demo`, `devauth`, `oidc`).

Na CI (W7-1 / B-075), o job **E2E (Playwright)** executa os specs com DevAuth
via `./infra/scripts/ci-e2e.sh` (data plane Compose + API/Web no host).

## Leitura obrigatória antes de codar

- `docs/product/glossario.md`
- `docs/architecture/contratos.md`
- `docs/adrs/ADR-001` … `ADR-010`
- `docs/security/multi-tenant.md`
- `AGENTS.md` e `docs/agents/orientacoes.md`
