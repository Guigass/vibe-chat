# Configuração via `.env` — Admin mínimo

Guia de referência para operar uma instância self-hosted com o **mínimo de dependência da UI `/admin`**. O operador de infra configura a plataforma via `.env`; o `workspace.admin` ajusta políticas por workspace na UI.

**Status:** catálogo executável fechado em **W7-7 / B-105** (2026-08-07). Spec:
[`B-105-catalogo-configuracao.md`](../product/specs/B-105-catalogo-configuracao.md).

> Regra crítica: uma variável presente no `.env` só chega ao processo dentro do
> container se o `compose.yaml` a mapear em `environment` ou a consumir em uma
> substituição `${VAR}`. Presença no template, sozinha, não configura API/Worker.

## Objetivo da fase (B-105)

1. **Inventariar** todas as variáveis de ambiente usadas por Compose, API, Worker e Web.
2. **Completar** `.env.example` como contrato único de configuração operacional (placeholders, nunca secrets reais — D-04).
3. **Documentar** a matriz **env vs admin UI**: o que só muda com restart/redeploy vs o que o admin muda em runtime.
4. **Fechar gaps** entre `appsettings*.json`, `compose.yaml` e `.env.example`.

## Modelo de duas camadas

| Camada | Quem configura | Onde | Exemplos |
|--------|----------------|------|----------|
| **Infra / plataforma** | Operador de infra | `.env` / secret manager | Postgres, Redis, Keycloak, MinIO, OIDC issuer, kill switches, keyring AES-GCM, BaseUrl OpenRouter |
| **Política + integrações** | `workspace.admin` | `/admin/settings` + DB | AI workspace, SMTP (incl. senha criptografada), webhook, retenção, Files, RateLimit |

Regra (B-069 / ADR-020): PUT geral **não** aceita secrets. Rotação de OpenRouter/SMTP/webhook usa endpoints dedicados; valores ficam em envelope AES-GCM (chave mestra só no env). Flag `RuntimeSettings__DatabaseOverridesEnabled=false` por default — rollback = desligar a flag no mesmo binário.

## Escopo

### Dentro (W7-7)

- Variáveis do data plane (Postgres, Redis, Keycloak, MinIO)
- Variáveis do profile `apps` (API, Web, Worker)
- Variáveis dos profiles opcionais (`tools`, `observability`, `proxy`)
- Mapeamento `VAR_ENV` → `Section__Key` do ASP.NET Core
- Defaults seguros para produção (`SEED_ENABLED=false`, `AI__Enabled=false`, etc.)
- Checklist de bootstrap: `cp .env.example .env` → ajustar `CHANGE_ME` → `task apps`

### Fora

- Substituir `/admin` para convites, papéis, auditoria de conversas ou export ZIP
- Configuração dinâmica por tenant sem restart (permanece em DB + admin API)
- Secrets manager específico de cloud (apenas padrão: montar env ou arquivo `.env`)

## Inventário auditado

Todas as substituições `${VAR}` observadas em `compose.yaml` e
`compose.override.yaml` possuem entrada no `.env.example` na data do snapshot.

### Imagens e portas do Compose

| Grupo | Variáveis |
|-------|-----------|
| Data plane | `POSTGRES_IMAGE`, `REDIS_IMAGE`, `KEYCLOAK_IMAGE`, `MINIO_IMAGE`, `MINIO_MC_IMAGE` |
| Tools/observabilidade | `MAILPIT_IMAGE`, `OTEL_COLLECTOR_IMAGE`, `PROMETHEUS_IMAGE`, `GRAFANA_IMAGE`, `LOKI_IMAGE`, `TEMPO_IMAGE` |
| Apps/proxy | `NODE_IMAGE`, `NGINX_IMAGE` |
| Data plane | `POSTGRES_PORT`, `REDIS_PORT`, `KEYCLOAK_HTTP_PORT`, `MINIO_API_PORT`, `MINIO_CONSOLE_PORT` |
| Tools/observabilidade | `MAILPIT_SMTP_PORT`, `MAILPIT_UI_PORT`, `PROMETHEUS_PORT`, `GRAFANA_PORT`, `LOKI_PORT`, `TEMPO_PORT` |
| Apps/proxy | `API_PORT`, `WEB_PORT`, `PROXY_HTTP_PORT`, `PROXY_HTTPS_PORT` |

