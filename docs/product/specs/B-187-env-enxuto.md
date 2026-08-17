# B-187 — Instalação configurável no admin (integração no DB)

> Wave W7-14 · Trilha A/G · Deps: B-105, B-069 · Decisões: D-04, D-05, D-10 · Risco R1

## Problema

Uma instalação nova ainda exige SMTP, IA, retenção, push, link preview e
kill switches no `.env`. O `/admin/settings` (B-069 / ADR-020) já persiste
política no DB, mas o caminho não fecha: o keyring lab é `CHANGE_ME` e
`RuntimeSettings:DatabaseOverridesEnabled` fica `false`. O operador não
configura o produto na UI.

O `.env` deve ser **só infra** (subir Postgres, IdP, MinIO, Redis, URLs,
pins). Tudo que é política, integração ou feature flag vai ao admin + DB.

Enxugar pins/portas/connection strings **não** é o objetivo.

## Escopo

Regra: **env = infra/bootstrap**. **DB = produto.** Sem registry genérico.

### Permanece no `.env` (infra — precisa existir antes do login)

- Postgres, Redis, Keycloak, MinIO, OIDC, URLs públicas, CORS de MinIO
- Portas de host, pins de imagem, profiles `tools` / `observability` / `proxy`
- Seed / bootstrap / DevAuth (primeiro boot, não política de runtime)
- TLS / proxy / OTel (stack de operação)
- Keyring AES-GCM (`RuntimeSettings:Encryption:*`) — chave mestra **não**
  pode viver no banco que ela destrava
- `RuntimeSettings:DatabaseOverridesEnabled` — fail-safe para ignorar o DB

### Vai ao `/admin/settings` + DB (quando overrides estiverem ligados)

Integração (secrets exigem keyring válido; PUT geral não aceita secret):

- SMTP (host, porta, from, TLS, senha criptografada)
- OpenRouter: API key (envelope) **e** `baseUrl` (https; sem IP privado —
  mesma guarda de webhook). Provider/workspace toggle já na UI
- Webhook URL + signing secret
- VAPID (privada em envelope; pública no GET; subject). Sem VAPID válido o
  push continua no-op

Política / limites (não são secrets):

- Retenção por tenant (`enabled`, `retentionDays`) + knobs do job
  (default days, batch, intervalo)
- Files e RateLimit por tenant. Teto = constantes de código
  (`AttachmentPolicies` / `RateLimitPolicies`), **não** env
- Link preview: timeout + kill switch de processo (toggle por tenant já existe)

Kill switches de **instância** (booleanos, default `false` exceto link
preview `true` como hoje; **não** exigem keyring):

- `Ai:Enabled`
- `Email:Enabled`
- `MessageRetention:Enabled`
- `Push:Enabled`
- `LinkPreview:Enabled`

Singleton tipada `administration.process_settings` (sem `tenant_id`): flags,
`openRouterBaseUrl`, knobs de retenção/link-preview, envelopes VAPID.
PUT existente grava os booleanos/`baseUrl`/knobs; VAPID rota por
`POST .../credentials/vapid/rotate`.

Dois níveis permanecem onde já existem: processo (instância) **e** política
tenant/workspace (`ai.workspaceEnabled`, `email.enabled`, `retention.enabled`,
`linkPreview.enabled`).

### Lab / primeira instalação

- Keyring **demo** no `.env.example` (32 bytes base64, claramente fake).
- Ligar `RuntimeSettings:DatabaseOverridesEnabled` **quando o keyring for
  válido**. `CHANGE_ME*` ou chave ausente → overrides **off**.
- Tirar do template **tudo que não é infra**, inclusive:
  `EMAIL__*`, `SMTP_*`, `AI__*`, `OPENROUTER_*`, `MessageRetention__*`,
  `LinkPreview__*`, `Push__*`.
- Compose pode manter `${…:-}` como fallback de um release; o template não
  pede que o operador preencha.
- Fluxo: `cp .env.example .env` → `task apps` → login admin → produto inteiro
  na UI (flags, SMTP, IA, webhook, retenção, files, rate limit, push/VAPID,
  link preview).

O catálogo canônico continua `docs/operations/configuracao-env.md`. Pins e
portas de infra **ficam** no template.

## Fora de escopo

