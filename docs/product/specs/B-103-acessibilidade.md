# B-103 — Acessibilidade WCAG 2.2 AA

> Wave W10-9 · Trilha D/E · Deps: B-099 · Decisões: D-11 · Risco R1

## Problema

A base é razoável — há `aria-label`, `aria-live` na timeline, `role="listbox"` na busca
e classes `sr-only`. Mas não há foco preso em painel, nem skip link, nem verificação
automatizada, e o `@angular/cdk` está no `package.json` sem nenhum import no `src`.
Sem gate na CI, a acessibilidade regride a cada PR.

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
- Foco visível com anel de 2 px em `--color-accent`, com contraste próprio.
- Estado de erro nunca comunicado só por cor — sempre ícone e texto.
- Toda ação por hover tem equivalente por foco de teclado.

## Multi-tenant e authZ

Nada novo. Cuidado só para o anúncio de `aria-live` não expor conteúdo de canal que a
pessoa não abriu.

## Aceite

- [ ] Navegar do login à mensagem enviada só por teclado
- [ ] Skip link funciona e é o primeiro no `Tab`
- [ ] Abrir a paleta prende o foco; `Esc` devolve à origem
- [ ] Nenhuma violação séria/crítica do axe em login, shell, thread e admin
- [ ] Contraste AA nos dois temas (relatório no PR)
- [ ] `prefers-reduced-motion` desliga as animações
- [ ] Alvos de toque ≥ 24 px
- [ ] Leitor de tela anuncia mensagem nova sem repetir a timeline inteira

## Testes

- CI: axe-core dentro do job de E2E, nas quatro telas; violação séria reprova o build.
- E2E: fluxo completo só com teclado.
- Unit (web): foco preso e devolução de foco por overlay.
- Manual: uma passada com leitor de tela registrada no PR (a automação de UX review
  cobre isso — `.cursor/automations/04-ux-review.prompt.md`).

## Riscos

- Gate novo reprovando PRs antigos → entrar com o baseline atual e apertar depois; o
  baseline vai no PR.
- `aria-live` verboso atrapalhando mais que ajudando → anúncio resumido e testado com
  leitor de tela, não só com o axe.
