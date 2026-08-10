# B-160 — Guardrails e políticas para bots

> Wave W18-6 · Trilha B/C/D/E/AI · Deps: B-130, B-133, B-155, B-158, B-159 · Decisões: D-22, D-23 · Risco R3
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Prompt e permissão técnica não bastam para impedir vazamento, prompt injection,
runaway cost ou uma tool de ERP executar ação incompatível com a intenção do
usuário.

## Escopo

- `BotPolicy` versionada e simulável por workspace/bot/classificação.
- Gates input, context, pre-tool, post-tool, output e post-run.
- Allow/deny de provider/model, fonte, tool, destino e classe de dados.
- Limites de tokens, contexto, turnos, tools, duração, concorrência e budget.
- Detecção/isolamento de instruções em fonte, mensagem e tool result.
- Grounding/citações obrigatórios para respostas corporativas.
- DLP/classificação B-130 e policy packs B-133 integráveis, sem alegar
  certificação.
- Approval workflow para tools reversíveis/alto impacto/comunicação externa,
  com preview redigido dos argumentos e expiração.
- `Features:BotToolWrites:Enabled=false`; read-only é baseline.
- Kill switch por instância/tenant/workspace/bot/server/tool.
- Reason codes, denial UX, report e revisão de safety findings.

## Fora de escopo

- Prometer eliminar toda alucinação ou ataque.
- Classificador opcional autorizar quando indisponível.
- Aprovação permanente genérica para tool high-impact.
- Guardrail só no frontend ou apenas por texto de system prompt.

## Contratos

Entidades `ai.bot_policies`, `ai.bot_policy_versions`,
`ai.bot_approval_requests`. Porta central `IBotPolicyEvaluator` é chamada antes
de provider/retrieval/tool/output. Eventos/audit vêm de B-159. Policy decision
retorna `allow|deny|require_approval`, reason code, versão e limites efetivos.

Approval fixa target/arguments hash, tool schema/version, bot/run e expiração;
alteração de argumentos invalida a aprovação.

## UX

Admin usa matriz de policy, simulação e diff antes de publicar. Usuário vê
negativa compreensível, tool/efeito/target antes de aprovar e resultado depois.
Não expor regras internas que facilitem bypass nem esconder consequência.

## Multi-tenant e authZ

Policy e approval são tenant-scoped. Aprovador precisa capability específica e
não pode aprovar além do próprio escopo. Interseção de permissões é recalculada
no momento da execução; aprovação não sobrevive a revoke.

## Aceite

- [ ] Prompt injection em fonte/tool não altera a precedência do system prompt.
- [ ] Tool sem grant/policy ou escrita com flag off é negada.
- [ ] High-impact exige aprovação por execução e argumentos imutáveis.
- [ ] Budget/turn/depth interrompe loop e registra reason code.
- [ ] Falha do policy service/evaluator fecha tool e egress.
- [ ] ACL revogada entre approval e call impede a execução.
- [ ] Cross-tenant não cria nem consome approval.

## Testes

Adversarial prompt injection/jailbreak, tool output poisoning, DLP/classification,
budget/depth/property tests, approval TOCTOU/replay/expiry, policy rollback,
cross-tenant/authZ, E2E deny→approve→execute e security review R3.

## Riscos

Falsa sensação de segurança, bypass por composição e approval fatigue. Mitigar
com defesa em camadas, deny-by-default, reason codes, red team/evals, escopo
mínimo e escrita desligada.