### Credenciais e identidades

| Variáveis | Secret? | Uso |
|-----------|---------|-----|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | senha: sim | Bootstrap/owner (init + criação de roles); nunca connection string de request |
| `POSTGRES_APP_USER`, `POSTGRES_APP_PASSWORD` | senha: sim | Role runtime (`vibechat_app`) sem ownership/`BYPASSRLS` — API/Worker |
| `POSTGRES_MIGRATOR_USER`, `POSTGRES_MIGRATOR_PASSWORD` | senha: sim | Role `vibechat_migrator` (BYPASSRLS) para migrate/seed/RLS; não no hot path |
| `POSTGRES_BACKUP_USER`, `POSTGRES_BACKUP_PASSWORD` | senha: sim | Role `vibechat_backup` (SELECT) para runbooks de backup |
| `KEYCLOAK_DB`, `KEYCLOAK_DB_USER`, `KEYCLOAK_DB_PASSWORD` | senha: sim | Banco do Keycloak |
| `KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD` | senha: sim | Bootstrap do IdP |
| `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `MINIO_BUCKET` | senha: sim | S3-compatible |
| `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` | senha: sim | Observabilidade |
| `KEYCLOAK_REALM`, `OIDC_WEB_CLIENT_ID`, `OIDC_API_AUDIENCE` | não | OIDC |

### Compose / data plane

| Variável | Serviço | Notas |
|----------|---------|-------|
| `POSTGRES_*`, `DATABASE_URL` | postgres, api, worker/host | SoT; `DATABASE_URL` é do caminho host |
| `REDIS_*`, `REDIS_URL` | redis, api, worker/host | Containers usam o endereço interno `redis:6379` |
| `KEYCLOAK_*`, `OIDC_*` | keycloak, api, web | Realm, issuer, clients |
| `MINIO_*` | minio, api | Anexos S3-compatível |
| `MAILPIT_*`, `SMTP_*` | mailpit (tools) | Dev only; prod usa `EMAIL__*` |
| `OTEL_*`, `PROMETHEUS_*`, `GRAFANA_*`, `LOKI_*`, `TEMPO_*` | observability | Profile opcional |
| `TLS_*`, `PROXY_*` | proxy | Profile opcional (W5-2) |

### Aplicação (profile `apps`)

| Variável `.env` | Binding ASP.NET | Serviço | Notas |
|-----------------|-----------------|---------|-------|
| `ASPNETCORE_ENVIRONMENT` | — | api, worker | `Production` em prod |
| `API_PORT`, `WEB_PORT` | — | compose | Portas host |
| `API_BASE_URL`, `WEB_BASE_URL` | — | web build | URLs públicas |
| `DATABASE_BOOTSTRAP_ON_STARTUP` | `Database__BootstrapOnStartup` | api | `false` por default; `true` no primeiro boot de staging aplica migrations + catálogo RLS sem seed |
| `SEED_ENABLED` | `Seed__Enabled` | api | `false` em prod |
| `SEED_INITIAL_ADMIN_EMAIL` | `Seed__InitialAdminEmail` | api | Com seed ativo, cria um stub pendente como `WorkspaceOwner`; o primeiro login OIDC com esse email assume o perfil |
| `AI__Enabled`, `AI__Provider` | `Ai__*` | api | Off default (D-06) |
| `OPENROUTER_API_KEY`, `OPENROUTER_BASE_URL` | `Ai__OpenRouter__*` | api | Fallback env; BaseUrl permanece env; key pode ir ao DB criptografada (ADR-020) |
| `EMAIL__*` | `Email__*` | api | Injetado no profile `apps`; senha fallback env ou envelope DB |
| `MessageRetention__*` | `MessageRetention__*` | worker | Injetado no profile `apps`; kill switch off default |
| `LinkPreview__*` | `LinkPreview__*` | api + worker | B-091 / ADR-021; default Enabled; TimeoutMs 8000 |
| `RuntimeSettings__DatabaseOverridesEnabled` | `RuntimeSettings__DatabaseOverridesEnabled` | api, worker | Default `false`; liga overrides DB + rotação |
| `RuntimeSettings__Encryption__ActiveKeyVersion` | idem | api, worker | Versão ativa do keyring AES-GCM |
| `RuntimeSettings__Encryption__Keys__{n}` | idem | api, worker | Chave mestra base64 (32 bytes); **nunca** no DB |

### Primeiro administrador do staging

O realm de staging importa o usuário `admin` com senha temporária lida de
`VIBECHAT_INITIAL_ADMIN_PASSWORD`; o valor não fica versionado. Defina também
`SEED_ENABLED=true` e `SEED_INITIAL_ADMIN_EMAIL=admin@vibechat.local` para que o
primeiro login assuma um membership real de `WorkspaceOwner` no workspace de
alpha. O cliente `vibechat-web` mantém o mapper `subject` no access token, inclusive
para lightweight tokens.

Quando for necessário reimportar o realm sem apagar o schema anterior, altere
`KEYCLOAK_DB_SCHEMA` para um nome de schema novo e faça o redeploy. Isso invalida
as sessões e recria os usuários do realm importado; use apenas com autorização
explícita no staging.

### Variáveis do template fora das substituições do Compose

| Grupo | Variáveis | Situação |
|-------|-----------|----------|
| Host/DX | `DATABASE_URL`, `POSTGRES_HOST`, `REDIS_HOST`, `REDIS_URL`, `SEED_API_URL` | Task/scripts ou execução no host |
| Load test | `K6_IMAGE`, `LOAD_API_BASE`, `LOAD_DEV_USER`, `LOAD_CHANNEL_ID` | Ferramentas/testes |
| Proxy/certs | `PROXY_PUBLIC_URL`, `TLS_DOMAIN`, `TLS_EMAIL`, `TLS_CERT_PATH`, `TLS_KEY_PATH` | Documentais/scripts; Compose substitui apenas imagem/portas |
| URLs auxiliares | `WEB_BASE_URL`, `OIDC_WEB_REDIRECT_URI`, `OIDC_WEB_POST_LOGOUT_REDIRECT_URI` | Não substituídas no Compose atual |
| Imagens .NET | `DOTNET_IMAGE`, `DOTNET_SDK_IMAGE` | Não substituídas diretamente pelo Compose |
| MinIO | `MINIO_REGION` | Não consumida pelo Compose |
| Observabilidade host | `OTEL_EXPORTER_OTLP_ENDPOINT` | Containers recebem endpoint interno fixo |
| SMTP | `SMTP_*`, `EMAIL__*` | Aliases `SMTP_*` são fallback legado no código; **SoT:** `EMAIL__*` injetado no api |
| Retenção | `MessageRetention__*` | Worker profile `apps` |
| IA duplicada | ~~`AI__OpenRouter__*`~~ | Removido do template; Compose usa `OPENROUTER_*` → `Ai__OpenRouter__*` |
| Roles PostgreSQL | `POSTGRES_APP_*`, `POSTGRES_MIGRATOR_*`, `POSTGRES_BACKUP_*` + `DATABASE_*_URL` | Entregue em `SEC-RLS-RUNTIME`; API/Worker usam app role |
| Projeto Compose | `COMPOSE_PROJECT_NAME` | Consumida pelo Docker Compose CLI |

### Chaves de aplicação — teto env vs admin

| Seção appsettings | Chaves | Status |
|-------------------|--------|--------|
| `Files` | `MaxSizeBytes`, TTLs, `AllowedContentTypes`, `Audio:*` | Teto env (Compose injeta `Files__MaxSizeBytes`); override por tenant em `files.settings` (ADR-020) |
| `RateLimit` | `SendPerMinute`, `HubPerMinute` | Teto env; override por tenant em `building_blocks.rate_limit_settings` |
| `Cors` | `Origins` | Default em código; proxy TLS (profile `proxy`) cobre origem pública |
| `Authentication` | `RequireHttpsMetadata` | `"false"` no Compose dev/self-host; produção com TLS usa proxy + issuer HTTPS |
| `Minio` | `Endpoint`, `UseSsl` | Mapeado no Compose (`Minio__*`); rede interna `minio:9000`, público via `MINIO_ENDPOINT` |
| `Observability` | `GrafanaUrl` | Default appsettings; profile `observability` expõe Grafana em `GRAFANA_PORT` |
| `RuntimeSettings` | `DatabaseOverridesEnabled`, `Encryption:*` | Feature flag + keyring; off/default seguro |

### Só via admin UI (não entram no `.env` mínimo)

| Área | Endpoint / tabela | Motivo |
|------|-------------------|--------|
| Webhooks | `PUT` + `POST .../credentials/webhook/rotate` → `integrations.webhook_endpoints` | URL e secret criptografado (B-048 / ADR-020) |
| Retenção por tenant | `retention.*` em admin settings | Política de negócio (B-047) |
| AI workspace | `ai.workspaceEnabled` + rotate OpenRouter | Liga/desliga IA + key criptografada |
| SMTP | `email.*` + rotate SMTP | Host/port/from + senha criptografada |
| Files / RateLimit | `files.*` / `rateLimit.*` | Limites por tenant sob teto env |

## Critérios de aceite (B-105)

- [x] Inventário documental das substituições do Compose e variáveis extras do template
- [x] Gaps de injeção SMTP/retenção/aliases registrados explicitamente
- [x] `.env.example` e `compose.yaml` alinhados ao catálogo (EMAIL__* no api; retenção no worker)
- [x] Tabela final env → binding → serviço → obrigatório em prod, após remover aliases duplicados
- [x] Matriz env vs `/admin` revisada com Security (B-069 / `modelo-ameacas.md`)
- [x] `docs/operations/operacao.md` e `docs/operations/desenvolvimento.md` linkam este guia
- [x] Testes de arquitetura (`ComposeConfigCatalogTests`) comprovam bindings do profile `apps`
- [x] API/Worker usam role runtime sem ownership/`BYPASSRLS`; migration usa role
  separada (`SEC-RLS-RUNTIME`)

## Evidência documental DOC-007

Em 2026-07-27:

```text
docker compose --env-file .env.example config --quiet
exit: 0

