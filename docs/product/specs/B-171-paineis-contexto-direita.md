# B-171 — Refatoração dos painéis de contexto (sidebar direita)

> Wave W9-9 · Trilha D · Deps: B-022, B-092, B-093 · Decisões: D-11 · Risco R1

## Problema

A coluna direita do shell (`--vc-context-width: 300px`) hospeda thread, pins,
salvos e o painel de contexto. Depois de B-092/B-093 o trilho ficou apertado:
lista/preview truncam cedo, o encaixe visual com a timeline é irregular e o
`vc-thread-panel` não compartilha o mesmo wrapper/`shell__context` dos demais.

## Escopo

- Aumentar um pouco a largura do trilho direito via token `--vc-context-width`
  (alvo sugerido ~360–380px no desktop; manter teto relativo em viewport estreita).
- Unificar encaixe: thread, pins, salvos e contexto usam a mesma coluna do grid,
  altura 100%, overflow interno consistente e header alinhado.
- Ajustar padding/ritmo e bordas para o painel “sentar” na grade do shell sem
  parecer flutuante ou cortado.
- Narrow (≤960px): overlay continua; largura `min(86vw, var(--vc-context-width))`
  acompanha o token novo.

## Fora de escopo

- Novas capacidades (seguir thread B-102, busca filtrada, etc.).
- Redesign da sidebar esquerda de canais (B-184).
- Mudança de contratos API/hub.
- Clonar visual Slack/Discord/WhatsApp.

## Contratos

Nenhum. Só UI/tokens no web (`apps/web`).

## UX

- Painel um pouco mais largo no desktop; timeline ainda é o foco principal.
- Conteúdo (lista de pins/salvos, replies da thread) legível sem truncar demais.
- Abrir/fechar e exclusão mútua dos painéis (já existentes) preservados.
- Light/dark e densidade respeitam tokens `--vc-*`.

## Multi-tenant e authZ

Sem mudança. Painéis continuam a consumir dados já filtrados por membership.

## Aceite

- [ ] `--vc-context-width` maior que 300px no desktop (documentar valor final na PR)
- [ ] Thread, pins, salvos e contexto compartilham a mesma coluna/encaixe visual
- [ ] Viewport ≤960px: overlay direito não quebra; Esc/fechar intactos
- [ ] Sem regressão de scroll da timeline (B-072) nem do rail esquerdo (UX-003)

## Testes

- Visual/manual nos quatro painéis (desktop + narrow)
- Spec/unit existentes de shell responsivo continuam verdes; ajustar asserts de
  largura se houver hardcode de 300px
