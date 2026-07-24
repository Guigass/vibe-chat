# Design System — VibeChat

## Direção visual

VibeChat **não** deve parecer Slack, Discord ou WhatsApp.

**Estética:** oceano/teal + charcoal — profissional, calmo, com presença. Tipografia expressiva. Superfícies com profundidade sutil (gradientes e textura leve), não flat monocromático.

### Brand test

Se remover a navegação e o primeiro viewport puder pertencer a outro produto genérico de chat, a marca está fraca. O nome **VibeChat** e a paleta teal/charcoal devem dominar o hero e o shell.

---

## Tipografia

| Papel | Família | Uso |
|-------|---------|-----|
| Display / Brand | **Sora** | Logo wordmark, títulos de seção, empty states |
| UI / Corpo | **IBM Plex Sans** | Mensagens, navegação, formulários, densidades |
| Mono (opcional) | **IBM Plex Mono** | Código, IDs técnicos em admin |

Evitar: Inter, Roboto, Arial, system-ui como face principal.

Import sugerido (Google Fonts ou self-host):

```css
/* Sora + IBM Plex Sans */
```

---

## Tokens CSS

### Temas

Suportar **light** e **dark** desde o início via `data-theme="light|dark"` no `html`.

```css
:root,
[data-theme="light"] {
  /* Brand */
  --vc-brand: #0d9488;          /* teal 600 */
  --vc-brand-hover: #0f766e;    /* teal 700 */
  --vc-brand-soft: #ccfbf1;     /* teal 100 */
  --vc-brand-ink: #134e4a;      /* teal 900 */

  /* Neutrals charcoal */
  --vc-ink: #1c1917;            /* stone/charcoal */
  --vc-ink-muted: #57534e;
  --vc-ink-subtle: #78716c;
  --vc-surface: #f8fafc;        /* cool light, não cream #F4F1EA */
  --vc-surface-elevated: #ffffff;
  --vc-border: #e7e5e4;

  /* Atmosphere */
  --vc-bg-atmosphere:
    radial-gradient(1200px 600px at 10% -10%, rgba(13, 148, 136, 0.18), transparent 60%),
    radial-gradient(900px 500px at 100% 0%, rgba(15, 23, 42, 0.06), transparent 50%),
    var(--vc-surface);

  /* Semantic */
  --vc-danger: #b91c1c;
  --vc-success: #047857;
  --vc-warning: #b45309;
  --vc-info: #0369a1;

  /* Chat */
  --vc-msg-mine: #ccfbf1;
  --vc-msg-theirs: #ffffff;
  --vc-composer-bg: #ffffff;

  /* Motion */
  --vc-ease-out: cubic-bezier(0.22, 1, 0.36, 1);
  --vc-dur-fast: 120ms;
  --vc-dur-med: 220ms;

  /* Radius — contido; evitar pills rounded-full em tudo */
  --vc-radius-sm: 6px;
  --vc-radius-md: 10px;
  --vc-radius-lg: 16px;

  /* Type */
  --vc-font-display: "Sora", sans-serif;
  --vc-font-body: "IBM Plex Sans", sans-serif;
  --vc-font-mono: "IBM Plex Mono", monospace;
}

[data-theme="dark"] {
  --vc-brand: #2dd4bf;          /* teal 400 */
  --vc-brand-hover: #5eead4;
  --vc-brand-soft: #134e4a;
  --vc-brand-ink: #99f6e4;

  --vc-ink: #f5f5f4;
  --vc-ink-muted: #a8a29e;
  --vc-ink-subtle: #78716c;
  --vc-surface: #0c0f12;        /* charcoal deep */
  --vc-surface-elevated: #161a1f;
  --vc-border: #292524;

  --vc-bg-atmosphere:
    radial-gradient(1000px 500px at 0% 0%, rgba(45, 212, 191, 0.12), transparent 55%),
    radial-gradient(800px 400px at 100% 0%, rgba(15, 23, 42, 0.9), transparent 50%),
    var(--vc-surface);

  --vc-msg-mine: #115e59;
  --vc-msg-theirs: #1c1917;
  --vc-composer-bg: #161a1f;
}
```

### Evitar (vieses comuns de UI gerada)

- Roxo / indigo como marca
- Fundo cream `#F4F1EA` + serif terracotta
- Layout “broadsheet” com filetes densos
- Glow neon, multi-shadow, emoji como ícones de produto
- Cards no hero; cards só quando forem container de interação

---

## Layout do shell (produto)

Primeira viewport do app autenticado = **uma composição**:

1. Brand wordmark (Sora)
2. Lista de spaces/channels (navegação)
3. Painel de conversa (headline = nome do channel)
4. Composer

Não transformar o shell em dashboard com stats, strips e callouts.

### Landing / marketing (se existir)

- Hero full-bleed com atmosfera ocean/charcoal
- Brand hero-level + uma headline + uma frase + CTA
- Sem overlays/badges flutuantes sobre mídia
- Motion: fade/slide do brand, parallax sutil do gradiente, hover do CTA (2–3 motions intencionais)

---

## Componentes (orientação)

| Elemento | Orientação |
|----------|------------|
| Botão primário | Fundo `--vc-brand`, texto claro/escuro conforme contraste |
| Botão ghost | Sem card; border `--vc-border` |
| Lista de channels | Flat; item ativo com barra teal à esquerda |
| Bolha de mensagem | Sem sombra pesada; `--vc-msg-mine/theirs`; radius `--vc-radius-md` |
| Composer | Superfície elevada, não card flutuante com glow |
| Modais | Só para fluxos (criar channel, convidar); CDK overlay |
| Painel de conversa | Coluna flex com `min-height: 0`; **scroll só na timeline**; composer fora do scroller (B-072) |

### PrimeNG (Wave 6 / B-073 — após emenda ADR-002)

Permitido para acelerar admin e formulários densos (DataTable, Dialog, Select, Toast). Obrigatório:

- Preset/tema amarrado aos tokens `--vc-*` (teal/charcoal, Sora / IBM Plex Sans)
- Sem skins roxas/default genéricas
- Chat shell continua composição própria; PrimeNG não vira “tema Slack”

---

## Acessibilidade

- Contraste WCAG AA em light e dark
- Focus ring visível (`outline` teal)
- Não depender só de cor para “não lido”
- `prefers-reduced-motion` reduz animações

## Ícones

Linha única (ex.: Lucide ou similar stroke), peso consistente. Sem mascotes genéricos de chat roxo.

## Entrega para o time frontend

1. Publicar tokens em `apps/web/src/styles/tokens.css`
2. Tema default: light; respeitar `prefers-color-scheme` + toggle
3. Fontes self-hosted preferível em ambientes air-gapped
4. Storybook opcional na fase 2; na fase 1, página `/dev/ui` interna basta
