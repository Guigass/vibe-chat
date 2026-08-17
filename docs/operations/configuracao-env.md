# Configuração via `.env` — Admin mínimo

Guia de referência para operar uma instância self-hosted. O operador de infra
sobe a plataforma via `.env` + Compose; o `workspace.admin` configura o
**produto** na UI. `.env` = infra (D-04); produto = admin + DB (ADR-020 / B-187).

**Status:** catálogo executável fechado em **W7-7 / B-105** (2026-08-07).
**W7-14 / B-187 Done:** `.env.example` só infra; produto no `/admin` + DB quando
overrides on. Este arquivo permanece o inventário canônico.
Specs:
[`B-105-catalogo-configuracao.md`](../product/specs/B-105-catalogo-configuracao.md),
[`B-187-env-enxuto.md`](../product/specs/B-187-env-enxuto.md).

> Regra crítica: uma variável presente no `.env` só chega ao processo dentro do
> container se o `compose.yaml` a mapear em `environment` ou a consumir em uma
> substituição `${VAR}`. Presença no template, sozinha, não configura API/Worker.

## Objetivo da fase (B-105)

1. **Inventariar** todas as variáveis de ambiente usadas por Compose, API, Worker e Web.
2. **Completar** o catálogo operacional (este arquivo) e o `.env.example` como
   template executável (placeholders, nunca secrets reais — D-04). B-187 leva
   produto ao admin; o inventário de infra não muda de arquivo.
3. **Documentar** a matriz **env vs admin UI**: o que só muda com restart/redeploy vs o que o admin muda em runtime.
4. **Fechar gaps** entre `appsettings*.json`, `compose.yaml` e `.env.example`.

## Modelo de duas camadas

| Camada | Quem configura | Onde | Exemplos |
|--------|----------------|------|----------|
| **Infra / plataforma** | Operador de infra | `.env` / secret manager | Postgres, Redis, Keycloak, MinIO, OIDC issuer, keyring AES-GCM, URLs, pins, seed/bootstrap, OTel/proxy |
| **Produto** | `workspace.admin` | `/admin/settings` + DB | SMTP, OpenRouter (key+baseUrl), webhook, retenção, Files, RateLimit, link preview, VAPID, kill switches (B-187) |

Regra (B-069 / ADR-020): PUT geral **não** aceita secrets. Rotação de OpenRouter/SMTP/webhook/VAPID usa endpoints dedicados; valores ficam em envelope AES-GCM (chave mestra só no env). Rollback = desligar `RuntimeSettings__DatabaseOverridesEnabled` no mesmo binário.

Caminho self-host pretendido (B-187): keyring demo (lab) ou chave real (prod) →
overrides só com chave válida → **produto** em `/admin/settings`. O `.env` é
**só infra**. Não mover Postgres, Redis, Keycloak, MinIO, OIDC, TLS, seed,
OTel ou keyring para o DB. Não apagar pins/portas do template só para “enxugar”.

## Dois artefatos (B-187)

| Artefato | Função |
|----------|--------|
| `.env.example` | Setup de **infra** (senhas de data plane, URLs, portas, pins, keyring demo de lab). |
| Este catálogo | Inventário completo + matriz env vs admin. |

B-187 fechou: o template não lista SMTP/IA/retenção/push/link-preview; o lab liga overrides com keyring demo válido.

## Escopo

### Dentro (W7-7)

- Variáveis do data plane (Postgres, Redis, Keycloak, MinIO)
- Variáveis do profile `apps` (API, Web, Worker)
- Variáveis dos profiles opcionais (`tools`, `observability`, `proxy`)
- Mapeamento `VAR_ENV` → `Section__Key` do ASP.NET Core
- Defaults seguros para produção (`SEED_ENABLED=false`; produto off no admin)
- Checklist de bootstrap: `cp .env.example .env` → ajustar `CHANGE_ME` → `task apps`

### Fora

- Substituir `/admin` para convites, papéis, auditoria de conversas ou export ZIP
- Tornar **toda** configuração dinâmica / despejar infra no PostgreSQL (rejeitado em ADR-020 e B-105)
- Levar o **produto** ao `/admin` sem tirar infra do `.env` (B-187)
- Secrets manager específico de cloud (apenas padrão: montar env ou arquivo `.env`)

## Inventário auditado

