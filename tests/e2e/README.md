# E2E — Playwright (VibeChat)

Fluxo alvo: **duas sessões** (alice e bob) no canal `#geral` — alice envia, bob recebe.

## Pré-requisitos

```bash
# Apps locais
task setup && task dev
task seed

# Dependências E2E
cd tests/e2e
npm ci
npx playwright install chromium
```

## Modos de autenticação

| Modo | `E2E_AUTH_MODE` | Quando usar |
|------|-----------------|-------------|
| Demo UI | `demo` (default) | Smoke sem Keycloak; cada browser tem estado local. Recebimento cruzado real exige API. |
| DevAuth | `devauth` | CI / local sem Keycloak: injeta `X-Dev-User: alice\|bob` nas requests à API/hub (API em `Development`). |
| OIDC | `oidc` | Login real Keycloak (`alice@vibechat.local` / `Demo123!`). |

```bash
# Demo (UI)
E2E_AUTH_MODE=demo npm test

# DevAuth (API Development + web)
E2E_AUTH_MODE=devauth WEB_BASE_URL=http://localhost:4200 API_BASE_URL=http://localhost:5080 npm test

# Keycloak
E2E_AUTH_MODE=oidc npm test
```

Ou via Taskfile: `task test:e2e`.

## CI (W7-1 / B-075)

O job **E2E (Playwright)** em `.github/workflows/ci.yml` sobe o data plane
(`compose.yaml`: postgres/redis/minio), API Development + Web, e roda os specs
com `E2E_AUTH_MODE=devauth` (sem Keycloak).

Reproduzir localmente o mesmo caminho:

```bash
./infra/scripts/ci-e2e.sh
# ou reutilizar stack já no ar:
SKIP_COMPOSE=1 SKIP_APPS=1 E2E_AUTH_MODE=devauth ./infra/scripts/ci-e2e.sh
```

## DevAuth

Quando `ASPNETCORE_ENVIRONMENT=Development`, a API aceita:

```http
X-Dev-User: alice
X-Dev-User: bob
X-Dev-User: demo
```

O helper de teste reescreve `/api/...` → `/api/v1/...` quando necessário e anexa o header por contexto de browser.

## Observações

- Demo mode puro **não** sincroniza mensagens entre browsers (estado local).
- Para assertiva “bob recebe”, use `devauth` ou `oidc` com API + SignalR saudáveis.
