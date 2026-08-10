# B-156 — Skills personalizadas e declarativas para bots

> Wave W18-2 · Trilha B/C/D/E · Deps: B-155 · Decisões: D-18, D-22 · Risco R2
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Um system prompt único não é reutilizável nem governável para comportamentos
especializados. A organização precisa criar skills próprias sem permitir código
arbitrário ou privilégio escondido em texto.

## Escopo

- `SkillDefinition` tenant-scoped com drafts e versões publicadas imutáveis.
- Campos: nome, descrição, instruções, schema de argumentos, exemplos/testes,
  owner, compatibilidade e classificação.
- Composição ordenada de skills na `BotVersion`.
- Referências somente a knowledge/tool grants já concedidos ao bot.
- Estados `Draft`, `Published`, `Deprecated`; diff, clone e rollback.
- Manifesto `vibechat.bot.skill.v1` exportável/importável sem secrets.
- Validação de tamanho, schema, links, referências e instruções incompatíveis
  com policies superiores.

## Fora de escopo

- JS/DLL/script, package manager, container ou eval.
- Credencial dentro do manifesto.
- Skill criar capability, tool grant ou conhecimento por texto.
- Registry público de skills; distribuição governada futura pode reutilizar
  B-137.

## Contratos

Entidades `ai.skill_definitions`, `ai.skill_versions`; endpoints
`/api/v1/admin/ai/skills*`; eventos `ai.skill.published.v1` e
`ai.skill.deprecated.v1`. `BotVersion` referencia IDs e versões exatas.
Import valida schema estrito e colisão de slug/version.

## UX

Editor com preview da ordem efetiva, referências usadas, testes de exemplo e
diff. Aviso explícito: instrução não concede acesso. Estados empty/loading/error,
acessibilidade e `pt-BR`/`en`.

## Multi-tenant e authZ

`ai.skill.manage` para editar/publicar; `ai.bot.manage` para atribuir. Skills não
cruzam tenant. Referência a grant/fonte fora do workspace falha fechada.

## Aceite

- [ ] Uma skill publicada pode ser reutilizada por vários bots.
- [ ] Alterar draft não muda bots já publicados.
- [ ] Manifesto com código/secret/capability não concedida é rejeitado.
- [ ] Skill não consegue remover guardrail nem elevar tools.
- [ ] Import/export preserva versão e não inclui secrets.
- [ ] Cross-tenant falha sem enumeração.

## Testes

Golden manifests, schema/import malicioso, versionamento/rollback, composição e
precedência de prompt, security cross-tenant e E2E criar/publicar/atribuir.

## Riscos

Prompt injection administrativo, capability escondida e packages não
confiáveis. Mitigar com DSL declarativa, precedência fixa, validação, diff,
publicação e zero execução de código.