Todas as substituições `${VAR}` observadas em `compose.yaml` e
`compose.override.yaml` possuem entrada no `.env.example` na data do snapshot
B-105. B-187 não omite pins/portas; omite **todo** var de produto que passou
ao admin. O inventário canônico continua nesta página.

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
| `KEYCLOAK_*`, `OIDC_*` | keycloak, api, web | Realm, issuer, clients. `KEYCLOAK_PROXY_HEADERS` default `xforwarded` (vazio quebra Keycloak 26). Lab: não injete `KC_HTTP_RELATIVE_PATH=""`. Staging path `/auth`: defina `KC_HTTP_RELATIVE_PATH=/auth` no container Coolify **e** `KEYCLOAK_HTTP_RELATIVE_PATH=/auth` para o MetadataAddress da API. |
| `MINIO_*` | minio, api | Anexos S3-compatível |
| `MAILPIT_*` | mailpit (tools) | Dev only; SMTP de produto no `/admin/settings` |
| `OTEL_*`, `PROMETHEUS_*`, `GRAFANA_*`, `LOKI_*`, `TEMPO_*` | observability | Profile opcional |
| `TLS_*`, `PROXY_*` | proxy | Profile opcional (W5-2) |

### Aplicação (profile `apps`)

| Variável `.env` | Binding ASP.NET | Serviço | Notas |
|-----------------|-----------------|---------|-------|
| `ASPNETCORE_ENVIRONMENT` | — | api, worker | `Production` em prod |
| `API_PORT`, `WEB_PORT` | — | compose | Portas host |
| `API_BASE_URL`, `WEB_BASE_URL` | — | web build | URLs públicas |
| `DATABASE_BOOTSTRAP_ON_STARTUP` | `Database__BootstrapOnStartup` | api | `false` por default; `true` no primeiro boot de staging aplica migrations + catálogo RLS sem seed |
| `BOOTSTRAP_ENABLED` | `Bootstrap__Enabled` | api | Cria idempotentemente o workspace inicial de staging, sem fixtures demo |
| `BOOTSTRAP_INITIAL_ADMIN_EMAIL` | `Bootstrap__InitialAdminEmail` | api | Email obrigatório quando o bootstrap está ativo; o primeiro login OIDC assume o perfil pendente de `WorkspaceOwner` |
| `BOOTSTRAP_WORKSPACE_NAME` | `Bootstrap__WorkspaceName` | api | Nome do primeiro workspace; default `VibeChat Alpha` |
| `BOOTSTRAP_WORKSPACE_SLUG` | `Bootstrap__WorkspaceSlug` | api | Slug do primeiro workspace; apenas letras ASCII, números e hífen |
| `SEED_ENABLED` | `Seed__Enabled` | api | `true` no lab; **`false` em staging/prod** |
| `ENABLE_DEV_AUTH` | build arg web → `publicConfig.enableDevAuth` | web build | Lab: `true` (botões Alice/Bob/Demo no login). **Staging/prod/Coolify: `false` ou omitir** (default Compose). Rebuild da web após mudar. |
| `AI__Enabled`, `AI__Provider` | `Ai__*` | api | B-187: sai do template; SoT admin+DB |
| `OPENROUTER_API_KEY`, `OPENROUTER_BASE_URL` | `Ai__OpenRouter__*` | api | B-187: sai do template; key+baseUrl no admin |
| `EMAIL__*` | `Email__*` | api | B-187: sai do template; SMTP no admin |
| `MessageRetention__*` | `MessageRetention__*` | worker | B-187: sai do template; processo + knobs no admin |
| `LinkPreview__*` | `LinkPreview__*` | api + worker | B-187: sai do template; processo + timeout no admin |
| `Push__Enabled`, `Push__Vapid__*` | `Push__*` | api + worker | B-187: sai do template; kill switch + VAPID no admin |
| `RuntimeSettings__DatabaseOverridesEnabled` | `RuntimeSettings__DatabaseOverridesEnabled` | api, worker | Default `false`; liga overrides DB + rotação |
| `RuntimeSettings__Encryption__ActiveKeyVersion` | idem | api, worker | Versão ativa do keyring AES-GCM |
| `RuntimeSettings__Encryption__Keys__{n}` | idem | api, worker | Chave mestra base64 (32 bytes); **nunca** no DB |

### Primeiro administrador do staging

O realm de staging importa o usuário `admin` com senha temporária lida de
`VIBECHAT_INITIAL_ADMIN_PASSWORD`; o valor não fica versionado. No primeiro boot
de uma stack vazia, use `BOOTSTRAP_ENABLED=true`,
`BOOTSTRAP_INITIAL_ADMIN_EMAIL=admin@vibechat.local` e `SEED_ENABLED=false`.
Isso cria somente o workspace alpha, o espaço/canal `Geral` e o membership
pendente de `WorkspaceOwner`; não cria Alice, Bob, mensagens ou IA demo. O
primeiro login OIDC com o mesmo email assume esse perfil. O cliente
`vibechat-web` mantém o mapper `subject` no access token, inclusive para
lightweight tokens.

