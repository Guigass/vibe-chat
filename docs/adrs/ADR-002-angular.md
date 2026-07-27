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

## Emenda (Wave 7 / B-104 / D-16) — Accepted

**Kit UI OSS pós-PrimeNG = spartan/ui** (não NG-ZORRO). Comparativo em D-16.

Stack UI oficial:

1. **Angular 22** standalone + Signals (inalterado)
2. **Angular CDK** para overlays, a11y, drag-drop, lists (base do spartan brain)
3. **spartan/ui** — `@spartan-ng/brain` (MIT, headless) + estilos helm/próprios mapeados
   aos tokens `--vc-*` (`design-system.md`)
4. **Composição própria** no shell de chat (`shared/ui`) — spartan entra onde há
   primitiva madura (ex.: select no `/admin`); tabela densa = HTML semântico + tokens
   (spartan não oferece DataTable)
5. Sem SDK/biblioteca de UI **comercial**; sem chave de licença em `.env`
6. **NG-ZORRO rejeitado** — MIT e Angular 22 ok, mas visual Ant Design conflita com
   identidade VibeChat e com “sem look genérico de kit de terceiros”

### Consequências

- **+** Compliance OSS; composer utilizável sem banner
- **+** Primitivas acessíveis (brain) sem lock-in visual; tokens VibeChat mandam
- **+** Alinhado ao CDK já adotado
- **−** Spartan peer exige **Tailwind CSS v4** (e `tw-animate-css`) — custo de DX no B-104
- **−** Sem DataTable pronto; admin usa tabela HTML + CSS dos tokens
- B-073 permanece no histórico como Done da Wave 6; remoção + adoção spartan = **B-104**
