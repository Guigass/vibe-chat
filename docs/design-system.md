# Design system de layout — VibeChat

Guia de uso do frame. A identidade (cor, tipo, marca, densidade do chat) continua
em [`docs/architecture/design-system.md`](architecture/design-system.md). Este
arquivo é a regra de encaixe: espaço, empilhamento e componentes base.

Catálogo vivo (só laboratório): `/dev/design-system`. Produção sem DevAuth não
abre a rota.

## Tokens

Definidos em `apps/web/src/styles/_tokens.scss`. Não inventar hex nem px solto
no componente.

| Grupo | Tokens | Uso |
|-------|--------|-----|
| Espaço | `--vc-space-1` 4px … `--vc-space-8` 32px | Gap, padding, margin. Sem 0.15rem / 13px / 18px |
| Tipo | `--vc-text-xs` 12 … `--vc-text-display` 40 | Texto de UI. Famílias continuam Sora / IBM Plex |
| Raio | `--vc-radius-sm` 6, `md` 10, `lg` 16 | Controles e superfícies. Sem pill em tudo |
| Sombra | `--vc-shadow-sm`, `md`, `lg`, `soft` | Elevação curta. Sem glow |
| Z | `--vc-z-raised` … `--vc-z-skip` | Dropdown 20, overlay 30, modal 40, skip 60 |
| Breakpoint | 640, 768, 960, 1280, 1440 | 960 é o shell estreito; 1440 é o desktop de revisão |
| Toque | `--vc-touch` / `--vc-control-height` 40px | Altura de controle. Abaixo de 960px o alvo mínimo é 40px |
| Cor | `--vc-brand`, `--vc-ink`, `--vc-surface`, semânticas | Não mudar o valor para “ajustar” layout |

Aliases que o CSS antigo já citava e agora existem: `--vc-text`,
`--vc-text-muted`, `--vc-border-subtle`, `--vc-surface-raised`,
`--vc-surface-sunken`, `--vc-warning-soft`.

## Primitivas

Classes globais em `styles/_layout.scss` e hosts `vc-stack`, `vc-cluster`,
`vc-page`, `vc-split`.

| Primitiva | Quando |
|-----------|--------|
| Stack | Lista vertical. `data-gap` 1, 2, 3, 4, 6 ou 8 |
| Cluster | Ações na mesma linha, com quebra. Nunca `space-between` numa linha que não cabe |
| Page | Miolo com largura máxima e padding da escala |
| Split | Barra + conteúdo. Abaixo de 960px vira uma coluna |

O shell autenticado continua a composição da doc de marca: marca, canais,
conversa, composer. Scroll só na timeline (e no miolo do admin no desktop).

## Componentes base

Em `apps/web/src/app/shared/ui/`. Reutilizar. Não criar botão ou input local.

| Componente | Selector | Nota |
|------------|----------|------|
| Botão | `vc-button` | primary, ghost, subtle. Altura `--vc-control-height` |
| Input | `vc-input` | Label visível ou `ariaLabel` |
| Select | `vc-select` | Mesma métrica do input |
| Card | `vc-card` | Só quando agrupa uma ação. Não card no shell de chat |
| Modal | `vc-modal` | Foco preso, Escape e rótulo de fechar |
| Tabela | `vc-table` | Scroll horizontal fica no wrap, não na página |
| Lista | `vc-list` | Itens com altura de controle |
| Empty state | `vc-empty-state` | Título + descrição vindos do i18n |
| Toolbar | `vc-toolbar` | Começo em `[vcToolbarStart]`, ações no resto |
| Tabs | `vc-tabs` | Setas movem a aba selecionada |
| Badge | `vc-badge` | Tom, não cor solta. Texto sobre a marca usa `--vc-on-brand` |

## Faça

- Alinhe pelo grid de 4px e por `min-width: 0` em coluna flex/grid.
- Quebre clusters antes de estourar a viewport.
- Esconda o texto longo com ellipsis e mantenha o nome acessível.
- No trilho compacto, mostre uma marca de uma letra e preserve o rótulo no nome acessível.
- Confira light e dark: o token de cor já troca com `data-theme`.
- Texto visível sai do catálogo i18n.

## Evite

- Margem ou padding em px que não esteja na escala.
- `overflow: hidden` na página inteira para esconder um filho desalinhado. O clip
  do `body` é rede de segurança; o filho tem de caber.
- Scroll horizontal da página. Tabela densa rola dentro de `.vc-table-wrap` ou
  `.admin-table-wrap`.
- Alvo de toque menor que 40px abaixo de 960px.
- Card, sombra pesada ou pill em volta de cada linha do chat.
- Hex novo, roxo de marca, ou `--vc-line`.
