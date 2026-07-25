# B-094 — Recibos de leitura e não lidas

> Wave W9-7 · Trilha C/D · Deps: B-088 · Decisões: D-11

## Problema

`ApiService.upsertReadCursor` existe e **nunca é chamado** pela UI. O resultado é que o
badge de não lidas nunca zera de forma confiável, o divisor de não lidas (B-088) não
tem em que se ancorar e ninguém sabe se a mensagem foi vista.

## Escopo

- Gravar o cursor de leitura quando a última mensagem visível muda e a aba está em foco
  (debounce de 1 s, e no `blur`).
- Contagem de não lidas por canal derivada de `maxSeq - lastReadSeq`.
- Marcar canal como lido / marcar como não lido a partir de uma mensagem.
- Indicador de leitura em **DM**: “visto” quando o outro lado leu.
- Em canal, indicador agregado: “lido por N” no menu da mensagem, sem lista nominal.
- Preferência de privacidade por usuário: desligar recibo de leitura (quem desliga
  também não vê o dos outros — simetria).

## Fora de escopo

- Recibo de entrega por dispositivo; lista nominal de quem leu em canal grande;
  “digitando” persistente.

## Contratos

`messaging.read_cursors` já existe. Novo:

- `PUT .../channels/{channelId}/read-cursor` — `{ lastReadSeq }`; monotônico, nunca
  retrocede sozinho (retroceder só via “marcar como não lido”, com flag explícita).
- `GET .../channels/unread` — resumo por canal: `{ channelId, unreadCount, mentionCount, lastReadSeq }`.
- `GET .../messages/{messageId}/read-by` — contagem e, em DM, quem leu.
- Evento de hub `ReadCursorChanged` só para a **própria** sessão do usuário
  (multi-dispositivo) e, em DM, para o outro participante.
- Preferência `readReceiptsEnabled` no perfil do usuário.

`contratos.md`: endpoints, evento e a preferência.

## UX

- Badge de não lidas some ao ler; badge de menção (B-082) tem cor própria.
- DM mostra “visto” discreto abaixo da última mensagem própria.
- “Marcar como não lido” no menu da mensagem reposiciona o divisor de B-088.
- Com recibo desligado, a UI explica a simetria ao invés de só sumir.

## Multi-tenant e authZ

- Cursor é por (`tenant`, `user`, `channel`) e exige membership; nunca é possível
  gravar ou ler cursor de terceiro.
- `read-by` em canal devolve **contagem**, não identidade — evita vigilância de
  leitura em canal grande e reduz superfície de privacidade.
- Cursor de outro tenant → 403, com teste negativo.

## Aceite

- [ ] Abrir canal e rolar até o fim zera o badge
- [ ] Reabrir o canal não traz o badge de volta
- [ ] Multi-dispositivo: ler no A zera no B via `ReadCursorChanged`
- [ ] DM mostra “visto” só depois de o outro abrir
- [ ] Recibo desligado esconde nos dois sentidos
- [ ] “Marcar como não lido” reposiciona o divisor
- [ ] Cursor cross-tenant → 403

## Testes

- Integration: monotonicidade do cursor; resumo de não lidas com menções;
  `read-by` em DM e canal.
- Security: gravar cursor de outro usuário → 403; cross-tenant → 403.
- Unit (web): debounce, cálculo de não lidas, marcar como não lido.
- E2E: duas sessões conferindo badge e “visto”.

## Riscos

- Gravação de cursor a cada scroll → debounce + só com aba em foco.
- Recibo de leitura como vigilância → agregado em canal, opt-out simétrico e
  registro disso no `modelo-ameacas.md`.
