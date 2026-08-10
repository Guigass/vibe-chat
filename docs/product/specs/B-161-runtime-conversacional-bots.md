# B-161 — Runtime conversacional e orquestração de bots

> Wave W18-7 · Trilha B/C/D/E/AI · Deps: B-156, B-157, B-158, B-159, B-160 · Decisões: D-16, D-22 · Risco R3
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

As definições, fontes e tools só geram valor quando um usuário pode conversar
com o bot com estado, cancelamento, citações e efeitos seguros, sem colocar IA no
hot path de `SendMessage`.

## Escopo

- Invocação por DM, menção explícita e ação “Perguntar ao bot”.
- Snapshot imutável de `BotVersion` e autoridade efetiva no início de cada run.
- Job assíncrono/idempotente no Worker; estados queued/running/waiting approval/
  completed/failed/cancelled/expired.
- Composição de prompt por precedência fixa; skills, retrieval e tools sob policy.
- Loop de tool limitado por turnos/depth/time/tokens/budget e cancelável.
- Streaming/progresso sem publicar mensagem parcial como verdade durável.
- Resposta final por Messaging com idempotência + `seq` + outbox, identidade Bot,
  citações e disclosure de tools.
- Timeout/fallback seguro, retry classificado e zero resposta duplicada.
- Causation/depth impede loops bot↔bot/automation.
- Rate-limit e concorrência por bot/workspace/user.

## Fora de escopo

- Responder automaticamente a toda mensagem por default.
- Run síncrono dentro do comando `SendMessage`.
- Background autonomy sem owner/trigger/capabilities.
- Persistir chain-of-thought.
- Executar tool high-impact sem B-160.

## Contratos

Endpoints `POST /api/v1/bots/{botId}/runs`, `GET .../runs/{id}`,
`POST .../cancel`; integração com message/mention pode criar o run após commit
por outbox. Hub events de progresso são efêmeros; conclusão produz mensagem
durável no pipeline canônico. Idempotency key deriva de invocation + bot version.

State machine usa entidades B-159. Erros públicos são estáveis e não revelam
server/tool/fonte fora do escopo.

## UX

Timeline mostra bot, status, cancelar, tool em uso/aguardando aprovação,
citações e falha recuperável. Usuário escolhe quanto contexto da conversa anexar.
Offline/reconnect consulta estado canônico; reduced motion e screen reader.

## Multi-tenant e authZ

Cada etapa revalida usuário, membership, bot, fonte e tool atuais. Revogação
durante o run cancela/nega a próxima etapa. Bot não lê o canal inteiro por estar
mencionado; recebe janela/contexto explicitamente autorizado.

## Aceite

- [ ] Usuário pergunta por DM/menção e recebe uma única resposta durável.
- [ ] Oráculo responde com citações apenas das fontes autorizadas.
- [ ] Assistente ERP usa somente tool concedida e respeita approval.
- [ ] Assistente geral funciona sem knowledge/MCP e não afirma acesso interno.
- [ ] Cancel/timeout/retry não duplica tool effect nem mensagem.
- [ ] Revogar grant/membership no meio do run impede continuidade.
- [ ] Qdrant/MCP/provider indisponível degrada sem quebrar chat.
- [ ] Bot↔bot loop e budget runaway são interrompidos.

## Testes

State-machine/property, idempotência/outbox/seq, worker crash/retry, fake
provider/Qdrant/MCP, approval pause/resume, revocation race, security
cross-tenant, load/concurrency e E2E com duas sessões + quatro perfis de bot.

## Riscos

Efeito duplicado, autoridade stale, loops, custo e UX enganosa. Mitigar com
snapshot + revalidação, step idempotency, limites, estados explícitos, audit e
mensagem final somente após conclusão.

