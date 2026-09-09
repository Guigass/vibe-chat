---
name: frontend-feature-review
description: >-
  Reviews and restyles VibeChat Angular chat UI (polls, composer, bubbles,
  panels, timeline) against the design system, own-vs-theirs alignment, hub/API
  DTO mapping, and shared components. Use when reviewing a frontend feature,
  fixing layout or styling, a message/card on the wrong side, an empty card,
  invented CSS tokens, nested forms, or when the user asks to revisar o front
  or estilizar uma feature.
---

# Frontend feature review — VibeChat

Revisa **e corrige** uma superfície do `apps/web` (não é o loop observe-only de
`.cursor/automations/04-ux-review.prompt.md`). Uma feature por passagem.

## Antes

1. Ler `docs/architecture/design-system.md` (tokens, light/dark, densidade, marca).
2. Procurar o que já existe em `apps/web/src/app/shared/ui/` — reutilizar
   `vc-input`, `vc-button`, `vc-avatar`, `vc-poll-card`, etc. Não inventar kit.
3. Identificar se o dado vem de HTTP, SignalR, ou os dois (eco + histórico).

Detalhe de tokens, ownership e casing: [checklist.md](checklist.md).

## Fluxo

1. **Mapear a superfície** — template, CSS, store, mapper HTTP, mapper do hub.
2. **Autor vs lado** — alinhamento e ações “minhas” saem de `authorUserId` vs
   `profile().id` via `idsEqual` / `isOwnAuthor`. Nunca confiar só em `mine`.
3. **Estilo** — só tokens `--vc-*` documentados. Light e dark. Sem clone
   Slack/Discord/WhatsApp. Sem card extra no shell.
4. **Contrato** — DTO de hub/API em camelCase **e** PascalCase (records .NET).
   Mapper compartilhado; o hub não pode sobrescrever um HTTP bom com payload vazio.
5. **Markup** — sem `<form>` aninhado; bordas com `--vc-border` (não `--vc-line`).
6. **Teste** — regressão do mapper/store + verificar no browser (Alice e Bob).
7. **Runtime** — Docker / `task` / Compose. Sem `npm`/`ng` no host.

## Ownership (lado da bolha)

Sintoma clássico: o **nome** está certo (Bob) e o cartão vai para a **direita**.

Causas que já quebraram enquete e vão repetir em thread, forward, pin, system:

- `mine: existing.mine || incoming.mine` — flag preso depois de um ingest errado
- `mine: true` hardcoded no merge de `clientMessageId` (eco otimista aplicado a outro autor)
- `normalize` com `mine: message.mine ?? …` — não recalcula
- stack da timeline usa `item.mine` do primeiro item em vez de `authorUserId`

Regra:

- Calcular `mine` com `isOwnAuthor(authorUserId, me)` em HTTP, hub e `normalize`
- Na UI, alinhar com `authorUserId` (`own()` / `isOwnStack()`), não só `message.mine`
- Não casar correlators (`id` / `clientMessageId`) se os autores forem diferentes
- Conferir **ao vivo** (SignalR) **e** depois de refresh (histórico HTTP)

Dois usuários no mesmo canal: o que é meu à direita, o dos outros à esquerda
com avatar. O nome no header não basta.

## Estilo

- Temas: `data-theme` + `data-density`. Contraste AA. Focus ring teal.
- Bolha: `--vc-msg-mine` / `--vc-msg-theirs`, `--vc-radius-md`, sem sombra pesada.
- Composer / painéis: `--vc-surface-elevated`, `--vc-border`, `--vc-composer-bg`.
- Marca: assets em `apps/web/public/` (inventário no design system). Não inventar logo/fundo.
- Motion: 2–3, sutis; respeitar `prefers-reduced-motion`.
- A11y: label no grupo, teclado, alvo de toque.

Token inexistente (`--vc-line`, cores hex soltas, roxo/indigo de marca) = bug.

## Verificar no browser

Não encerrar com um print estático.

- Exercitar o fluxo (criar, receber do outro usuário, votar, encerrar, empty/error).
- Duas sessões DevAuth (Alice / Bob) quando a feature for realtime.
- Depois do rebuild: banner **Atualizar** ou refresh — o store em memória mente.
- Conferir a outra superfície que lê o mesmo estado (timeline, thread, busca).

## Pronto

- Código no padrão do repo (standalone, signals, inglês em ids/tipos)
- Teste da regressão (mapper, merge, alinhamento)
- Evidência: o que o browser mostrou (lados, tokens, light/dark se o CSS mudou)
