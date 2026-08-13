# B-184 — Refatoração da barra de navegação (sidebar esquerda)

> Wave W9-11 · Trilha D · Deps: B-020 · Soft-deps: B-171, UX-003 · Decisões: D-11 · Risco R1

## Problema

A rail esquerda agrupa spaces, canais, DMs e membros, mas a hierarquia visual é
plana, a busca local é fraca ou ausente e o modo “compacto” da densidade global
não cobre um trilho só de ícones no desktop. Com o crescimento de blocos
(spaces, DMs, membros, admin), a navegação fica menos intuitiva e compete com a
timeline.

## Escopo

- Reorganizar a rail: hierarquia clara entre workspace/header, filtro/busca
  local, blocos (spaces/canais, DMs, membros) e footer (admin/perfil).
- Separação visual dos blocos (rótulos, ritmo, divisores) sem clonar Slack/
  Discord/WhatsApp e sem cards desnecessários.
- **Modo compacto da nav** no desktop: trilho estreito (ícones + tooltips/
  acessível), distinto da densidade global `data-density` (B-049 / design
  system). Preferência persistida (local ou perfil — documentar na PR).
- **Pesquisa/filtro na rail**: filtrar canais, DMs e membros já carregados na
  sidebar (client-side). Não substitui busca de mensagens (B-098) nem a paleta
  (B-099).
- Polimento de estados: unread/menção/rascunho legíveis; empty do filtro;
  teclado (foco, Escape limpa filtro); preservar colapso/overlay ≤960px
  (UX-003).
- Tokens `--vc-*` e assets de marca existentes; motion sutil (abrir/fechar
  compacto, highlight do item ativo).

## Fora de escopo

- Redesign dos painéis de contexto à direita (B-171).
- Busca full-text / filtros de mensagens (B-098).
- Paleta de comandos global (B-099).
- Inbox unificada (B-117) ou novos tipos de conversa.
- Branding por tenant (B-140) ou personalização visual do usuário (B-185).
- Mudança de contratos de membership/spaces além do necessário para UI.

## Contratos

Nenhum obrigatório. Preferência de “nav compacta” pode ficar em `localStorage`
nesta fatia; se subir para `GET/PUT /me`, atualizar `contratos.md` na mesma PR.

Filtro da rail usa dados já autorizados no cliente (membership).

## UX

- Desktop: toggle compacto ↔ expandido; expandido continua sendo o default.
- Filtro no topo da lista **somente no modo aberto**; no compacto o filtro some
  (filtrar exige expandir a rail). Resultados agrupados por bloco; zero matches
  com empty state curto.
- Seletor de workspace: só no modo aberto e quando há **mais de um** workspace.
- Modo compacto: tooltips overlay (`vcTooltip` / BrnTooltip) no hover/focus dos
  ícones — não usar `title` nativo; manter `aria-label`. Filtro, seletor de
  workspace, pesquisa e “Novo channel” só no modo aberto.
- Bloco de DMs na rail usa o rótulo **Recentes**.
- Ícones de largura vs esconder: chevrons (`Encolher/Expandir menu`) para o
  compacto; painel com seta (`Esconder/Mostrar barra`) para ocultar a rail.
- Narrow (≤960px): overlay/backdrop/Escape intactos; compacto não quebra o
  colapso automático.
- Contraste dos rótulos de seção: melhorar o que UX-004 apontou, sem esperar
  B-103 se for só ajuste de token.
- Light/dark e densidade global continuam válidos.

## Multi-tenant e authZ

Sem mudança. A rail só lista canais/membros já filtrados por membership;
filtro local não amplia o conjunto.

## Aceite

- [ ] Blocos (spaces/canais, DMs, membros) visualmente distintos e escaneáveis
- [ ] Modo compacto da nav no desktop (ícones + a11y) com preferência persistida
- [ ] Filtro/pesquisa local cobre canais, DMs e membros da rail
- [ ] Viewport ≤960px: colapso/overlay/Escape sem regressão (UX-003)
- [ ] Sem regressão de badges unread/menção/rascunho nem do footer Admin
- [ ] Sem clonar visual Slack/Discord/WhatsApp; tokens `--vc-*`

## Testes

- Unit/spec de shell: toggle compacto, filtro (match/empty), narrow overlay
- Visual/manual light/dark + comfortable/compact (densidade) × nav expandida/
  compacta
- Regressão E2E/smoke de abrir canal via rail e fechar overlay no narrow

## Riscos

Filtro client-side insuficiente em workspaces enormes — aceitável nesta fatia;
escala server-side fica para follow-up se necessário. Compacto sem tooltip/nome
acessível quebra a11y — exigir `aria-label` + tooltip overlay nos ícones.
