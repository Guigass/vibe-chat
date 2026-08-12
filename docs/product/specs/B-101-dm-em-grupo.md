# B-101 — DM em grupo

> Wave W10-7 · Trilha B/C/D · Deps: B-021 · Decisões: D-11 · Risco R3

## Problema

DM só existe 1:1 (`get-or-create` por par de usuários). Para falar com três pessoas
sobre algo pontual, é preciso criar um canal — o que polui o workspace com canais
efêmeros que ninguém arquiva.

## Escopo

- DM com 3 a 9 participantes (limite configurável).
- Criar a partir do seletor de pessoas ou adicionando alguém a uma DM 1:1
  (o que cria uma **nova** conversa; a original permanece).
- Adicionar participante: só vê as mensagens a partir da entrada.
- Sair da DM em grupo; quem sai perde acesso ao histórico dali em diante.
- Nome opcional; sem nome, mostra os nomes dos participantes.
- Presence e typing iguais aos do canal.

## Fora de escopo

- Promover DM em grupo para canal; papéis dentro da DM; convidar externo (é B-040).
- Roster/add-remove de **canal privado** do workspace → **B-186** (este item é
  só `GroupDm`).

## Contratos

Reusa `Channel` com `Type = GroupDm` — sem entidade nova, sem caminho paralelo de
messaging (mantém `seq`, outbox e history).

- `POST /api/v1/workspaces/{workspaceId}/group-dms` — `{ userIds[], name? }`;
  devolve a existente quando o conjunto de participantes é idêntico (idempotência
  por conjunto ordenado).
- `POST .../channels/{channelId}/participants` — adiciona (só participante atual)
- `DELETE .../channels/{channelId}/participants/me` — sai
- `PATCH .../channels/{channelId}` — renomeia (só participante)
- Mensagem de sistema no fluxo quando alguém entra ou sai.
- `Directory:GroupDm:MaxParticipants` (default `9`).

`contratos.md`: tipo de canal, endpoints, regra de idempotência por conjunto.

## UX

- Seletor de pessoas com chips; a partir de 2 selecionados vira DM em grupo.
- Cabeçalho mostra os nomes ou o nome dado, e a contagem de participantes.
- Sidebar agrupa DMs em grupo junto das DMs, com avatares empilhados.
- Adicionar alguém avisa que a pessoa **não** verá o histórico anterior.

## Multi-tenant e authZ

- Todos os participantes têm de ser do mesmo tenant e do mesmo workspace.
- Membership é a lista de participantes; não participante → 403 no history e no hub.
- Adicionar participante grava o `seq` de entrada; o history filtra por ele — quem
  entrou depois **não** lê o que veio antes. Este é o teste de segurança principal.
- Sair grava o `seq` de saída; o filtro passa a ser uma janela.

## Aceite

- [ ] Criar DM com 3 pessoas abre a conversa para as três
- [ ] Criar de novo com o mesmo conjunto devolve a mesma conversa
- [ ] Adicionar uma quarta pessoa: ela não vê o histórico anterior
- [ ] Sair encerra o acesso, inclusive no hub
- [ ] 10 participantes → 400
- [ ] Não participante → 403 no history e no `JoinChannel`
- [ ] Renomear reflete nas duas sessões

## Testes

- Integration: idempotência por conjunto; janela de visibilidade ao entrar e sair.
- Security: não participante no history e no hub → 403; participante de outro tenant
  na criação → 403; leitura de mensagem anterior à entrada → vazio.
- E2E: três sessões trocando mensagem.

## Riscos

- Vazamento de histórico ao adicionar alguém → filtro por `seq` de entrada com teste
  dedicado; sem isso o item não passa.
- Conjunto de participantes gerando conversas duplicadas → chave normalizada
  (lista ordenada) com índice único.
