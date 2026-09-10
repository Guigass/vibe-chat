# B-103 — Acessibilidade WCAG 2.2 AA

> Wave W10-9 · Trilha D/E · Deps: B-099 · Decisões: D-11 · Risco R1

## Problema

A base tinha rótulos ARIA e foco contido na paleta, mas faltavam skip link,
controle de foco em outros overlays e verificação automatizada. A timeline
inteira era uma região viva, e contrastes/rótulos de status tinham regressões.

## Plano de execução — 2026-09-10

- Work-Item: B-103 / W10-9; Risk: R1; trilhas D/E; dependência B-099 Done.
- Superfícies: shell, timeline/thread, overlays, tokens globais, catálogos i18n e E2E/CI.
- Entrega: teclado/foco, anúncios resumidos por conversa, contraste e gate axe nas quatro telas.
- Gates: build/typecheck, unitários web, i18n, Playwright com axe nos dois temas,
  regressões de foco/teclado e screenshots. Leitor de tela real: rodada exploratória final
  do perfil econômico, sem alegação de certificação.
- Parada: escopo entregue com evidência; em falha técnica registrar causa e trabalho restante.

## Escopo

- Foco preso em todo overlay (paleta, visualizador de imagem, diálogos, picker de emoji),
  com foco devolvido ao elemento de origem no fechamento — usando o `a11y` do CDK, que
  já é dependência.
- Skip link “Ir para a conversa” como primeiro elemento focável.
- Landmarks: `banner`, `navigation`, `main`, `complementary`, `contentinfo`.
- Ordem de foco lógica no shell inteiro; nenhum `tabindex` positivo.
- Contraste AA nos dois temas, incluindo estados desabilitado e placeholder.
- Indicador de foco visível em tudo, também em `forced-colors`.
- Alvo de toque mínimo de 24×24 CSS px (WCAG 2.2 · 2.5.8).
- Alternativa por clique para toda ação de arrastar (2.5.7) — vale para B-079.
- `prefers-reduced-motion` respeitado em toda animação.
- Anúncios de `aria-live` sem inundar: mensagem nova é anunciada de forma resumida.
- **Gate na CI**: axe-core nas telas principais; violação séria ou crítica reprova.

## Fora de escopo

- Certificação formal e auditoria externa; AAA; suporte a leitor de tela específico
  além do que o padrão garante.

## Contratos

Nenhum. É frontend e CI.

## UX

- Skip link aparece só no foco, no topo à esquerda.
- Foco visível com outline de 2 px em `--vc-brand`, com contraste próprio.
- Estado de erro nunca comunicado só por cor — sempre ícone e texto.
- Toda ação por hover tem equivalente por foco de teclado.

## Multi-tenant e authZ

Nada novo. Cuidado só para o anúncio de `aria-live` não expor conteúdo de canal que a
pessoa não abriu.

## Aceite

- [x] Navegar do login à mensagem enviada só por teclado
- [x] Skip link funciona e é o primeiro no `Tab`
- [x] Abrir a paleta prende o foco; `Esc` devolve à origem
- [x] Nenhuma violação séria/crítica do axe em login, shell, thread e admin
- [x] Contraste AA nos dois temas nas telas e estados verificados (relatório local)
- [x] `prefers-reduced-motion` desliga as animações
- [x] Controles verificados com alvos de toque ≥ 24 px
- [ ] Passagem com leitor de tela real — deferida à rodada exploratória final;
  região viva resumida e cancelamento por troca de conversa cobertos por unitários

## Testes

- CI: axe-core dentro do job de E2E, nas quatro telas; violação séria reprova o build.
- E2E: fluxo completo só com teclado.
- Unit (web): foco preso e devolução de foco por overlay.
- Manual: passagem com leitor de tela real na rodada exploratória final do perfil
  econômico (`docs/agents/autonomia.md`); testes automatizados não a substituem.

## Riscos

- Gate novo reprovando PRs antigos → entrar com o baseline atual e apertar depois; o
  baseline vai no PR.
- `aria-live` verboso atrapalhando mais que ajudando → anúncio resumido e testado com
  leitor de tela, não só com o axe.

## Evidência local — 2026-09-10

Implementação concluída no workspace; fechamento `Done` depende do PR/CI/merge.
Não houve alteração de contratos HTTP, dados ou authZ.

- Build de produção: `docker compose -f compose.yaml -f compose.dev.yaml --profile apps up -d --build --no-deps web` — passou.
- Container `vibechat-b103-check`, dependências Linux isoladas: typecheck
  (`npx tsc -p tsconfig.app.json --noEmit`) e `npm test -- --watch=false`
  em `apps/web` — **293 testes / 62 arquivos passaram**.
- `npm run check-i18n` — **648 IDs / 9 catálogos completos**.
- `npx playwright test accessibility.spec.ts command-palette.spec.ts` em `tests/e2e`
  com DevAuth — **4 testes passaram**. Axe WCAG A/AA até 2.2 em login, shell,
  thread, admin, emoji e encaminhamento, light/dark: sem violações sérias/críticas,
  sem exclusões e sem regras desativadas. Testes adicionais de alvos de botão,
  ordem de Tab, foco, forced-colors e movimento reduzido.
- Relatório: `tests/e2e/playwright-report/index.html`; capturas em
  `tests/e2e/test-results/accessibility-*/`. Revisão visual de shell/thread/admin
  concluída. CI publica relatórios mesmo quando verde e executa unitários web.
- Avisos existentes do build: fallback de locale `pt-BR` → `pt` e orçamento CSS
  de shell/message-bubble; nenhum erro de build.
- Stop reason: `GOAL_MET` para implementação e validação local; publicação,
  CI/merge e passagem com leitor de tela real não são alegados como concluídos.
