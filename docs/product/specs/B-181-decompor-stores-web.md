# B-181 — Decompor stores e hub do web

> Wave W19-4 · Trilha D · Deps: W19-3 (recomendado) · Risco R1

## Problema

Stores e serviços de estado do chat concentram lógica demais em poucos arquivos:
`message.store.ts` (~874 linhas), `chat-hub.service.ts` (~817),
`channel.store.ts` (~444), `thread.store.ts` (~463). Isso mistura ingestão
SignalR, normalização, unread, optimistic UI e side effects.

## Escopo

- Separar responsabilidades por concern (ex.: ingestão hub, normalização de
  mensagens, unread/cursor, optimistic send, thread state).
- Extrair helpers puros testáveis onde fizer sentido (sort por `seq`, merge de
  gap-fill, dedupe).
- Preservar API pública dos stores consumida por componentes (signals/métodos
  existentes) ou migrar consumidores no mesmo PR.
- Meta: nenhum store/hub > 400 linhas após decomposição.

## Fora de escopo

- Mudar protocolo SignalR ou contratos de hub.
- Refatorar `api.service.ts` (B-180) ou componentes visuais (B-182).
- Novas features de mensageria.

## Contratos

Sem mudança de eventos hub ou payloads. Comportamento de unread/reconnect
idêntico.

## Multi-tenant e authZ

Preservar filtros por tenant/canal; sem vazamento de estado entre sessões.

## Aceite

- [ ] Stores/hub decompostos; nenhum arquivo > 400 linhas.
- [ ] `npm test` e E2E dois usuários verdes.
- [ ] Reconnect + gap-fill + typing continuam funcionando.

## Testes

- Vitest dos helpers extraídos.
- E2E: dois usuários, envio, edit, reação, reconnect.

## Riscos

- Regressão em race de optimistic send — cobrir com testes existentes de
  dedupe/`clientMessageId`.