docker compose --env-file .env.example config --profiles
apps
observability
proxy
tools
```

O Docker CLI local também avisou que não conseguia ler o config global do usuário;
isso não alterou o código de saída nem o parse do projeto.

DOC-007 considera concluídos o inventário, a matriz env/admin e a prova de parse.
B-105 fechou a injeção de `EMAIL__*` no api, confirmou `MessageRetention__*` no worker,
removeu alias duplicado `AI__OpenRouter__*` do template e adicionou testes de catálogo.

## Checklist de produção (profile `apps`)

Antes de expor a instância:

1. `cp .env.example .env` e substituir **todos** `*_change_me` / `CHANGE_ME` por secrets reais (D-04).
2. `ASPNETCORE_ENVIRONMENT=Production` e `SEED_ENABLED=false`.
3. `AI__Enabled=false` (default) até opt-in explícito; `OPENROUTER_API_KEY` só se provider externo.
4. `EMAIL__Enabled=false` (default) até SMTP real; nunca commitar `EMAIL__Smtp__Password`.
5. `MessageRetention__Enabled=false` (default) até política legal/operacional aprovada (ADR-018).
6. Issuer OIDC (`KEYCLOAK_ISSUER_URL`) e URLs públicas (`API_BASE_URL`, `WEB_BASE_URL`, `MINIO_ENDPOINT`) apontam para hostname TLS real.
7. Validar parse: `docker compose --env-file .env config --quiet`.
8. Subir: `task apps` e confirmar `/health` + login OIDC.
9. `/admin/settings`: secrets mascarados; `processSource`/`Source` = `env` para kill switches globais.

## Bootstrap rápido (hoje)

```bash
cp .env.example .env
# Ajustar CHANGE_ME / *_change_me para o ambiente
task apps
```

Produção: `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false`, secrets reais só via `.env` ou secret manager (D-04). Ver também [`operacao.md`](./operacao.md) e [`runbooks/README.md`](./runbooks/README.md).
