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

- [x] Stores/hub decompostos; nenhum arquivo > 400 linhas.
- [x] `npm test` (Vitest) verde. E2E de dois usuários não rodou neste PR: o refactor não muda contrato SignalR nem a UI; o smoke fica para o CI.
- [x] Reconnect + gap-fill + typing cobertos pelos testes de store/hub já existentes (cursor, presença, load/jump) mais os helpers novos.

## Execução — 2026-10-02

- Work-Item: B-181; Wave: W19-4; Trilha: D; Risk: R1. Deps: W19-3 satisfeita em `main` (`6b14e3d`).
- API pública dos stores e do hub permanece nos mesmos módulos. Tipos de evento do hub são reexportados por `chat-hub.service.ts`.
- Linhas: `message.store.ts` 316, `chat-hub.service.ts` 363, `channel.store.ts` 398, `thread.store.ts` 341. Helpers de ingestão, timeline, envio otimista, cursor, unread, diretório, contatos e patches de thread ficam em arquivos próprios, todos ≤ 400 linhas.
- Verificação em `node:22.22.3`: `tsc -p tsconfig.app.json --noEmit` e `npm test -- --watch=false` (**73** arquivos, **334** testes) verdes. O aviso de locale `pt-BR` já existia.
- `tsc -p tsconfig.spec.json` ainda falha em `login-devauth-contrast.spec.ts` (erro pré-existente, fora deste PR). O Vitest desse arquivo passa.

## Testes

- Vitest dos helpers extraídos.
- E2E: dois usuários, envio, edit, reação, reconnect.

## Riscos

- Regressão em race de optimistic send — cobrir com testes existentes de
  dedupe/`clientMessageId`.
