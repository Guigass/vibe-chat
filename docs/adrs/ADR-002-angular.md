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
