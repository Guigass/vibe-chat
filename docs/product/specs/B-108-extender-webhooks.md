# B-108 — Extender webhooks outbound

> Wave W10-12 · Trilha B/C/D · Deps: B-048, B-069 · Decisões: —

## Problema

B-048 entregou o mínimo: **1 endpoint por tenant**, só evento `MessageCreated`,
HMAC, URL/secret no admin. Integrações reais precisam de mais eventos (edit,
delete, reação, membership), escolher o que assinar, eventualmente mais de um
destino e um “ping” de teste — sem isso o webhook fica incompleto frente ao
benchmark (Slack/Teams).

## Escopo

Estender `integrations.webhook_endpoints` + UI admin (settings / B-106):

- **Catálogo de eventos assináveis** (opt-in por endpoint), no mínimo:
  - `MessageCreated` (já existe)
  - `MessageEdited`
  - `MessageDeleted`
  - `ReactionChanged`
  - `MemberInvited` / `MemberRoleChanged` (se já houver outbox equivalente)
- **Filtros opcionais** por endpoint: lista de `channelId` (vazio = todos os canais
  do tenant aos quais o fan-out já se aplica); nunca vazar canal de outro tenant
- **Múltiplos endpoints** por tenant (limite baixo, ex.: 5) — cada um com
  `name`, `enabled`, `url`, `secret`, `subscribedEvents[]`, `channelFilter[]?`
- **Ping de teste** no admin: `POST .../admin/webhooks/{id}/test` envia evento
  sintético `WebhookTest` assinado; não grava mensagem de negócio
- Headers atuais preservados; incluir `X-VibeChat-Event` com o tipo real
- Secret mascarado no GET; rotação igual B-048/B-069 (`workspace.admin` só)
- Delivery continua best-effort via outbox/worker **após** realtime; falha de
  HTTP não reprocessa o outbox de mensagem (mesmo contrato B-048), mas registra
  `lastStatus` / `lastErrorAt` no endpoint para a UI

## Fora de escopo

- Webhooks inbound genéricos sem identidade — envio por terceiros é **B-109**
  (núcleo) + **B-110** (install); capabilities avançadas **B-066** (W15);
  loja pública continua fora (D-11)
- Fila dedicada ou bus externo (ADR-015)
- Retry agressivo com DLQ completa (pode ser follow-up; aqui só last-status)
- Assinar eventos de AI ou export
- Transformação custom de payload (sempre o JSON canônico do outbox / DTO estável)

## Contratos

- Migration: endpoints N por tenant; colunas `Name`, `SubscribedEvents` (json/text),
  `ChannelFilter` (json uuid[]?), `LastDeliveryAt`, `LastStatusCode`, `LastError`
- `GET/PUT /admin/settings` (ou sub-recursos `GET/POST/PUT/DELETE .../admin/webhooks`)
  — documentar em `contratos.md`; preferir CRUD dedicado se a lista passar de 1
- Eventos novos no worker path de delivery outbound (não no hot path SendMessage)
- RLS por `TenantId`; testes T11 permanecem (secret nunca em claro)

## UX

- Admin → Settings → Webhooks: lista de endpoints, toggle, eventos (checkboxes),
  filtro de canais, “Enviar teste”, rotacionar secret
- Visível só com `workspace.admin` (matriz B-106)
- Empty state: “Nenhum webhook — adicione um endpoint HTTPS”

## Multi-tenant e authZ

- Só `workspace.admin`; Auditor/Member → 403 e UI escondida (B-106)
- Channel filter só aceita canais do tenant; ID fora → 400
- Payload nunca inclui dados de outro tenant

## Aceite

- [ ] Assinar só `MessageEdited` → create não dispara; edit dispara
- [ ] Dois endpoints ativos recebem o mesmo evento (se ambos assinaram)
- [ ] Filtro de canal: mensagem em outro canal não entrega
- [ ] Ping de teste retorna status e atualiza lastStatus
- [ ] Secret mascarado; Member/Auditor 403
- [ ] Limite de endpoints respeitado

## Testes

- Integration: matriz evento × subscription × channel filter × multi-endpoint
- Security: T11 + cross-tenant channel id no filter → 400/403
- Unit: seleção de eventos no admin; mask de secret
- E2E smoke opcional: admin salva endpoint e vê lastStatus após ping

## Riscos

- Explosão de deliveries → limite de endpoints + timeout curto (~5s) mantido
- Assinar “tudo” por default → default = só `MessageCreated` (compat B-048)
- SSRF via URL → mesma régua B-048 (`https` ou localhost em lab)
