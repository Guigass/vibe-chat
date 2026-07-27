# B-094 — Recibos de leitura e não lidas (persistência definitiva)

> Wave W9-7 · Trilha C/D · Deps: B-088 · Decisões: D-11 · Risco R2

## Problema

`ApiService.upsertReadCursor` existe e **nunca é chamado** pela UI. Hoje o badge de
não lidas é efêmero no client: `ChannelStore.selectChannel` zera `unreadCount` só
em memória, sem gravar cursor no servidor. Resultado: F5 / novo login / outro
dispositivo trazem as “não lidas” de volta (ou as apagam sem ter lido de verdade);
o divisor de B-088 não tem âncora confiável; ninguém sabe se a mensagem foi vista.

A contagem e o estado de “não lida” precisam **persistir de forma definitiva** no
Postgres (`messaging.read_cursors`), não em memória nem em `localStorage`.

## Escopo

- **Fonte da verdade:** `messaging.read_cursors` por (`tenant`, `user`, `channel`).
  Badge, divisor e push (B-095) leem desse cursor — nunca só do estado Angular.
- Gravar o cursor quando a última mensagem visível muda e a aba está em foco
  (debounce de 1 s, e no `blur`).
- Contagem de não lidas por canal: `maxSeq - lastReadSeq` (e menções via B-082).
- Hidratar badges no boot / reconnect via `GET .../channels/unread` (resumo), não
  N chamadas por canal se o endpoint agregado existir.
- Abrir o canal **não** zera o badge sozinho: só avança o cursor quando o usuário
  de fato lê (fim da timeline visível / “marcar como lido”).
- Marcar canal como lido / marcar como não lido a partir de uma mensagem
  (retrocesso explícito do cursor).
- Indicador de leitura em **DM**: “visto” quando o outro lado leu.
- Em canal, indicador agregado: “lido por N” no menu da mensagem, sem lista nominal.
- Preferência de privacidade: desligar recibo de leitura (simetria — quem desliga
  também não vê o dos outros).
- Multi-dispositivo: `ReadCursorChanged` sincroniza badges entre sessões do mesmo
  usuário.

## Fora de escopo

- Recibo de entrega por dispositivo; lista nominal de quem leu em canal grande;
  “digitando” persistente.
- Web Push / Notification API — é **B-095** (usa este cursor para não notificar o
  que já foi lido noutro dispositivo).
- Centro de notificações in-app com histórico longo — follow-up se produto pedir.

## Contratos

`messaging.read_cursors` já existe. Garantir / completar:

- `PUT .../channels/{channelId}/read-cursor` — `{ lastReadSeq }`; monotônico, nunca
  retrocede sozinho (retroceder só via “marcar como não lido”, com flag explícita).
- `GET .../channels/unread` — resumo por canal: `{ channelId, unreadCount, mentionCount, lastReadSeq }`.
- `GET .../messages/{messageId}/read-by` — contagem e, em DM, quem leu.
- Evento de hub `ReadCursorChanged` só para a **própria** sessão do usuário
  (multi-dispositivo) e, em DM, para o outro participante.
- Preferência `readReceiptsEnabled` no perfil do usuário.

`contratos.md`: endpoints, evento e a preferência. Cursor com RLS por `tenant_id`.

## UX

- Badge de não lidas **sobrevive** a F5, logout/login e troca de aba/dispositivo até
  o cursor avançar de verdade.
- Badge some ao ler; badge de menção (B-082) tem cor própria.
- DM mostra “visto” discreto abaixo da última mensagem própria.
- “Marcar como não lido” no menu da mensagem reposiciona o divisor de B-088 e
  **persiste** o cursor retrocedido no servidor.
- Com recibo desligado, a UI explica a simetria ao invés de só sumir.

## Multi-tenant e authZ

- Cursor é por (`tenant`, `user`, `channel`) e exige membership; nunca é possível
  gravar ou ler cursor de terceiro.
- `read-by` em canal devolve **contagem**, não identidade — evita vigilância de
  leitura em canal grande e reduz superfície de privacidade.
- Cursor de outro tenant → 403, com teste negativo.

## Aceite

- [ ] Abrir canal e rolar até o fim zera o badge **e** grava cursor no servidor
- [ ] F5 / novo login: badge permanece zerado se já leu; permanece >0 se não leu
- [ ] Só abrir o canal (sem ler até o fim) **não** apaga o badge de forma permanente
- [ ] Multi-dispositivo: ler no A zera no B via `ReadCursorChanged`
- [ ] DM mostra “visto” só depois de o outro abrir
- [ ] Recibo desligado esconde nos dois sentidos
- [ ] “Marcar como não lido” reposiciona o divisor e sobrevive a F5
- [ ] Cursor cross-tenant → 403

## Testes

- Integration: monotonicidade do cursor; resumo de não lidas com menções;
  `read-by` em DM e canal; persistência após “releitura” da API.
- Security: gravar cursor de outro usuário → 403; cross-tenant → 403.
- Unit (web): debounce, cálculo de não lidas, marcar como não lido; **proibir**
  zerar badge só em memória sem `upsertReadCursor`.
- E2E: duas sessões conferindo badge e “visto”; reload confirma badge estável.

## Riscos

- Gravação de cursor a cada scroll → debounce + só com aba em foco.
- Zerar badge no `selectChannel` sem PUT → regressão explícita a cobrir no E2E.
- Recibo de leitura como vigilância → agregado em canal, opt-out simétrico e
  registro disso no `modelo-ameacas.md`.
