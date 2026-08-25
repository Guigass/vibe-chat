# B-096 — Enquetes

> Wave W10-2 · Trilha C/D · Deps: B-087 (W8-9) · Decisões: D-11 · Risco R2

## Problema

Decidir qualquer coisa em grupo vira uma sequência de 👍 numa mensagem, sem contagem
confiável nem registro de quem faltou responder.

## Escopo

- Criar enquete em **canal com membership** (não thread, não DM nesta fatia): pergunta
  + **2 a 10** opções.
- Limites de texto (UTF-16, trim antes de validar):
  - pergunta: **1–500** code units;
  - texto de cada opção: **1–100** code units.
- Voto **único** ou **múltiplo** (escolha de quem cria, imutável depois) — o WhatsApp
  adicionou o voto único justamente porque só múltiplo não resolve decisão.
- Resultado ao vivo com contagem e percentual (percentual sobre total de votos emitidos,
  não sobre membros do canal).
- Enquete **anônima** ou com votos **visíveis** (definido na criação, imutável depois).
- Encerrar enquete (autor ou `workspace.admin`); encerrada não aceita voto.
- Prazo opcional (`ClosesAt`): encerramento automático pelo worker quando
  `ClosesAt <= now()` e a enquete ainda está aberta.
- **Empate** ao encerrar: se duas ou mais opções empatam na maior contagem, o cartão
  destaca **todas** as empatadas (sem declarar vencedor único); rótulo acessível
  “Empate”.
- Criação por `/enquete` (infra de slash B-087) ou botão no composer.
- Quem cria: membro do canal com `message.send` (mesma regra de enviar mensagem).

## Fora de escopo

- Enquete em thread e em DM na primeira fatia; múltiplas perguntas; exportar resultado.
- Editar pergunta/opções depois de publicada; reabrir enquete encerrada.
- Canais somente leitura / anúncio (B-112) — quando existirem, seguem regra própria de
  publicação; esta fatia assume canal normal com `message.send`.
- Permissão dedicada além de `message.send` (ex.: `poll.create` separado).

## Contratos

Enquete é uma mensagem com `MessageType = Poll` — mantém `seq`, outbox e histórico
funcionando sem caminho paralelo. A mensagem pode ter `Body` vazio; a pergunta vive
em `messaging.polls`.

`messaging.polls` (RLS por `TenantId`, FORCE):

| Coluna | Tipo |
|--------|------|
| `TenantId`, `MessageId`, `ChannelId`, `CreatedByUserId` | uuid |
| `Question` | text (1–500) |
| `AllowMultiple`, `Anonymous` | bool |
| `ClosesAt`, `ClosedAt` | timestamptz? |

`messaging.poll_options` (RLS por `TenantId`, FORCE):

| Coluna | Tipo |
|--------|------|
| `TenantId`, `Id`, `PollId` | uuid |
| `Text` | text (1–100) |
| `Position` | int (0…n−1, único por enquete) |

`messaging.poll_votes` (RLS por `TenantId`, FORCE):

| Coluna | Tipo |
|--------|------|
| `TenantId`, `PollId`, `OptionId`, `UserId` | uuid |
| `CreatedAt` | timestamptz |

Índices / invariantes:

- Único `(TenantId, PollId, OptionId, UserId)` — impede votar duas vezes na mesma opção.
- Modo **único**: no máximo **uma** linha por `(TenantId, PollId, UserId)`; `POST
  .../votes` substitui atomicamente (DELETE dos votos do usuário na enquete + INSERT).
- Modo **múltiplo**: uma linha por opção escolhida; `POST .../votes` define o conjunto
  completo (`optionIds` ⊆ opções da enquete, 1…N itens); `DELETE .../votes` remove
  todos os votos do usuário na enquete.
- `ClosesAt`, quando informado na criação, deve ser **estritamente futuro** (UTC).

Endpoints:

- `POST /api/v1/channels/{channelId}/polls` — cria (gera a mensagem + opções); exige
  `message.send` + membership.
- `POST /api/v1/polls/{pollId}/votes` — body `{ optionIds: uuid[] }`; regras acima;
  exige `message.send` + membership; enquete fechada → **409**.
- `DELETE /api/v1/polls/{pollId}/votes` — retira todos os votos do caller; enquete
  fechada → **409**.
- `POST /api/v1/polls/{pollId}/close` — autor ou `workspace.admin`; idempotente se já
  fechada.
- History/thread inclui payload `poll` agregado (contagens, percentuais, flags, prazo,
  `closedAt`, opções com `voteCount`; em anônima **sem** `voters`).

Evento `PollChanged` via outbox com resumo agregado (mesmo shape do history).

Worker (`apps/worker`):

- Job periódico (claim transacional, idempotente) seleciona enquetes com
  `ClosedAt IS NULL AND ClosesAt IS NOT NULL AND ClosesAt <= now()`.
