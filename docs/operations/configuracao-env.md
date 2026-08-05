# Configuração via `.env` — Admin mínimo

Guia de referência para operar uma instância self-hosted com o **mínimo de dependência da UI `/admin`**. O operador de infra configura a plataforma via `.env`; o `workspace.admin` ajusta políticas por workspace na UI.

**Status:** inventário documental auditado em **2026-07-27**; alinhamento
executável continua planejado em **W7-7 / B-105**. Spec:
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
| **Infra / plataforma** | Operador de infra | `.env` / secret manager | Postgres, Redis, Keycloak, MinIO, OIDC issuer, SMTP host, AI provider/key, kill switches de worker |
| **Política por workspace** | `workspace.admin` | `/admin/settings` + DB | Habilitar AI no workspace, URL de webhook, dias de retenção, override de SMTP não-secreto |

Regra já em vigor (B-069): **secrets de AI e SMTP não são graváveis pela API** — só via env/secret store. A UI admin mostra máscara (`configured` / `••••last4`) e toggles não-secretos.

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
| `SEED_ENABLED` | `Seed__Enabled` | api | `false` em prod |
| `AI__Enabled`, `AI__Provider` | `Ai__*` | api | Off default (D-06) |
| `OPENROUTER_API_KEY`, `OPENROUTER_BASE_URL` | `Ai__OpenRouter__*` | api | Secret; `CHANGE_ME` |
| `EMAIL__*` | `Email__*` | api, worker | **Gap:** template possui, Compose não injeta |
| `MessageRetention__*` | `MessageRetention__*` | worker | **Gap:** template possui, Compose não injeta |

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
| SMTP | `SMTP_*`, `EMAIL__*` | **Gap:** não injetadas em API/Worker |
| Retenção | `MessageRetention__*` | **Gap:** não injetadas no Worker |
| IA duplicada | `AI__OpenRouter__*` | Compose usa `OPENROUTER_*` |
| Roles PostgreSQL | `POSTGRES_APP_*`, `POSTGRES_MIGRATOR_*`, `POSTGRES_BACKUP_*` + `DATABASE_*_URL` | Entregue em `SEC-RLS-RUNTIME`; API/Worker usam app role |
| Projeto Compose | `COMPOSE_PROJECT_NAME` | Consumida pelo Docker Compose CLI |

### Chaves de aplicação sem contrato fechado

Estes itens devem ser resolvidos ou documentados como intencionalmente fixos em B-105:

| Seção appsettings | Chaves | Status |
|-------------------|--------|--------|
| `Files` | `MaxSizeBytes`, TTLs, `AllowedContentTypes` | Defaults em Development/código; não exposto pelo Compose |
| `RateLimit` | `SendPerMinute`, `HubPerMinute` | Development/defaults; não exposto |
| `Cors` | `Origins` | Default em código; não exposto |
| `Authentication` | `RequireHttpsMetadata` | Compose não injeta; revisar Production |
| `Minio` | `Endpoint`, `UseSsl` | Apenas `PublicEndpoint` é mapeado explicitamente |
| `Observability` | `GrafanaUrl` | Default pode divergir de `GRAFANA_PORT` |

### Só via admin UI (não entram no `.env` mínimo)

| Área | Endpoint / tabela | Motivo |
|------|-------------------|--------|
| Webhooks | `PUT /admin/settings` → `integrations.webhook_endpoints` | URL e secret por workspace; HMAC compartilhado com consumidor (B-048) |
| Retenção por tenant | `retention.*` em admin settings | Política de negócio por workspace (B-047) |
| AI workspace toggle | `ai.workspaceEnabled` | Liga/desliga IA no workspace sem redeploy |
| SMTP override não-secreto | `email.*` em DB | Host/port/from podem divergir do env por tenant |

## Critérios de aceite (B-105)

- [x] Inventário documental das substituições do Compose e variáveis extras do template
- [x] Gaps de injeção SMTP/retenção/aliases registrados explicitamente
- [ ] `.env.example` e `compose.yaml` alinhados ao catálogo (exige mudança executável)
- [ ] Tabela final env → binding → serviço → obrigatório em prod, após remover aliases
- [ ] Matriz env vs `/admin` revisada com Security (B-069 / `modelo-ameacas.md`)
- [x] `docs/operations/operacao.md` e `docs/operations/desenvolvimento.md` linkam este guia
- [ ] Smoke comprova e-mail, IA e retenção opt-in dentro dos containers
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
B-105 continua `Planned` porque injetar SMTP/retenção, remover aliases e comprovar
os serviços dentro dos containers são mudanças executáveis, não documentação.

## Bootstrap rápido (hoje)

```bash
cp .env.example .env
# Ajustar CHANGE_ME / *_change_me para o ambiente
task apps
```

Produção: `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false`, secrets reais só via `.env` ou secret manager (D-04). Ver também [`operacao.md`](./operacao.md) e [`runbooks/README.md`](./runbooks/README.md).
