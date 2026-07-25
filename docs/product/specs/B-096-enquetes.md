# B-096 — Enquetes

> Wave W10-2 · Trilha C/D · Deps: — · Decisões: D-11

## Problema

Decidir qualquer coisa em grupo vira uma sequência de 👍 numa mensagem, sem contagem
confiável nem registro de quem faltou responder.

## Escopo

- Criar enquete no canal: pergunta + 2 a 10 opções.
- Voto único ou múltiplo (escolha de quem cria) — o WhatsApp adicionou o voto único
  justamente porque só múltiplo não resolve decisão.
- Resultado ao vivo com contagem e percentual.
- Enquete anônima ou com votos visíveis (definido na criação, imutável depois).
- Encerrar enquete (autor ou `workspace.admin`); encerrada não aceita voto.
- Prazo opcional de encerramento automático.

## Fora de escopo

- Enquete em thread e em DM na primeira fatia; múltiplas perguntas; exportar resultado.

## Contratos

Enquete é uma mensagem com payload próprio — mantém `seq`, outbox e histórico
funcionando sem caminho paralelo.

`messaging.polls`:

| Coluna | Tipo |
|--------|------|
| `TenantId`, `MessageId`, `ChannelId` | uuid |
| `Question` | text |
| `AllowMultiple`, `Anonymous` | bool |
| `ClosesAt`, `ClosedAt` | timestamptz? |

`messaging.poll_options` (`Id`, `PollId`, `Text`, `Position`) e `messaging.poll_votes`
(`TenantId`, `PollId`, `OptionId`, `UserId`, `CreatedAt`), único por
(`PollId`, `OptionId`, `UserId`).

- `POST .../channels/{channelId}/polls` — cria (gera a mensagem)
- `POST .../polls/{pollId}/votes` — `{ optionIds }`; substitui o voto quando é único
- `DELETE .../polls/{pollId}/votes` — retira o voto
- `POST .../polls/{pollId}/close`
- Evento `PollChanged` via outbox com o resumo agregado.

`contratos.md`: tabelas, endpoints, evento.

## UX

- Criação por `/enquete` (B-087) ou botão no composer.
- Cartão na timeline: pergunta, opções com barra de progresso, total de votos, prazo.
- Votada: destaca a escolha; anônima mostra só números.
- Encerrada: cartão em estado fechado, com o vencedor destacado.
- Opções navegáveis por teclado; `role="group"` com nome acessível da pergunta.

## Multi-tenant e authZ

- Votar exige membership no canal; enquete de outro tenant → 403.
- Enquete anônima não expõe identidade em **nenhuma** resposta da API — o agregado é
  calculado no servidor. Isso é requisito, não detalhe de UI.
- Encerrar: só autor ou `workspace.admin`.
- Auditoria de conversa (B-067) mostra a enquete e o agregado; em anônima, sem nomes.

## Aceite

- [ ] Criar enquete com 3 opções aparece nas duas sessões
- [ ] Voto único troca a escolha em vez de somar
- [ ] Voto múltiplo acumula
- [ ] Anônima nunca devolve `userId` na API
- [ ] Encerrada rejeita voto com 409
- [ ] Prazo encerra sozinho pelo worker
- [ ] Votar em enquete de outro tenant → 403

## Testes

- Integration: voto único/múltiplo, unicidade, encerramento manual e por prazo.
- Security: cross-tenant → 403; resposta de enquete anônima sem identidade
  (assert explícito no payload).
- Unit (web): cálculo de percentual, estado encerrado, acessibilidade do grupo.

## Riscos

- Anonimato furado por endpoint secundário → teste que varre a resposta inteira.
- Voto em rajada → reusa o rate-limit existente por usuário.
