# ADR-002: Angular

## Status: Accepted

## Contexto

O cliente web precisa de SPA madura, tipagem forte, roteamento, formulários, acessibilidade e PWA para uso corporativo. O time/agentes precisam de um framework com padrões claros e ecossistema estável para apps grandes.

## Decisão

Usar **Angular 22** com:

- Componentes **standalone**
- **Signals** para estado local/UI
- **Angular CDK** para overlays, a11y, drag-drop quando necessário
- **PWA** (service worker) para installability e cache de shell
- Sem NgModules legados em código novo

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| React + Vite | Válido, mas Angular oferece DI, roteamento e padrões opinionated alinhados a app corporativo grande |
| Vue / Svelte | Ecossistema menor para o perfil enterprise alvo deste projeto |
| Blazor | Acoplaria frontend ao .NET demais; PWA/chat UX costuma ser mais ágil em Angular/TS |

## Consequências

- **+** Estrutura previsível; Signals reduzem boilerplate de estado
- **+** CDK acelera UI acessível (modais, lists)
- **−** Curva de aprendizado para quem só conhece React
- **−** Bundle precisa de atenção (lazy routes por workspace/área)

## Emenda (Wave 6 / B-073) — Superseded

Adotar **PrimeNG** como biblioteca de componentes (tabelas, dialogs, forms, menus) **desde que**:

1. Tema/CSS variables mapeiem para tokens VibeChat (`design-system.md`) — sem look genérico “Prime default”
2. CDK continue para a11y/overlays pontuais quando PrimeNG não couber
3. Bundle: import modular / lazy por área (admin vs chat)
4. Identidade visual própria preservada (não clonar Slack/Discord)

### Implementação de referência (histórica — remover em B-104)

- Preset: `apps/web/src/app/core/theme/vibechat.preset.ts` (Aura + teal/charcoal)
- Provider: `providePrimeNG` em `app.config.ts` com `darkModeSelector: '[data-theme="dark"]'`
- Superfície: página `/admin` (DataTable / Select / Tag)
- Chat shell permanece composição própria + CDK

## Emenda (Wave 7 / B-104 / D-15) — Accepted

**Sair do PrimeNG.** `primeng@22` faz parte do PrimeUI comercial (chave obrigatória;
sem chave, banner fixo cobre o composer — UX-002). Isso conflita com “sem dependências
proprietárias” (`AGENTS.md`) e com a licença Apache-2.0 do produto (D-01).

Stack UI oficial:

1. **Angular 22** standalone + Signals (inalterado)
2. **Angular CDK** para overlays, a11y, drag-drop, lists
3. **Composição própria** com tokens `--vc-*` (`design-system.md`) — inclusive `/admin`
4. Sem SDK/biblioteca de UI comercial; sem chave de licença em `.env`

### Consequências

- **+** Compliance OSS; composer utilizável sem banner
- **+** Uma linguagem visual só (tokens VibeChat) em chat e admin
- **−** Tabelas/selects densos no admin precisam de componentes próprios (B-104)
- B-073 permanece no histórico como Done da Wave 6; o trabalho de remoção é **B-104**
