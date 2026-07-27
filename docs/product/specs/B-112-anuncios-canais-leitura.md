# B-112 — Anúncios e canais somente leitura

> Wave 11 · Trilha B/C/D · Deps: W9-7, B-041 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Comunicações oficiais se perdem na timeline e não há como separar publicação de
leitura nem medir confirmação quando ela é necessária.

## Escopo

- Modo de channel `Announcement`.
- Permissões `announcement.publish` e `announcement.acknowledge`.
- Mensagem pode exigir confirmação explícita até uma data.
- Lista agregada de confirmações para publisher/admin.
- Badge e inbox distinguem anúncio de mensagem comum.
- Audit de criação, publicação, edição e encerramento.

## Fora de escopo

- Assinatura eletrônica legal.
- Disparo para usuários fora do workspace.
- Tornar qualquer canal existente read-only sem migration explícita.

## Contratos

Channel ganha modo/capabilities; anúncio preserva `Message` e `seq`. API de
acknowledgement é idempotente. Eventos `announcement.published` e
`announcement.acknowledged`; contratos finais entram em `contratos.md`.

## UX

Composer fica indisponível para quem não publica. Anúncio mostra autor, data,
prazo e estado de confirmação; leitor pode confirmar por teclado.

## Multi-tenant e authZ

Somente membros autorizados leem. Relatório de confirmação exige
`announcement.publish` ou `workspace.admin`; RLS em acknowledgements.

## Aceite

- [ ] Usuário comum lê, mas não publica.
- [ ] Publisher cria e edita anúncio segundo B-107.
- [ ] Confirmação repetida não duplica contagem.
- [ ] Inbox/badge mostram pendência.
- [ ] Relatório não cruza tenant/channel.

## Testes

Integração de publish/ack; security de leitura/publicação/relatório; E2E com
publisher e member; realtime e reconnect.

## Riscos

Confundir confirmação com prova legal; deixar isso explícito. Fan-out de muitos
acks deve ser agregado e limitado.

