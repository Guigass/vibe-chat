# B-083 — Emoji picker e reações livres

> Wave W8-5 · Trilha C/D · Deps: B-024 · Decisões: D-11

## Problema

As reações são seis emojis fixos (`REACTION_EMOJI_OPTIONS` em
`apps/web/src/app/shared/ui/message-bubble/message-bubble.ts`) e o composer não tem
seletor de emoji nenhum. Reagir com 🚀 ou responder com 😅 é impossível.

## Escopo

- Seletor de emoji compartilhado: usado no composer e na barra de reação da bolha.
- Busca por nome (pt-BR e en), categorias, aba de **usados recentemente** (local).
- Reação com qualquer emoji Unicode válido, validado no servidor.
- Tooltip da reação lista quem reagiu (até 10 nomes + “e mais N”).
- Barra rápida com os 6 emojis atuais mantida, seguida de “mais…”.

## Fora de escopo

- Emoji customizado por workspace (upload) — item futuro; exige storage e moderação.
- Reação em anexo isolado, sticker e GIF.

## Contratos

`messaging.message_reactions.Emoji` deixa de ser validado contra lista fixa e passa a
validar **forma**: sequência Unicode de emoji, até 8 code points, sem texto. A lista
`AllowedReactionEmojis` sai do código de domínio.

- `GET .../messages/{messageId}/reactions/{emoji}/users` — quem reagiu; membership.
- Evento `ReactionChanged` ganha `topUsers: string[]` (até 3) para o tooltip inicial.

`contratos.md`: nova regra de validação e endpoint de quem reagiu.

## UX

- Picker em popover ancorado, com foco preso enquanto aberto e `Esc` para fechar.
- Grade virtualizada — o conjunto Unicode completo não pode travar a rolagem.
- Chip da reação: emoji + contagem; borda de destaque quando o próprio usuário reagiu.
- Sem conexão, a reação fica otimista e reverte com aviso se o envio falhar.

## Multi-tenant e authZ

Permissão `message.react` já existe e continua valendo. Validação de emoji é no
servidor — o cliente não é fonte de verdade. Reagir em mensagem de outro tenant
continua 403.

## Aceite

- [ ] Reagir com 🚀 funciona e aparece nas duas sessões
- [ ] Buscar “foguete” encontra 🚀
- [ ] Recentes persistem entre recargas
- [ ] Texto puro (`":)"`) enviado como reação → 400
- [ ] Tooltip mostra quem reagiu
- [ ] Picker navegável só por teclado

## Testes

- Unit (api): validador de emoji aceita sequências ZWJ e rejeita texto.
- Unit (web): busca por nome nos dois idiomas; lista de recentes.
- Integration: toggle de reação arbitrária persiste e emite `ReactionChanged`.
- Security: reagir cross-tenant → 403.

## Riscos

- Bundle do dataset de emoji → carregar sob demanda, fora do bundle inicial.
- Emoji inválido/spoofado no banco → validação estrita no servidor, não no cliente.
