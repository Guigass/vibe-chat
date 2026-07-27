# B-082 — Menções

> Wave W8-4 · Trilha B/C/D · Deps: B-081 · Decisões: D-11 · Risco R2

## Problema

Não existe forma de chamar alguém. Em canal com 20 pessoas, a mensagem que precisa de
resposta de uma pessoa específica se perde igual às outras. Menção é o que separa
“mensagem lida por acaso” de “mensagem endereçada”.

## Escopo

- `@` no composer abre autocomplete de membros **do canal**, filtrando por nome e e-mail.
- `@aqui` notifica quem está online no canal; `@canal` notifica todos os membros.
- Menção vira um token estável no corpo: `<@userId>` (id, não nome — nome muda).
- Renderização como chip clicável; menção ao próprio usuário destacada.
- Contador de menções separado do contador de não lidas na sidebar.
- Persistência de menção por mensagem, para a UI de não lidas e o push (B-095).

## Fora de escopo

- `@grupo` / user groups — sem modelo de grupo hoje.
- Menção de canal (`#canal`) — vira item próprio se houver demanda.
- Notificação por e-mail de menção — depende de B-097.

## Contratos

Tabela nova `messaging.message_mentions`:

| Coluna | Tipo |
|--------|------|
| `TenantId` | uuid, NOT NULL, RLS |
| `MessageId` | uuid |
| `ChannelId` | uuid |
| `MentionedUserId` | uuid? (nulo para `@aqui`/`@canal`) |
| `Kind` | enum `User` \| `Here` \| `Channel` |

Escrita na **mesma transação** da mensagem (invariante de messaging).

- `GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/members?query=` — para o
  autocomplete; exige membership; devolve só membros do canal.
- Evento de hub `MessageCreated` (broadcast no grupo do canal) ganha
  `mentionedUserIds: uuid[]` e `mentionKinds` (`User`/`Here`/`Channel` presentes).
  Cada cliente deriva `mentionsMe` localmente a partir do próprio user id e dos kinds —
  **não** colocar `mentionsMe: bool` no payload compartilhado (todos receberiam o mesmo valor).

`contratos.md`: tabela, endpoint, campo do evento, formato `<@userId>`.

## UX

- Autocomplete: até 8 resultados, navegação com setas, `Enter`/`Tab` seleciona, `Esc` fecha.
- `@aqui` e `@canal` aparecem no topo da lista com aviso de quantas pessoas notificam.
- Chip da menção usa `--color-accent-soft`; a menção ao próprio usuário destaca a bolha inteira.
- Sidebar: badge numérico de menções (cor de destaque) distinto do ponto de não lidas.
- Menção a quem saiu do canal renderiza como texto neutro, não como chip morto.

## Multi-tenant e authZ

- O autocomplete só devolve membros do canal — nunca o diretório do workspace, e nunca
  outro tenant. É o mesmo caminho de authZ do histórico.
- `@canal` em canal grande exige a permissão `channel.mention_all` (default: quem pode
  postar), para evitar barulho.
- `message_mentions` tem `TenantId` + RLS como toda tabela de negócio.

## Aceite

- [ ] `@ali` sugere Alice; `Enter` insere o chip
- [ ] A mensagem no banco guarda `<@uuid>`, não `@Alice`
- [ ] Alice vê a bolha destacada e o badge de menção no canal
- [ ] Trocar o nome de exibição de Alice atualiza o chip nas mensagens antigas
- [ ] Autocomplete não sugere quem não é membro do canal
- [ ] `@canal` sem permissão → 403 e aviso no composer

## Testes

- Unit (web): parser de `<@id>`, filtro do autocomplete, navegação por teclado.
- Integration: enviar com 2 menções grava 2 linhas na mesma transação.
- Security: autocomplete cross-tenant → 403; menção a usuário de outro tenant é ignorada.
- E2E: Alice menciona Bob; Bob vê badge sem recarregar.

## Riscos

- `@canal` virando spam → permissão + confirmação acima de N membros.
- Autocomplete disparando query por tecla → debounce de 200 ms e cache por canal.
