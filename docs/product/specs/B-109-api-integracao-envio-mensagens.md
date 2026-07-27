# B-109 — Núcleo de plugin: Bot + token + envio de mensagens

> Wave W10-13 · Trilha B/C/D/E · Deps: B-004, B-069, B-021 · Decisões: D-11 (loja pública fora; plugins locais ok) · Risco R3

## Problema

Só humanos autenticados (OIDC / DevAuth) enviam mensagem. Outros sistemas (ERP,
helpdesk, CI, automações) não têm como postar em canal, DM ou como bot. O enum
`Role.Bot` já existe no catálogo, mas **não há** identidade de integração, token
nem endpoint para terceiros. Webhooks atuais (B-048/B-108) são só **outbound**.

Esta fatia é o **núcleo** da trilha de plugins: credencial + send. A fachada de
“instalar plugin” / manifesto é **B-110**; capabilities avançadas são **B-066**
(por último).

## Escopo

Fatia mínima — motor reutilizado por plugins:

- **Conta Bot** (`workspace.admin` cria via API admin ou via B-110):
  - nome/display, avatar opcional depois
  - perfil com `Role.Bot` no workspace
  - **API token** opaco (mostrado **uma vez** na criação/rotação; só hash no DB)
  - escopos: canais permitidos (`channelIds[]`; default restrito: lista explícita)
  - flag `allowDms`; toggle enabled; revogar token invalida na hora
- **Auth:** `Authorization: Bearer vc_int_…` (ou header `X-VibeChat-Integration-Token`)
  — sem OIDC do usuário humano; TenantContext derivado do token
- **Enviar mensagem** (mesmo pipeline de B-004: idempotency + `seq` + outbox):
  - `POST /api/v1/integrations/v1/channels/{channelId}/messages`
    body `{ body, idempotencyKey, threadId? }`
  - `POST /api/v1/integrations/v1/dms` — get-or-create DM + send (`allowDms`)
- Capability implícita desta fatia: `messages.send`
- Mensagem na timeline como autor Bot (badge “bot” mínimo)
- Rate-limit dedicado (Redis) por bot/integração; respeita B-078 quando existir
- Audit: `integration.message.send`, `integration.token.rotate`, criação/revoga
- UI admin mínima aceitável; preferir consolidar em **B-110** (evitar duas UIs)

## Fora de escopo

- Shell de instalação / manifesto / built-ins — **B-110**
- Capabilities avançadas (slash, events, UI hooks, catálogo built-in rico) —
  **B-066** / **B-111** (W15, depois do núcleo)
- Marketplace / App Directory público (D-11)
- Slash commands externos, interactive buttons, modals
- Webhooks inbound genéricos sem identidade Bot
- Leitura ampla de histórico pelo bot
- Editar/apagar / anexos via API de integração (follow-ups)

## Contratos

- Tabelas sugeridas (nomes finais em inglês no schema):
  - `integrations.bots` / `integration_accounts`: `TenantId`, `WorkspaceId`,
    `UserId` (perfil Bot), `Name`, `Enabled`, `CreatedAt`
  - `integrations.bot_tokens`: hash do token, `LastUsedAt`, `RevokedAt`
  - `integrations.bot_channel_scopes`: channel ids + `allowDms`
- Endpoints sob prefixo `/api/v1/integrations/v1/...` (separado do JWT humano)
- Reusa `SendMessage` / outbox / hub — **não** duplicar path de escrita
- `contratos.md` + glossário; RLS por `TenantId`
- Token nunca em log, trace ou GET (só `tokenConfigured` / last4 opcional)

## UX

- Bolha do bot: nome + indicador discreto “Bot”
- Se B-110 ainda não existir: formulário admin mínimo de criar bot/token
- Member não vê a seção (B-106)

## Multi-tenant e authZ

- Token amarra **um** tenant + workspace; cross-tenant → 401/403
- Post em canal fora do escopo ou sem membership do bot → 403
- DM só com usuários do mesmo workspace
- Suíte security dedicada para integration token

## Aceite

- [ ] Sistema externo posta em canal permitido; membros veem em tempo real
- [ ] Canal fora do escopo → 403
- [ ] DM para membro do workspace funciona com `allowDms`
- [ ] Token revogado → 401 na hora
- [ ] Token em claro só na criação/rotação; GET admin nunca devolve o secret
- [ ] Idempotency-Key evita duplicar
- [ ] Cross-tenant com token de outro tenant → 401/403

## Testes

- Integration: send canal + DM + idempotency + revoke
- Security: token leak, escopo, cross-tenant, Member sem admin API
- E2E opcional: criar bot → post → bob vê a mensagem

## Riscos

- Duas UIs (integração crua vs plugin) → B-110 deve absorver a fachada
- Token exfiltrado → hash + rotação + rate-limit + escopo mínimo de canais
- Confundir com marketplace → D-11; B-066 é plataforma local, não loja