O import do realm é operação de primeiro boot: o Keycloak não sobrescreve um
realm já existente. Para recriar o staging, remova a stack e seus volumes e
suba uma stack nova. O projeto não alterna schemas nem mantém um segundo estado
do Keycloak como mecanismo de reset.

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
| SMTP | (produto) | SoT: `/admin/settings` (B-187); Compose pode manter `${EMAIL__*:}` de release |
| Retenção | (produto) | SoT: `/admin/settings`; Compose pode manter fallback de release |
| IA duplicada | (produto) | SoT: `/admin/settings`; Compose pode manter `OPENROUTER_*` de release |
| Roles PostgreSQL | `POSTGRES_APP_*`, `POSTGRES_MIGRATOR_*`, `POSTGRES_BACKUP_*` + `DATABASE_*_URL` | Entregue em `SEC-RLS-RUNTIME`; API/Worker usam app role |
| Projeto Compose | `COMPOSE_PROJECT_NAME` | Consumida pelo Docker Compose CLI |

### Chaves de aplicação — teto de código vs admin

| Seção appsettings | Chaves | Status |
|-------------------|--------|--------|
| `Files` | `MaxSizeBytes`, TTLs, `AllowedContentTypes`, `Audio:*` | Teto de **código** (`AttachmentPolicies`); override por tenant em `files.settings` (B-187) |
| `RateLimit` | `SendPerMinute`, `HubPerMinute` | Teto de código; override por tenant |
| `Cors` | `Origins` | Default em código; proxy TLS (profile `proxy`) cobre origem pública |
| `Authentication` | `RequireHttpsMetadata` | `"false"` no Compose dev/self-host; produção com TLS usa proxy + issuer HTTPS |
| `Minio` | `Endpoint`, `UseSsl` | Mapeado no Compose (`Minio__*`); rede interna `minio:9000`, público via `MINIO_ENDPOINT` |
| `Observability` | `GrafanaUrl` | Default appsettings; profile `observability` expõe Grafana em `GRAFANA_PORT` |
| `RuntimeSettings` | `DatabaseOverridesEnabled`, `Encryption:*` | Feature flag + keyring; off/default seguro |

### Só via admin UI (não entram no `.env` mínimo)

| Área | Endpoint / tabela | Motivo |
|------|-------------------|--------|
| Webhooks | `PUT` + `POST .../credentials/webhook/rotate` → `integrations.webhook_endpoints` | URL e secret criptografado (B-048 / ADR-020) |
| Retenção | `retention.*` + knobs de processo | Política tenant + job de instância (B-047 / B-187) |
| AI | `ai.*` + rotate OpenRouter | Flag, provider, key, baseUrl |
| Kill switches | `*.processEnabled` (B-187) | Singleton `administration.process_settings` |
| SMTP | `email.*` + rotate SMTP | Host/port/from + senha criptografada |
| Link preview | processo + timeout + toggle tenant | B-091 / B-187 |
| Push / VAPID | `push.*` + rotate VAPID | Kill switch + chaves da instância |
| Files / RateLimit | `files.*` / `rateLimit.*` | Limites por tenant sob teto de código |

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
B-105 fechou a injeção de `EMAIL__*` no api (histórico). **B-187** tirou produto do
`.env.example`; SMTP/IA/retenção/push/VAPID passam ao `/admin/settings`.

## Checklist de produção (profile `apps`)

Antes de expor a instância:

1. `cp .env.example .env` e substituir **todos** `*_change_me` / `CHANGE_ME` por secrets reais (D-04).
2. `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false` e `ENABLE_DEV_AUTH=false` (ou omitir — default do Compose).
3. Produto (IA/e-mail/retenção/push) **off por default**; ligar em `/admin/settings` (B-187).
4. Nunca commitar senha SMTP / VAPID / OpenRouter; rotação no admin.
5. Purge: processo + política do tenant; default false até política legal/operacional aprovada (ADR-018).
6. Issuer OIDC (`KEYCLOAK_ISSUER_URL`) e URLs públicas (`API_BASE_URL`, `WEB_BASE_URL`, `MINIO_ENDPOINT`) apontam para hostname TLS real.
7. Validar parse: `docker compose --env-file .env config --quiet`.
8. Subir: `task apps` e confirmar `/health` + login OIDC.
9. `/admin/settings`: secrets mascarados; produto com fonte `database` quando overrides on.

## Bootstrap rápido (hoje)

```bash
cp .env.example .env
# Ajustar CHANGE_ME / *_change_me para o ambiente
task apps
```

Produção: `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false`, `ENABLE_DEV_AUTH=false`, secrets reais só via `.env` ou secret manager (D-04). Ver também [`operacao.md`](./operacao.md) e [`runbooks/README.md`](./runbooks/README.md).
