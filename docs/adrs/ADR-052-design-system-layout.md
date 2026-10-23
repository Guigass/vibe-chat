# ADR-052: Design system de layout no front

## Status: Accepted

## Contexto

A identidade visual (teal/charcoal, Sora, IBM Plex Sans) já estava em
`docs/architecture/design-system.md` e em `--vc-*`. As telas principais, porém,
misturavam margens soltas, tokens citados e não definidos (`--vc-text-sm`,
`--vc-shadow-md`, `--vc-border-subtle`) e frames que estouravam a viewport:
rodapé da sidebar, header do shell, admin e formulários de compliance/perfil.

Não havia uma primitiva única de stack/cluster/página nem um catálogo interno
dos componentes base. Storybook continua fora desta fase.

## Decisão

1. **Tokens estruturais** em `apps/web/src/styles/_tokens.scss`, sem trocar a
   paleta: escala de espaço 4px, tipo, raios já existentes, sombras, z-index,
   nomes de breakpoint e alvo de toque `--vc-touch` (40px). Cores seguem
   `data-theme`. Aliases `--vc-text`, `--vc-text-muted`, `--vc-border-subtle`,
   `--vc-surface-raised` e `--vc-warning-soft` passam a existir de fato.
2. **Primitivas** em `styles/_layout.scss`: `vc-stack`, `vc-cluster`, `vc-page`,
   `vc-split` (barra + conteúdo, uma coluna abaixo de 960px).
3. **Componentes base** em `shared/ui`, reusando botão, input, badge e empty
   state: select, card, modal, table, list, toolbar, tabs. Sem biblioteca nova.
4. **Catálogo** só de laboratório em `/dev/design-system`. Build de produção
   sem `enableDevAuth` redireciona para o login.
5. **Telas** passam a usar esses tokens. O scroll da página não é o scroll da
   conversa nem do admin no desktop. No mobile o alvo mínimo dos controles de
   chrome é 40px. A cor da marca não muda.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Storybook nesta rodada | A doc de fase 1 já aceita página interna; menos um app para servir |
| Tailwind como API de layout | O projeto já tem Tailwind para o preset spartan; uma segunda escala de espaço dividiria o time |
| Reescrever cada tela em cima dos componentes novos | Alto risco de regressão E2E; o ganho desta rodada é o frame, não um kit paralelo |

## Consequências

- **+** Espaçamento e empilhamento têm um lugar só
- **+** Catálogo em `/dev/design-system` para revisar o kit sem caçar tela
- **−** Parte do chrome antigo ainda tem CSS próprio, agora apontando para os tokens
- **−** Media queries repetem os px dos breakpoints; custom property não abre `@media`

## Rollback

Reverter o commit restaura o CSS anterior. Não há migration nem contrato de API.