- Para cada uma: seta `ClosedAt`, grava outbox `PollChanged` + audit `poll.close`
  (`actor=system`, metadata com `pollId`).
- Atraso aceitável: até **60 s** após `ClosesAt` (mesma ordem de magnitude do purge
  B-047); retry não duplica fechamento.

Audit (`audit_events`): `poll.create`, `poll.vote` (só metadata agregado — sem nomes
em anônima), `poll.close`, `poll.unvote`.

`contratos.md`: tabelas, índices, endpoints, evento, job e audit.

Slash (extensão de B-087):

- Registrar `/enquete` em `GET .../commands` para quem tem `message.send`.
- Parser abre o fluxo de criação no composer (sem modal de slash — fora de escopo de
  B-087); argumentos inválidos → erro inline, texto preservado.

## UX

- Criação por `/enquete` ou botão “Enquete” no composer; formulário inline: pergunta,
  opções (2–10), toggle único/múltiplo, toggle anônima, prazo opcional.
- Cartão na timeline: pergunta, opções com barra de progresso, total de votos, prazo
  restante (se houver).
- Votada: destaca a(s) escolha(s) do usuário; anônima mostra só números.
- Encerrada: cartão em estado fechado; destaca vencedor(es) ou “Empate”.
- Opções navegáveis por teclado; `role="group"` com nome acessível da pergunta.
- Quem não tem `message.send` (ex.: Auditor) vê resultados, mas composer de voto
  desabilitado com explicação.

## Multi-tenant e authZ

- Criar, votar e desvotar exigem **membership no canal** + `message.send`; enquete de
  outro tenant → **403**.
- **Auditor** (`admin.dashboard`, sem `message.send`): pode **ler** a enquete no canal,
  não vota nem cria.
- Enquete anônima não expõe identidade em **nenhuma** resposta da API ao membro comum
  — o agregado é calculado no servidor. Isso é requisito, não detalhe de UI.
- Encerrar: só autor (`CreatedByUserId`) ou `workspace.admin`.
- Auditoria de conversa (B-067, Done): mostra pergunta, opções e agregado; em anônima,
  **sem nomes** de votantes; em visível, lista quem votou em cada opção (somente na
  superfície admin B-067, não vazada para membros comuns).
- **Guest** (B-040, Planned): quando existir, guest com `message.send` no canal do
  convite pode criar e votar; até lá, regra documentada para implementação futura —
  authZ continua membership + `message.send`.
- Canais somente leitura (B-112): fora desta fatia; hoje não há modo announcement.

## Aceite

- [ ] Membro com `message.send` cria enquete com 3 opções; cartão aparece nas duas
  sessões sem F5
- [ ] Auditor vê a enquete, mas POST de voto retorna **403**
- [ ] Pergunta >500 ou opção >100 code units → **400** com mensagem clara
- [ ] Menos de 2 ou mais de 10 opções na criação → **400**
- [ ] Voto único: segundo POST com outra opção substitui a primeira (contagem da
  opção anterior cai para 0)
- [ ] Voto único: duas opções no mesmo POST → **400**
- [ ] Voto múltiplo: POST com duas opções acumula contagem nas duas
- [ ] Anônima: varredura do JSON de history/vote/close — nenhum `userId`/`voters`
- [ ] Visível: history inclui quem votou por opção para membros do canal
- [ ] Encerrada manualmente rejeita POST/DELETE de voto com **409**
- [ ] `ClosesAt` no passado na criação → **400**
- [ ] Com `ClosesAt` futuro, worker fecha sozinho em ≤60 s; `ClosedAt` preenchido;
  voto posterior → **409**
- [ ] Empate: duas opções com mesma contagem máxima → cartão destaca ambas com
  “Empate”, sem vencedor único
- [ ] Votar em enquete de outro tenant → **403**
- [ ] `/enquete` aparece em `GET .../commands` só para quem tem `message.send`

## Testes

- Integration: voto único (substituição), múltiplo (conjunto), unicidade de índice,
  encerramento manual, fechamento por `ClosesAt` (clock fake no worker), empate.
- Security: cross-tenant → 403; resposta de enquete anônima sem identidade (assert
  explícito em todo o payload); Auditor sem send → 403 em mutações.
- Unit (web): cálculo de percentual, estado encerrado, empate, acessibilidade do grupo.
- Arch: tabelas tenant-aware com `TenantId` + RLS listadas em `multi-tenant.md`.

## Riscos

- Anonimato furado por endpoint secundário → teste que varre a resposta inteira.
- Voto em rajada → reusa o rate-limit existente por usuário.
- Índice `(PollId, OptionId, UserId)` sozinho não garante voto único → invariante
  `(TenantId, PollId, UserId)` no modo único é requisito de implementação.
- Job de fechamento duplicado → claim transacional + `ClosedAt` idempotente.
