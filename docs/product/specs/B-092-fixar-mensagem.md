# B-092 — Fixar mensagem

> Wave W9-5 · Trilha C/D · Deps: B-089 · Decisões: D-11 · Risco R2

## Problema

Não há onde guardar a informação de referência do canal — link do board, combinado da
equipe, decisão tomada. Ela é repetida toda semana porque some no histórico.

## Escopo

- Fixar/desafixar mensagem no canal, com limite de **20** fixadas.
- Barra no topo do canal com a contagem e acesso à lista.
- Lista de fixadas em painel lateral, com “ir até a mensagem” (usa B-089).
- Mensagem fixada mostra um marcador na bolha.
- Fixar e desafixar geram evento de sistema visível no canal (transparência).
- Apagar a mensagem desafixa automaticamente.

## Fora de escopo

- Fixar em DM sem membership compartilhada, fixar anexo isolado, ordenação manual.

## Contratos

Tabela nova `messaging.pinned_messages`:

| Coluna | Tipo |
|--------|------|
| `TenantId` | uuid, NOT NULL, RLS |
| `ChannelId`, `MessageId` | uuid |
| `PinnedByUserId` | uuid |
| `PinnedAt` | timestamptz |

Único por (`TenantId`, `ChannelId`, `MessageId`).

- `POST .../channels/{channelId}/messages/{messageId}/pin`
- `DELETE .../channels/{channelId}/messages/{messageId}/pin`
- `GET .../channels/{channelId}/pins`
- Evento de hub `PinChanged` com `{ messageId, pinned, byUserId }`, via outbox.
- Permissão nova `message.pin` (default: membros; admin pode restringir a managers).

`contratos.md`: tabela, três endpoints, evento e permissão.

## UX

- Barra fina no topo: texto “3 fixadas” (sem emoji; ícone do design system se houver), abre o painel.
- Painel lista autor, trecho e data, com “ir até” e “desafixar”.
- No limite, a UI explica e sugere desafixar antes.
- Barra some quando não há fixadas — sem espaço vazio permanente.

## Multi-tenant e authZ

- `pinned_messages` tem `TenantId` + RLS.
- Fixar exige membership no canal **e** `message.pin`; a mensagem tem de ser do mesmo
  canal (teste negativo obrigatório).
- Fixar/desafixar entra no audit como `message.pin` / `message.unpin`.

## Aceite

- [ ] Fixar mostra a barra nas duas sessões sem F5
- [ ] “Ir até” rola até a mensagem, mesmo fora da página carregada
- [ ] 21ª fixada é bloqueada com mensagem clara
- [ ] Apagar a mensagem remove da lista de fixadas
- [ ] Sem `message.pin` → 403
- [ ] Fixar mensagem de outro canal → 400

## Testes

- Integration: fixar/desafixar, limite, unicidade, desafixar em cascata no delete.
- Security: cross-tenant → 403; sem permissão → 403.
- E2E: Alice fixa, Bob vê a barra ao vivo.

## Riscos

- Fixadas viram entulho → limite de 20 e evento visível no canal.
- Mensagem fixada apagada deixando referência morta → cascata no soft-delete.
