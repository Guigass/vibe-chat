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

## Emenda (Wave 6 / B-073) — Accepted

Adotar **PrimeNG** como biblioteca de componentes (tabelas, dialogs, forms, menus) **desde que**:

1. Tema/CSS variables mapeiem para tokens VibeChat (`design-system.md`) — sem look genérico “Prime default”
2. CDK continue para a11y/overlays pontuais quando PrimeNG não couber
3. Bundle: import modular / lazy por área (admin vs chat)
4. Identidade visual própria preservada (não clonar Slack/Discord)

### Implementação de referência

- Preset: `apps/web/src/app/core/theme/vibechat.preset.ts` (Aura + teal/charcoal)
- Provider: `providePrimeNG` em `app.config.ts` com `darkModeSelector: '[data-theme="dark"]'`
- Primeira superfície: página `/admin` (DataTable / Select / Tag)
- Chat shell permanece composição própria + CDK
