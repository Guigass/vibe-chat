# B-162 — Avaliação, publicação segura e templates de bots

> Wave W18-8 · Trilha B/C/D/E/G/AI · Deps: B-161 · Decisões: D-16, D-22 · Risco R2
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Publicar um bot apenas porque o prompt “parece bom” não prova grounding, recusas,
isolamento, custo nem segurança de tools. Também falta um ponto de partida para
os bots internos desejados.

## Escopo

- Evaluation suite versionada por bot: casos, expected properties, rubricas e
  datasets sintéticos por default.
- Gates de publicação: schema, grounding/citações, recusas, ACL, prompt
  injection, tool scopes, approvals, budget, timeout e regressão.
- Comparação candidate vs versão ativa, scorecards e falhas bloqueantes.
- Staged/canary por usuários/canais, monitoramento e rollback.
- Feedback/report de usuário ligado a bot/run/version sem conteúdo excessivo.
- Templates locais, sem secrets nem auto-enable:
  - **Oráculo da empresa** — knowledge obrigatório, resposta com fontes, sem tools;
  - **Assistente de ERP** — MCP allowlisted, read-only baseline;
  - **Treinador** — personalidade/skills e corpus curado opcional;
  - **Assistente geral** — sem knowledge/MCP por default.
- Checklist operacional e evidence bundle para publicação.

## Fora de escopo

- Benchmark público universal ou alegação de segurança perfeita.
- Usar dados reais sem ACL/retention.
- Template já conectado a provider, Qdrant, ERP ou credencial real.
- Autoaprovar tool high-impact porque passou em eval.

## Contratos

Entidades `ai.bot_eval_suites`, `ai.bot_eval_cases`, `ai.bot_eval_runs`,
`ai.bot_rollouts`; manifesto `vibechat.bot.template.v1`. Critérios bloqueantes
versionados e auditados. Template referencia capabilities desejadas, nunca IDs
reais ou secrets.

## UX

Scorecard mostra mudanças, regressões, casos bloqueantes, custo/latência e
cobertura de safety. Publicar exige revisão do diff e dos grants. Templates
explicam o que ainda precisa ser configurado.

## Multi-tenant e authZ

Suites, datasets e rollouts são tenant/workspace-scoped. Caso com dado real
herda ACL/classificação/lifecycle. Executor de eval usa as mesmas policies do
runtime e não ganha acesso de admin.

## Aceite

- [ ] Versão com falha cross-tenant/tool high-impact não pode ser publicada.
- [ ] Candidate é comparável à versão ativa com evidence bundle reproduzível.
- [ ] Canary pode ser interrompido e rollback preserva audit.
- [ ] Quatro templates criam drafts seguros, nunca bots ativos/credenciados.
- [ ] Dataset sintético é default; dado real segue lifecycle e ACL.
- [ ] Feedback aponta run/version sem expor prompt/secret em listagem.

## Testes

Golden evals, deterministic fake provider, regressão de citations/refusal,
prompt injection/tool approval, dataset authZ/purge, canary/rollback e E2E
template→configurar→avaliar→publicar.

## Riscos

Gaming de métricas, dataset contaminado e template transmitir falsa prontidão.
Mitigar com propriedades de segurança bloqueantes, corpus versionado,
evidência reproduzível, defaults sem credencial e canary.

