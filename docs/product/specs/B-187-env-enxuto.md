# B-187 — Instalação configurável no admin (integração no DB)

> Wave W7-14 · Trilha A/G · Deps: B-105, B-069 · Decisões: D-04, D-05, D-10 · Risco R1

## Problema

Uma instalação nova ainda exige SMTP, chave de IA, retenção e detalhes de
integração no `.env`. O `/admin/settings` (B-069 / ADR-020) já persiste isso no
DB, mas o caminho não fecha: o keyring lab é `CHANGE_ME` (criptografia
indisponível) e `RuntimeSettings:DatabaseOverridesEnabled` fica `false`. O
operador não configura a instância na UI.

O `.env.example` mistura infra (que **precisa** continuar no env) com política
de integração (que **já pode** ir ao admin). Enxugar o template inteiro — pins,
portas, profiles, connection strings — **não** é o objetivo: isso só esconde
infra sem habilitar o admin.

## Escopo

Habilitar o caminho **admin + DB** só no que ADR-020 já permite. Infra permanece
no `.env`.

### Vai ao `/admin/settings` + DB (quando o keyring for válido)

- SMTP (host, porta, from, TLS, senha criptografada)
- OpenRouter API key (envelope AES-GCM); provider/workspace toggle já na UI
- Webhook URL + signing secret
- Retenção por tenant (`retentionDays` etc.)
- Files e RateLimit por tenant (sob teto env)

### Permanece no `.env` (não limpar)

- Postgres, Redis, Keycloak, MinIO, OIDC, URLs públicas, CORS, seed/bootstrap
- Portas de host, pins de imagem, profiles `tools` / `observability` / `proxy`
- Kill switches de processo: `AI__Enabled`, `EMAIL__Enabled`,
  `MessageRetention__Enabled`, `Push__Enabled`
- Keyring AES-GCM (`RuntimeSettings:Encryption:*`) e VAPID
- `OPENROUTER_BASE_URL` (ADR-020)

### Lab / primeira instalação

- Provisionar keyring de **demo** no `.env.example` (32 bytes em base64,
  claramente fake — mesmo espírito das senhas `*_change_me`).
- Ligar `RuntimeSettings:DatabaseOverridesEnabled` **quando o keyring for
  válido**. Placeholder `CHANGE_ME*` ou chave ausente → overrides **off**
  (fail-safe). Produção: gerar chave real, depois ligar.
- Tirar do template só o detalhe de integração que passou ao admin:
  `EMAIL__Smtp__*`, aliases `SMTP_*`, `OPENROUTER_API_KEY`,
  `MessageRetention__DefaultRetentionDays` / `BatchSize` / `IntervalMinutes`.
  Kill switches e infra **ficam**.
- Documentar o fluxo: `cp .env.example .env` → `task apps` → login admin →
  SMTP/IA/webhook/retenção/Files/RateLimit na UI.

O catálogo canônico continua `docs/operations/configuracao-env.md`. Não exigir
que o template omita pins/portas.

## Fora de escopo

- Mover Postgres, Redis, Keycloak, MinIO, OIDC, CORS/proxy/TLS, seed, OTel,
  BaseUrl OpenRouter, keyring ou VAPID para o banco (D-04 / ADR-020).
- Apagar pins, portas ou vars de profile opcional do `.env.example`.
- Ligar overrides com keyring vazio ou `CHANGE_ME`.
- Registry genérico `settings[key]=value`.
- Secret manager de cloud.
- Redesign da UI `/admin/settings`.

## Contratos

Sem endpoint novo. Precedência inalterada (ADR-020): kill switch/teto env →
override DB → fallback env → default seguro. PUT geral não aceita secrets;
rotação continua nos endpoints dedicados. API nunca devolve plaintext.

## UX

Não muda o chat. `/admin/settings` já mostra fonte (`env` / `workspace` /
`default`) e o gate “Overrides DB” + estado do keyring. B-187 faz esse gate
funcionar no lab e deixa a instalação nova configurar integrações na UI, sem
pedir SMTP/IA no `.env`.

## Multi-tenant e authZ

Inalterado: `workspace.admin` em settings; secrets mascarados; RLS nas tabelas
de settings. Keyring demo no template não é secret de produção.

## Aceite

- [ ] Lab: keyring demo válido no `.env.example`; overrides ligados só com
      chave válida; `CHANGE_ME` / vazio → off.
- [ ] Instalação nova configura SMTP, OpenRouter, webhook, retenção, Files e
      RateLimit em `/admin/settings` sem essas vars no `.env`.
- [ ] `.env.example` **mantém** infra, portas, pins, URLs, kill switches, VAPID
      e keyring.
- [ ] `.env.example` **remove** `EMAIL__Smtp__*`, `SMTP_*`, `OPENROUTER_API_KEY`
      e detalhe de retenção (não o kill switch).
- [ ] Catálogo e guia do admin descrevem: integração na UI; infra no env.
- [ ] `docker compose --env-file .env.example config --quiet` continua válido.
- [ ] Sem secret real no template.

## Testes

- Keyring demo → `IsEncryptionAvailable`; placeholder `CHANGE_ME*` → false.
- Com flag+key: rotate OpenRouter/SMTP no admin não retorna 503
  `RuntimeSettingsDisabled`.
- `ComposeConfigCatalogTests`: bindings Compose de EMAIL/AI/retenção/push
  permanecem; template **não** precisa listar `EMAIL__Smtp__*` /
  `OPENROUTER_API_KEY`; **precisa** continuar declarando infra usada sem default.
- Settings mascarados; nenhum secret real no git.

## Riscos

- Keyring demo no git: só lab, rotulado como fake; produção troca antes de
  ligar overrides.
- Overrides sem chave: fail-closed (já em `RuntimeSecretProtector`).
- Operador procura SMTP no `.env`: catálogo + comentário no template apontam
  `/admin/settings`.
