# B-182 — Decompor componentes de chat (`composer`, `message-bubble`)

> Wave W19-5 · Trilha D · Deps: W19-3, B-173 (recomendado) · Risco R1

## Problema

`composer.ts` (~1,3k linhas) e `message-bubble.ts` (~1,2k linhas) concentram
layout, interações, anexos, áudio, formatação, menus de contexto e modos de
edição/resposta. Componentes grandes dificultam review e reuso.

## Escopo

- Extrair subcomponentes por tipo de conteúdo/ação (ex.: composer de anexos,
  toolbar de formatação, preview de reply, bubble de áudio/imagem/link).
- Extrair lógica não-visual para services/helpers quando apropriado.
- Preservar tokens do design system, a11y e comportamento UX existente.
- Meta: `composer.ts` e `message-bubble.ts` ≤ 400 linhas cada (shell + wiring).

## Fora de escopo

- Mudar UX de produto (novos fluxos, B-173 implementação funcional se ainda
  `Planned` — preferir decompor após B-173 `Done`).
- Refatorar stores (B-181) ou API layer (B-180).
- Alterar contratos de mensagem/anexo.

## Contratos

Sem mudança de API. UI continua consumindo os mesmos DTOs.

## UX

Comportamento visual e de interação idêntico: hover/focus, context menu,
composer modes (reply/edit), progresso de upload, waveform de áudio.

## Aceite

- [x] Subcomponentes extraídos; shells ≤ 400 linhas.
- [x] `npm test` (339) e `ng build` verdes. E2E fica no CI do PR (sem mudança de contrato).
- [ ] axe-core sem novas violações críticas nos componentes tocados. Roles e rótulos foram só relocados; axe não rodou neste PR.

## Testes

- Vitest dos componentes/helpers extraídos.
- E2E: envio texto, anexo, áudio, reply, reação, context menu.

## Riscos

- Regressão visual — validar com screenshots ou E2E visual se disponível.
- B-173 em flight — coordenar merge ou executar após editar-no-composer `Done`.

## Execução — 2026-10-05

- Work-Item: B-182; Wave: W19-5; Trilha: D; Risk: R1.
- Deps satisfeitas: W19-3 (`6b14e3d`) e B-173 `Done`. Base `69139a2` (B-181 já em `main`).
- `composer.ts` 351 linhas. `message-bubble.ts` 297 linhas.
- Composer: barra de contexto, anexos, enquete, aviso de slash, toolbar de formatação, agendamento e áudio.
- Bolha: ícones de status, encaminhamento, citação, preview de link, anexos e reações.
- Sem mudança de contrato de mensagem. API e `Infrastructure.cs` fora deste PR.
- Verificação em `node:22.22.3`: `tsc -p tsconfig.app.json --noEmit`, `check-i18n`, `npm test -- --watch=false` (**75** arquivos, **339** testes) e `ng build` verdes. Aviso de locale `pt-BR` e o budget de `shell.page.scss` já existiam.
