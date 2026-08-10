# B-155 — Catálogo e versionamento de bots com IA

> Wave W18-1 · Trilha B/C/D/E · Deps: B-109, B-110, B-121, D-22 · Decisões: D-16, D-18, D-22 · Risco R2
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

O VibeChat terá bot/token e IA pontual, mas não um recurso administrável que una
personalidade, modelo, escopos e versões. Sem uma definição publicada, mudanças
de prompt/modelo alterariam comportamento sem rastreabilidade.

## Escopo

- CRUD admin de `BotDefinition` e rascunhos por workspace.
- Identidade visível `Principal.Bot`, reutilizando B-109.
- System prompt, descrição, avatar, owner e canais/DMs de invocação.
- Model binding provider-neutral: provider, model id, parâmetros allowlisted,
  limite de tokens, timeout, fallback e classe de dados permitida.
- `BotVersion` imutável, publish/disable/rollback e optimistic concurrency.
- Referências versionadas para skills, knowledge grants, MCP grants e policy.
- Quotas/budget por bot e workspace; feature off por default.
- API/admin UI com lista, builder, diff, preview e histórico de publicação.

## Fora de escopo

- Implementar skills, ingestão, Qdrant ou MCP — B-156/B-157/B-158.
- Executar o loop conversacional completo — B-161.
- Chave de provider dentro da definição do bot.
- Bot herdar papel do criador ou receber `workspace.admin`.

## Contratos

Entidades esperadas: `ai.bot_definitions`, `ai.bot_versions`,
`ai.bot_channel_scopes` e `ai.model_bindings`, todas tenant/workspace-scoped e
com RLS. Endpoints `/api/v1/admin/ai/bots*`; mutações retryable usam
`Idempotency-Key`. Eventos versionados `ai.bot.published.v1`,
`ai.bot.disabled.v1` e `ai.bot.rolled_back.v1`.

Versão publicada fixa IDs/versões dos componentes. GET nunca devolve secret de
provider. Shapes finais entram em `architecture/contratos.md` no PR.

## UX

Admin cria rascunho em etapas: identidade → personalidade/modelo → capacidades
→ limites → revisão. Mostrar claramente `Draft`, `Published`, `Disabled`,
versão ativa, diff e impacto do rollback. Member só vê bots invocáveis.

## Multi-tenant e authZ

Somente `workspace.admin` com capability `ai.bot.manage` cria/publica. Bot e
model binding pertencem a um tenant/workspace; referências cross-tenant falham
sem revelar existência. O bot não herda authority do admin.

## Aceite

- [ ] Workspace cadastra vários bots com system prompts/modelos diferentes.
- [ ] Run futuro pode fixar uma versão imutável e reproduzível.
- [ ] Rollback reativa versão anterior sem reescrever histórico.
- [ ] Chave de provider não aparece em API, audit, diff ou log.
- [ ] Bot desativado não aceita nova invocação e preserva autoria passada.
- [ ] Cross-tenant e Member sem permissão falham fechados.

## Testes

Unit de state machine/versionamento; integration de CRUD/publish/rollback e
concorrência; security cross-tenant/authZ/secret; web E2E do builder e diff;
architecture test das fronteiras AI/Integrations.

## Riscos

Prompt/model drift e privilege inheritance. Mitigar com snapshots imutáveis,
diff/publicação, secret reference, escopos explícitos e identidade técnica
separada.

