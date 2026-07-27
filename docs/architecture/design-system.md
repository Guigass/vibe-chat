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

### UI kit (Wave 7 / B-104 + B-106 / D-15 + D-27 — emenda ADR-002)

**Não usar PrimeNG** nem qualquer lib de UI **comercial**. Kit OSS oficial:

- **spartan/ui** (`@spartan-ng/brain` + estilos Helm copiados/próprios) — MIT,
  headless, CDK
- Tokens `--vc-*` mandam o visual (teal/charcoal, Sora / IBM Plex Sans); sem skin
  shadcn/Ant Design default na UI final
- Angular CDK para overlays, a11y, drag-drop e lists
- Shell de chat: composição própria em `shared/ui`; spartan onde a primitiva couber
  (ex.: select no `/admin`)
- Tabelas: a primitiva visual do spartan ou HTML semântico pode fornecer a base,
  mas paginação, ordenação e filtros continuam lógica explícita do produto; não
  tratar a primitiva como um data grid completo
- **NG-ZORRO rejeitado** (D-27) — MIT ok, mas visual Ant Design conflita com esta
  identidade
- Histórico: B-073 introduziu PrimeNG no `/admin`; B-104 remove o pacote e troca
  Table/Select/Tag por spartan, HTML semântico e tokens; **B-106** entrega o
  console admin com nav lateral, toolbars, listagens densas e filtros

---

## Acessibilidade

- Contraste WCAG AA em light e dark
- Focus ring visível (`outline` teal)
- Não depender só de cor para “não lido”
- `prefers-reduced-motion` reduz animações

## Ícones

Linha única (ex.: Lucide ou similar stroke), peso consistente. Sem mascotes genéricos de chat roxo.

Ícones de **app/PWA/favicon** (marca) estão em `apps/web/public/` — ver seção [Assets de marca](#assets-de-marca).

---

## Assets de marca

Arquivos estáticos servidos pelo Angular a partir de `apps/web/public/` (glob `**/*` no `angular.json`). Preferir URLs absolutas a partir da raiz do site (`/assets/...`, `/icons/...`).

**Fonte da verdade para agentes:** reutilizar estes arquivos; não inventar logo, favicon ou fundo genérico.

### Layout

| Caminho | Conteúdo |
|---------|----------|
| `apps/web/public/favicon.ico` | Favicon raiz (ligado em `index.html`) |
| `apps/web/public/icons/` | Ícones PWA/manifest + favicons PNG/WebP usados pelo app |
| `apps/web/public/manifest.webmanifest` | Manifest PWA (referências em `icons/icon-*x*.png`) |
| `apps/web/public/assets/audios/` | Sons de notificação de mensagem |
| `apps/web/public/assets/background/` | Fundos light/dark (shell e área de chat) |
| `apps/web/public/assets/images/` | Logos e placeholder de avatar |
| `apps/web/public/assets/icons/` | Pack completo (android, ios, pwa, windows11, favicons, webp) — uso futuro / plataformas |

### URLs úteis

| Uso | URL |
|-----|-----|
| Logo padrão | `/assets/images/logo/logo.png` |
| Logo claro / escuro | `/assets/images/logo/logo-white.png`, `logo-black.png` |
| Logo vertical | `/assets/images/logo/logo-vertical.png`, `logo-white-vertical.png`, `logo-black-vertical.png` |
| Logo mobile | `/assets/images/logo/logo-mobile.png`, `logo-mobile-black.png`, `logo-moblie-white.png` *(typo no filename preservado)* |
| Avatar placeholder | `/assets/images/user-ph.png` |
| Fundo app light/dark | `/assets/background/light.webp`, `/assets/background/dark.webp` |
| Fundo chat light/dark | `/assets/background/light_chat.webp`, `/assets/background/dark_chat.webp` |
| Som mensagem | `/assets/audios/msg_n1.mp3` … `msg_n4.mp3` |
| Favicon / apple | `/favicon.ico`, `/icons/favicon-32x32.png`, `/icons/apple-touch-icon.png` |
| Ícones PWA | `/icons/icon-72x72.png` … `/icons/icon-512x512.png` (+ variantes `.webp`) |

### Regras de uso

- Wordmark/logo: preferir PNGs em `assets/images/logo/`; casar variante light/dark com `data-theme`.
- Fundo do shell/chat: usar `background/*` em vez de inventar gradiente genérico que fuja dos tokens (atmosfera CSS `--vc-bg-atmosphere` continua válida para superfícies sem imagem).
- Notificação sonora: só um dos `msg_n*.mp3`; respeitar mute do usuário / `prefers-reduced-motion` não cobre áudio — oferecer preferência quando implementar.
- Pack `assets/icons/{android,ios,windows11}/`: não é obrigatório no hot path web; existe para PWA/store/instalação.
- Domínios oficiais e registro legal de marca seguem D-02; estes arquivos são a identidade visual técnica do produto no repositório.

## Entrega para o time frontend

1. Publicar tokens em `apps/web/src/styles/tokens.css`
2. Tema default: light; respeitar `prefers-color-scheme` + toggle
3. Fontes self-hosted preferível em ambientes air-gapped
4. Storybook opcional na fase 2; na fase 1, página `/dev/ui` interna basta
5. Assets de marca: catalogados acima; reutilizar antes de adicionar novos binários
