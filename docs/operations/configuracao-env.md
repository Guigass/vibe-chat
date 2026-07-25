# Configuração via `.env` — Admin mínimo

Guia de referência para operar uma instância self-hosted com o **mínimo de dependência da UI `/admin`**. O operador de infra configura a plataforma via `.env`; o `workspace.admin` ajusta políticas por workspace na UI.

**Status:** fase planejada — **W7-7 / B-105** (`docs/roadmap/roadmap.md`). Este documento será completado quando o catálogo estiver fechado.

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

## Inventário inicial (parcial — a completar em B-105)

### Compose / data plane

| Variável | Serviço | Notas |
|----------|---------|-------|
| `POSTGRES_*`, `DATABASE_URL` | postgres, api, worker | SoT; senha `*_change_me` em dev |
| `REDIS_*`, `REDIS_URL` | redis, api, worker | Backplane SignalR + presence |
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
| `EMAIL__*` | `Email__*` | api, worker | SMTP; off default (D-10) |
| `MessageRetention__*` | `MessageRetention__*` | worker | Kill switch purge (B-047) |

### Presentes em `appsettings` mas ainda sem variável em `.env.example` (gaps conhecidos)

Estes itens devem ser resolvidos ou documentados como intencionalmente fixos em B-105:

| Seção appsettings | Chaves | Status |
|-------------------|--------|--------|
| `Files` | `MaxSizeBytes`, `PresignUploadTtlSeconds`, `AllowedContentTypes` | Hardcoded no `compose.yaml` hoje |
| `RateLimit` | `SendPerMinute`, `HubPerMinute` | Só em `appsettings.Development.json` |
| `Cors` | `Origins` | Não exposto em `.env.example` |
| `Authentication` | `RequireHttpsMetadata` | `"false"` no Compose lab |

### Só via admin UI (não entram no `.env` mínimo)

| Área | Endpoint / tabela | Motivo |
|------|-------------------|--------|
| Webhooks | `PUT /admin/settings` → `integrations.webhook_endpoints` | URL e secret por workspace; HMAC compartilhado com consumidor (B-048) |
| Retenção por tenant | `retention.*` em admin settings | Política de negócio por workspace (B-047) |
| AI workspace toggle | `ai.workspaceEnabled` | Liga/desliga IA no workspace sem redeploy |
| SMTP override não-secreto | `email.*` em DB | Host/port/from podem divergir do env por tenant |

## Critérios de aceite (B-105)

- [ ] `.env.example` comentado por seção; toda variável do `compose.yaml` (profiles default + apps + proxy + observability + tools) documentada
- [ ] Tabela env → binding → serviço → obrigatório em prod
- [ ] Matriz env vs `/admin` revisada com Security (B-069 / `modelo-ameacas.md`)
- [ ] `docs/operations/operacao.md` e `docs/operations/desenvolvimento.md` linkam este guia
- [ ] Gaps listados acima resolvidos **ou** registrados como decisão explícita (não silencioso)

## Bootstrap rápido (hoje)

```bash
cp .env.example .env
# Ajustar CHANGE_ME / *_change_me para o ambiente
task apps
```

Produção: `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false`, secrets reais só via `.env` ou secret manager (D-04). Ver também [`operacao.md`](./operacao.md) e [`runbooks/README.md`](./runbooks/README.md).