- Mover Postgres, Redis, Keycloak, MinIO, OIDC, CORS/proxy/TLS, seed, OTel
  ou keyring para o banco (D-04).
- Apagar pins, portas ou vars de profile opcional do `.env.example`.
- Ligar overrides com keyring vazio ou `CHANGE_ME`.
- Registry genérico `settings[key]=value`.
- Secret manager de cloud.
- Redesign da UI `/admin/settings` (estender seções que já existem).

## Contratos

Sem endpoint novo, salvo `POST /api/v1/admin/settings/credentials/vapid/rotate`
(mesmo shape dos outros rotate). Kill switches, `baseUrl` e knobs entram no
PUT geral.

Precedência (emenda ADR-020 / B-187), para **produto**:

1. `DatabaseOverridesEnabled=false` → defaults de código (features off,
   link preview on). Env de produto deixa de ser contrato.
2. Flag on + linha no DB → **SoT DB**.
3. Flag on sem linha → default de código.

PUT geral não aceita secrets. API nunca devolve plaintext. `processSource` /
`Source` = `database` | `default`. Worker lê a mesma singleton.

AuthZ inalterada: `workspace.admin`. Gate de processo afeta a instância
(self-host: o admin é o operador). Audit `settings.change` /
`settings.credential.rotate`.

## UX

Não muda o chat. `/admin/settings` já mostra fonte e o gate “Overrides DB”.
B-187 faz o gate funcionar no lab e deixa a instalação nova configurar o
**produto** na UI, sem pedir SMTP/IA/push/flags no `.env`.

## Multi-tenant e authZ

Inalterado: `workspace.admin`; secrets mascarados; RLS nas tabelas
tenant-aware. `process_settings` é singleton da instância; role runtime
lê/grava só via API admin. Keyring demo no template não é secret de
produção. Defaults seguros: IA/e-mail/retenção/push **off**; link preview
**on** (ADR-021).

## Aceite

- [ ] Lab: keyring demo válido; overrides só com chave válida;
      `CHANGE_ME` / vazio → off.
- [ ] Instalação nova configura **todo o produto** em `/admin/settings`
      (SMTP, OpenRouter key+baseUrl, webhook, retenção, Files, RateLimit,
      link preview, VAPID, cinco kill switches) **sem** essas vars no `.env`.
- [ ] `.env.example` **só** infra: data plane, OIDC, URLs, portas, pins,
      seed/bootstrap, OTel/proxy, keyring, flag de overrides.
- [ ] `.env.example` **não** lista `EMAIL__*`, `SMTP_*`, `AI__*`,
      `OPENROUTER_*`, `MessageRetention__*`, `LinkPreview__*`, `Push__*`.
- [ ] Com overrides on, PUT/rotate persistem; GET devolve fonte `database`;
      worker/API honram o valor.
- [ ] Flag off: consumidores usam default de código (não exigem env de produto).
- [ ] Catálogo e guia: produto na UI; infra no env.
- [ ] `docker compose --env-file .env.example config --quiet` continua válido.
- [ ] Sem secret real no template.

## Testes

- Keyring demo → `IsEncryptionAvailable`; `CHANGE_ME*` → false.
- Com flag+key: rotate OpenRouter/SMTP/VAPID no admin não retorna 503
  `RuntimeSettingsDisabled`.
- Com flag on: PUT dos process flags + `openRouterBaseUrl` grava a singleton;
  sem linha, default de código.
- `openRouterBaseUrl` / webhook: `http` interno e IP privado recusados.
- Flag off: PUT de produto é no-op; features no default seguro.
- `ComposeConfigCatalogTests`: template **não** precisa listar vars de
  produto; **precisa** declarar infra usada sem default.
- Settings mascarados; nenhum secret real no git.

## Riscos

- Keyring demo no git: só lab; produção troca antes de ligar overrides.
- Overrides sem chave: fail-closed para secrets. Flags/knobs não dependem
  do keyring.
- `workspace.admin` liga purge/IA/push na instância: auditado; rollback =
  desligar `DatabaseOverridesEnabled`.
- `openRouterBaseUrl` na UI: guarda https + IP público (mesmo espírito do
  webhook); não é allowlist de vendor.
- Operador procura SMTP/flags no `.env`: catálogo + comentário no template
  apontam `/admin/settings`.
