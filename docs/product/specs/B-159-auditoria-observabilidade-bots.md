# B-159 — Auditoria, custos e observabilidade de bots

> Wave W18-5 · Trilha B/C/D/E/F/AI · Deps: B-132, B-139, B-155, B-157, B-158 · Decisões: D-22, D-23 · Risco R3
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Sem trilha própria, não é possível explicar qual versão de bot/modelo/skill
respondeu, quais fontes/tools foram usadas, quem aprovou uma ação ou por que um
guardrail negou. Logar prompts integrais resolveria investigação às custas de
privacidade.

## Escopo

- Modelo `BotRun`, `BotRunStep`, `ToolCall`, `Approval` e `PolicyDecision`.
- Snapshot de IDs/versões/hashes de bot, prompt, modelo, skills, fontes, tools e
  policy; sem chain-of-thought.
- Actor efetivo/delegador, tenant/workspace/conversation, correlation/causation.
- Status, stop reason, tokens, custo estimado, latência, retry, cache e fallback.
- Tool args/results redigidos por schema/policy; secret detector antes de audit.
- Audit de create/publish/disable/grant/revoke/run/tool/approval/report.
- Retention e acesso separados para metadata, transcript opcional e audit.
- Métricas/dashboards: success/error, p95, budget, citations, tool deny,
  approvals, retrieval denied, index lag, stale vectors e delete propagation.
- Integração opcional com SIEM B-132 usando eventos minimizados/versionados.

## Fora de escopo

- Persistir chain-of-thought.
- Prompt/output/tool result integral por default.
- Telemetria externa obrigatória ou phone-home.
- Usar audit como replay automático de ação externa.

## Contratos

Entidades em schemas AI/Audit com RLS e lifecycle explícito. Eventos
`ai.bot.run.started.v1`, `ai.bot.run.completed.v1`,
`ai.bot.tool.requested.v1`, `ai.bot.tool.completed.v1`,
`ai.bot.approval.decided.v1` e `ai.bot.policy.denied.v1`.
API admin de busca/detalhe/export minimizado por cursor.

Reason codes e stop reasons são catálogo versionado. Transcript, se habilitado,
fica em store separado com flag/policy/TTL e não entra em logs.

## UX

Painel por bot/versão com volume, custo, latência, erros, tools e safety events.
Investigação mostra timeline redigida e fontes/aprovações, com indicador de dado
omitido por policy. Usuário pode reportar uma resposta.

## Multi-tenant e authZ

`ai.audit.read` é separado de `ai.bot.manage`; Auditor vê metadata permitida, não
secrets/transcripts por implicação. SIEM/export nunca mistura tenant. Acesso a
uma fonte citada continua sujeito à ACL atual.

## Aceite

- [ ] Run pode ser explicado por IDs/versões, ator, sources/tools e decisions.
- [ ] Secret/corpo integral/chain-of-thought não aparece em log/audit default.
- [ ] Budget, token/custo estimado e latência são agregáveis sem PII.
- [ ] Aprovação e tool effect compartilham correlation/causation.
- [ ] Export/SIEM minimizado e cross-tenant falham fechados.
- [ ] Retention/purge de transcript não apaga audit mínimo obrigatório.

## Testes

Golden redaction, secret canaries, retention/purge/hold, event contracts,
correlation, SIEM minimization, query authZ/cross-tenant, dashboard metrics e
falha de audit em ação que exige fail-closed.

## Riscos

Audit virar cópia do prompt, SIEM exfiltrar conteúdo e custo ficar incorreto.
Mitigar com metadata/hashes, redaction allowlist, políticas separadas, marcação
de estimativa e testes canary.

