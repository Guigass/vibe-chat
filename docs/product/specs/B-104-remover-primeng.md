# B-104 — Remover PrimeNG (spartan/ui + CDK)

> Wave W7-6 · Trilha D/G · Deps: W6-7, D-15, D-27 · Decisões: D-15 (c), D-27 (spartan)

## Problema

`primeng@22` é **comercial** (PrimeUI). Sem chave de licença, o pacote injeta um
banner fixo (`z-index` máximo, shadow root fechado) no canto inferior direito que
**cobre Anexar e Enviar** no composer (UX-002). Isso quebra a fatia vertical e
viola “sem dependências proprietárias” (`AGENTS.md`). O owner fechou **D-15** com
a opção **(c) sair do PrimeNG** — não comprar chave, não esconder o banner por CSS.

**D-27** escolheu o substituto OSS: **spartan/ui** (não NG-ZORRO). Ver comparativo
em `docs/roadmap/decisoes-pendentes.md` § D-27 e emenda ADR-002.

Uso atual do PrimeNG (único):

- `apps/web/package.json` → `"primeng": "^22.0.0"` (+ `@primeuix/themes`)
- `apps/web/src/app/app.config.ts` → `providePrimeNG` + `VibeChatPreset`
- `apps/web/src/app/core/theme/vibechat.preset.ts`
- `apps/web/src/styles/_primeng.scss` (+ `@use` em `styles.scss`)
- `apps/web/src/app/features/admin/admin.page.ts` → `TableModule`, `SelectModule`, `TagModule`

O shell de chat **já** é composição própria (`shared/ui`) + CDK.

## Escopo

- Remover `primeng` e `@primeuix/themes` do `package.json` / lockfile.
- Remover `providePrimeNG`, `vibechat.preset.ts`, bridge `_primeng.scss` e qualquer
  import `primeng/*`.
- Adotar **spartan/ui** no web:
  - `@spartan-ng/brain` (MIT, headless; validar peers atuais no lockfile durante
    a implementação — a decisão foi conferida com Angular 22)
  - Tailwind CSS v4 e demais peers declarados pelo pacote
  - Estilos Helm (ou equivalentes) **copiados/mapeados** para tokens `--vc-*` —
    identidade VibeChat manda; sem skin shadcn default na UI final
- Reescrever `/admin` sem PrimeNG:
  - **Select** de papel → primitiva spartan (`BrnSelect` / Helm) + tokens
  - **Tag**/badge de status → `shared/ui` badge existente ou Helm tag mapeado a `--vc-*`
  - **Tabela** de membros → primitiva visual do spartan ou HTML semântico + CSS dos
    tokens; o kit não fornece um data grid rico equivalente ao PrimeNG DataTable,
    então paginação/filtros/ordenação permanecem lógica explícita do produto
- Preservar comportamento e authZ atuais do admin — só troca de apresentação.
- Atualizar docs se ainda restar menção a “usar PrimeNG” ou “só composição própria
  sem kit OSS” (ADR-002 / design-system / frontend.mdc / riscos).
- Marcar UX-002 como `Done` em `docs/product/ux-findings.md` quando o banner sumir.

## Fora de escopo

- Comprar, gerar ou configurar chave Community/Commercial do PrimeUI.
- Esconder o banner por CSS/JS enquanto o pacote existir.
- Adotar **NG-ZORRO** / Ant Design (rejeitado em D-27).
- Adotar Angular Material ou outra lib com visual genérico dominante.
- Redesign amplo do admin (menu lateral, toolbars, filtros, listagens polidas) —
  isso é **B-106** depois deste item.
- Redesign do chat shell.
- Substituir o shell de chat por componentes spartan — chat permanece `shared/ui`.
- Polish visual além do necessário para paridade funcional do `/admin`.

## Contratos

Sem mudança de API, eventos, schemas ou claims. Só frontend.

## UX

- `/admin` utilizável com teclado; foco visível; contraste AA nos tokens.
- Sem cards desnecessários; sem look Ant Design / shadcn default.
- Light/dark via `data-theme` (já existente).
- Composer: **Anexar** e **Enviar** clicáveis, sem overlay de licença.

## Multi-tenant e authZ

Nada muda. Admin segue exigindo as mesmas permissões (`workspace.admin`,
`admin.dashboard`, etc.).

## Aceite

- [ ] `primeng` e `@primeuix/themes` ausentes de `package.json` e do lockfile
- [ ] Nenhum import `primeng/*` / `providePrimeNG` / preset Aura no repo
- [ ] `@spartan-ng/brain` (+ peers atuais documentados) presente; select admin via spartan
- [ ] Tabela admin sem lib comercial; primitiva/HTML semântico + tokens `--vc-*`
- [ ] `/admin` (DevAuth alice admin): membros, troca de role e tags sem regressão óbvia
- [ ] Tela de chat: banner “Invalid PrimeUI License” **não** aparece; Anexar/Enviar
      acessíveis
- [ ] `ng build` (ou `npm run build` em `apps/web`) passa
- [ ] Testes web/E2E relevantes da trilha admin/chat passam
- [ ] UX-002 fechado ou anotado para Docs fechar

## Testes

- Unit/component: admin page renderiza listas/selects sem PrimeNG.
- E2E existente de admin (se houver) verde; smoke login DevAuth → canal → composer
  visível.
- Não é necessário Testcontainers (sem persistência nova).

## Riscos

- Introduzir Tailwind v4 “em tudo” → limitar ao necessário do spartan; tokens `--vc-*`
  continuam a fonte da verdade visual (design-system.md).
- Reimplementar um data grid “na medida” demais → primitiva/HTML semântico + CSS;
  sem virtual scroll salvo necessidade medida.
- Deixar CSS órfão do PrimeNG → apagar `_primeng.scss` por completo.
- Agente tentar “só comentar providePrimeNG” e deixar o pacote → o banner pode
  continuar; **desinstalar** o pacote é obrigatório.
- Agente adotar NG-ZORRO por comodidade de Table → rejeitar (D-27).

## Ordem sugerida para o agente Build

1. Adicionar Tailwind v4 + `@spartan-ng/brain` (e deps peers mínimas).
2. Trocar `admin.page.ts`: Select/Tag via spartan + badge; tabela semântica.
3. Remover provider/preset/styles do PrimeNG.
4. `npm uninstall primeng @primeuix/themes` e commit do lockfile.
5. Subir web, confirmar ausência do banner e admin ok.
6. Rodar testes da trilha web.

## Follow-up

Depois do merge: **B-106** — admin shell com nav lateral, toolbars, listagens e
filtros de respeito (`docs/product/specs/B-106-admin-shell.md`).
