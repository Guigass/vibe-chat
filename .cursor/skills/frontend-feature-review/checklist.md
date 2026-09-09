# Checklist — frontend feature review

Usar junto com [SKILL.md](SKILL.md). Marcar o que a feature toca.

## Reusar (não recriar)

| Precisa | Onde já existe |
|---------|----------------|
| Botão / ghost / icon | `shared/ui/button`, `icon-button` |
| Input / textarea | `shared/ui/input`, `textarea` |
| Avatar / empty / skeleton | `shared/ui/avatar`, `empty-state`, `skeleton` |
| Bolha / enquete | `shared/ui/message-bubble`, `poll-card` |
| Comparar ids / “é meu?” | `idsEqual`, `isOwnAuthor` em `core/services/message-sync.ts` |
| Poll HTTP+hub | `shared/polls/poll-summary.ts` (`mapPollSummary`, `mergeRemotePoll`) |
| Tokens / marca | `docs/architecture/design-system.md` |

Export público: `apps/web/src/app/shared/ui/index.ts`.

## Tokens oficiais (não inventar)

`--vc-brand`, `--vc-brand-hover`, `--vc-brand-soft`, `--vc-brand-ink`,
`--vc-ink`, `--vc-ink-muted`, `--vc-ink-subtle`,
`--vc-surface`, `--vc-surface-elevated`, `--vc-border`,
`--vc-danger`, `--vc-success`, `--vc-warning`, `--vc-info`,
`--vc-msg-mine`, `--vc-msg-theirs`, `--vc-composer-bg`,
`--vc-radius-sm|md|lg`, `--vc-ease-out`, `--vc-dur-fast|med`,
`--vc-font-display|body|mono`, tokens de densidade (`--vc-msg-*`, `--vc-timeline-*`).

Proibido: `--vc-line`, hex de marca solto, roxo/indigo, cream `#F4F1EA`.

## Ownership — arquivos que costumam errar

- `message.store.ts` / `thread.store.ts` — `ingestRemote`, `normalize`, `patchByClientId`
- `message-sync.ts` — `mergeMessagesById`, `upsertRemoteMessage`, `findMessageByCorrelators`
- `chat-hub.service.ts` — `mapPayload` (AuthorId / authorId)
- `api.service.ts` — `mapMessage` / mappers de DTO
- `timeline.ts` / `timeline-items.ts` — `timeline__stack--mine` vs autor
- `message-bubble.ts` — classe `vc-msg--mine`, menu, encerrar enquete

## Casing hub / API

Payload SignalR e records .NET podem chegar `question` **ou** `Question`.
Ler os dois no mapper compartilhado. O eco do hub não pode apagar um HTTP
completo (ex.: enquete sem opções). Preferir merge que preserve escolha local
quando o remoto for eco atrasado.

## Markup

- Um `<form>` por fluxo. Painel extra = `<div>`, não form filho.
- Controles com as classes do kit (`vc-input`, `vc-button`), não input nu sem borda.
- Timeline: scroll só na lista; composer fora do scroller.

## Browser (mínimo)

- [ ] Eu envio: direita, sem avatar no stack
- [ ] Outra pessoa envia: esquerda, avatar, nome certo
- [ ] Refresh não muda o lado
- [ ] Light e dark se o CSS mudou
- [ ] Empty / error / desabilitado
