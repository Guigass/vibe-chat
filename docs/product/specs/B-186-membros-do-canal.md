# B-186 — Membros do canal (lista + gestão)

> Wave W10-15 · Trilha B/D · Deps: B-020 · Soft-deps: B-171 · Decisões: D-07 · Risco R2

## Problema

B-020 entregou spaces e criar canal, mas a gestão de quem participa ficou
incompleta. Em canal privado só o criador entra em `channel_members`; não há UI
de roster nem API de add/remove para membros do workspace. O `GET …/members`
atual serve só autocomplete de menção (máx. 8). Convite de **externo** é B-040;
este item cobre membros **já do workspace**.

## Escopo

- Lista completa de membros do canal (roster) para quem já tem membership.
- Canal **público**: roster = membros do workspace (somente leitura; “todos do
  workspace” — sem add/remove individual).
- Canal **privado**: add/remove de `userId` que já seja `workspace_member`.
- Criador e quem tem `channel.manage` (ou `workspace.admin`) gerem private;
  membro comum pode **sair** do privado (exceto se for o último membro com
  `channel.manage` — bloquear até transferir ou remover o canal).
- Contagem no cabeçalho do canal; abrir roster no painel de contexto (ou
  overlay no narrow).
- Mensagem de sistema ao adicionar/remover (mesmo padrão de B-101 quando existir).
- Audit `channel.member.add` / `channel.member.remove` / `channel.member.leave`.

## Fora de escopo

- Guest / link de convite externo → **B-040**.
- DM em grupo / participantes de GroupDm → **B-101** (reusa ideias; endpoints
  distintos).
- Papéis granulares **dentro** do canal (moderador de canal).
- Auto-join em massa, sync SCIM → canal (B-128).
- Redesign da sidebar esquerda (B-184) ou só largura do painel (B-171).

## Contratos

Reusa `conversations.channel_members`. Estender o GET existente:

| Endpoint | Notas |
|----------|-------|
| `GET …/channels/{channelId}/members` | Sem `query`: lista paginada (`limit` default 50, máx. 200, `cursor`); com `query`: autocomplete até 8 (comportamento atual). Resposta inclui `userId`, `displayName`, `email?`, `joinedAt`, `presence?` se já houver |
| `POST …/channels/{channelId}/members` | Body `{ userId }`; só `Private`; exige `channel.manage` ou `workspace.admin`; alvo deve ser workspace member; 409 se já membro; 400 em Public/Direct/Group |
| `DELETE …/channels/{channelId}/members/{userId}` | Remove outro; `channel.manage` / admin; não remover o último gestor |
| `DELETE …/channels/{channelId}/members/me` | Sair; membro do privado; mesmas regras do último gestor |

Hub (opcional nesta fatia): `ChannelMemberChanged` `{ channelId, userId, action: added|removed|left }` para atualizar roster ao vivo. Se não houver hub, refetch no open do painel.

Atualizar `contratos.md` e permissões: mutações exigem `channel.manage` (já no
catálogo) além da membership do canal.

## UX

- Cabeçalho: “N membros” clicável.
- Painel: lista com avatar/nome; em privado, botão “Adicionar” (picker do
  diretório do workspace, só quem ainda não está) e “Remover” / “Sair”.
- Empty/erro claros; sem clonar Slack/Discord.
- Guest (quando B-040 existir) aparece no roster com selo “Convidado”.

## Multi-tenant e authZ

- Só membros do canal (ou workspace, em público) listam o roster.
- Add só aceita `userId` do mesmo tenant + mesmo workspace.
- Cross-tenant / canal de outro workspace → 403.
- Removido perde history, hub `JoinChannel` e listagem do privado imediatamente.
- Público não usa `channel_members` para ACL de leitura (regra atual); mutações
  de membership individual em público → 400.

## Aceite

- [ ] Abrir canal privado mostra contagem e lista com o criador
- [ ] Admin/gestor adiciona Alice; ela passa a ver o canal na sidebar e o histórico
- [ ] Remover Bob: ele perde acesso (API + hub) na hora
- [ ] Membro sai com “Sair”; último gestor não consegue sair sem outro gestor
- [ ] Canal público: lista workspace members; sem add/remove individual
- [ ] Autocomplete `?query=` continua limitado a 8
- [ ] Cross-tenant / user de outro workspace → 403

## Testes

- Integration: add/remove/leave; paginação do GET; 409 duplicado; 400 em público.
- Security: sem membership → 403; cross-tenant; removido não lê history nem
  `JoinChannel`; Member sem `channel.manage` não adiciona.
- E2E / unit web: abrir roster, adicionar, contagem atualiza.

## Riscos

- Confundir com B-040 (externo) ou B-101 (GroupDm) — glossário/specs cruzadas.
- Canal privado “órfão” sem gestor — regra do último gestor + teste.
- Roster grande — paginação obrigatória; sem carregar o workspace inteiro no
  autocomplete de add (buscar no diretório com query).
