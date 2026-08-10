# B-163 — Refatoração do message bubble (ações discretas + context menu)

> Wave W9-0 · Trilha D · Deps: B-023, B-024, B-083 · Decisões: D-11, D-15 · Risco R1

## Problema

A bolha (`apps/web/src/app/shared/ui/message-bubble/message-bubble.ts`) expõe o tempo
todo uma barra densa: seis emojis rápidos, “mais…”, Responder, Abrir thread, Editar e
Apagar. Em canal movimentado a timeline fica visualmente barulhenta; ações secundárias
competem com o corpo da mensagem. Waves 8–9 ainda vão acrescentar citar, encaminhar,
fixar e salvar — sem um modelo de interação limpo, a chrome só cresce.

## Escopo

- Refatorar o layout da bolha para leitura primeiro: corpo, metadados e reações
  aplicadas; chrome de ação fora do fluxo visual padrão.
- Ações primárias (reagir, responder) só no **hover/focus** da bolha, em toolbar
  compacta (ícones), não como botões texto permanente.
- Demais opções (editar, apagar, abrir thread, e futuros: citar, encaminhar, fixar,
  salvar) em **context menu** (clique direito / long-press / botão “⋯”).
- Menu via Angular CDK + padrão spartan/ui (D-15 / B-104); teclado e leitor de tela.
- Manter contratos e eventos atuais; só muda a superfície de UI.

## Fora de escopo

- Novas capacidades de mensageria (B-084, B-085, B-092, B-093) — só o slot de
  entrada no menu quando esses itens existirem.
- Redesign do composer, avatar ou agrupamento da timeline (B-088).
- Clonar visual Slack/Discord/WhatsApp.

## Contratos

Nenhum novo endpoint, evento ou schema. AuthZ e payloads de edit/delete/react/
reply/thread permanecem iguais.

## UX

- Estado default: bolha sem barra de ações visível; chips de reação já aplicadas
  continuam abaixo do corpo.
- Hover (pointer fine) ou focus-within: toolbar flutuante discreta com reagir,
  responder e “mais”.
- Context menu e “mais” listam as mesmas opções; itens destrutivos (Apagar) no
  final, com estilo de perigo.
- Touch: long-press abre o menu; toolbar também acessível pelo botão “⋯” sempre
  alcançável no focus.
- `Esc` fecha menu/picker; foco retorna ao gatilho.
- Densidade alinhada ao design system (`--vc-*`); motion sutil (aparecer/desaparecer
  da toolbar), sem glow nem pills genéricos.

## Multi-tenant e authZ

Sem mudança. Itens do menu só aparecem se o cliente já teria mostrado o botão
(ex.: Editar/Apagar só em `message().mine` e políticas vigentes). Servidor continua
a fonte de verdade.

## Aceite

- [ ] Bolha sem hover/focus não mostra barra de reações rápidas nem Editar/Apagar
- [ ] Hover ou focus revela toolbar compacta com reagir + responder + “mais”
- [ ] Clique direito / long-press / “mais” abre o mesmo menu de opções
- [ ] Editar, apagar, reagir, responder e abrir thread continuam funcionando
- [ ] Menu e toolbar navegáveis só por teclado; `Esc` fecha
- [ ] Touch: long-press abre o menu sem selecionar texto por engano

## Testes

- Unit (web): visibilidade da toolbar por hover/focus; itens do menu filtrados por
  `mine` / flags de ação.
- Component/E2E: abrir menu, editar e apagar; reagir via toolbar.
- a11y: foco preso no menu aberto; anúncio do botão “mais”.

## Riscos

- Descoberta de ações em mobile → long-press + “⋯” sempre no focus; documentar no
  glossário/UX se necessário.
- Regressão em features que emitem outputs da bolha → manter a mesma API de
  `@Output` do componente; só muda o gatilho visual.
