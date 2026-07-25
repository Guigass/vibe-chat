# VibeChat Web

Angular 22 SPA (standalone + Signals + CDK + PWA) for the VibeChat vertical slice.

## Stack

- Angular 22, Signals, standalone components
- SCSS design tokens (`teal/ocean + charcoal`)
- OIDC via `oidc-client-ts` (Keycloak)
- Realtime via `@microsoft/signalr`
- PWA service worker (production builds)

## Develop

```bash
export PATH=$HOME/.local/node/bin:$PATH
npm install
npm start
```

App: `http://localhost:4200`

## Environment

| Key | Default |
|-----|---------|
| API | `http://localhost:5080` |
| Hub | `http://localhost:5080/hubs/chat` |
| Keycloak | `http://localhost:8080/realms/vibechat` |

## Structure

```
public/                 static assets (copied to site root)
  favicon.ico
  icons/                PWA + favicon PNGs/WebPs
  assets/
    audios/             message notification sounds
    background/         light/dark shell & chat backgrounds
    images/logo/        brand logos
    icons/              full platform icon pack
src/app/
  core/       auth, api, tenant, stores, theme, hub
  shared/ui/  design system components
  features/   auth, chat, admin, ai
  layout/     authenticated shell
```

Brand asset inventory (paths/URLs for agents): `docs/architecture/design-system.md` § Assets de marca.

## Keyboard

- `Ctrl/Cmd+K` — focus search
- `Escape` — close context panel / blur
- `Enter` — send message (`Shift+Enter` newline)
