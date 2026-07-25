# B-104 — Remover PrimeNG (UI própria + CDK)

> Wave W7-6 · Trilha D/G · Deps: W6-7, D-15 · Decisões: D-15 (c)

## Problema

`primeng@22` é **comercial** (PrimeUI). Sem chave de licença, o pacote injeta um
banner fixo (`z-index` máximo, shadow root fechado) no canto inferior direito que
**cobre Anexar e Enviar** no composer (UX-002). Isso quebra a fatia vertical e
viola “sem dependências proprietárias” (`AGENTS.md`). O owner fechou **D-15** com
a opção **(c) sair do PrimeNG** — não comprar chave, não esconder o banner por CSS.

Uso atual (único):

- `apps/web/package.json` → `"primeng": "^22.0.0"`
- `apps/web/src/app/app.config.ts` → `providePrimeNG` + `VibeChatPreset`
- `apps/web/src/app/core/theme/vibechat.preset.ts`
- `apps/web/src/styles/_primeng.scss` (+ `@use` em `styles.scss`)
- `apps/web/src/app/features/admin/admin.page.ts` → `TableModule`, `SelectModule`, `TagModule`

O shell de chat **já** é composição própria + CDK.

## Escopo

- Remover a dependência `primeng` do `package.json` / lockfile (`npm uninstall primeng`).
- Remover `providePrimeNG`, `vibechat.preset.ts`, bridge `_primeng.scss` e qualquer
  import `primeng/*`.
- Reescrever a página `/admin` com componentes próprios (ou shared UI existente) +
  CDK, mapeados aos tokens `--vc-*`:
  - tabela de membros / listas densas (substitui DataTable)
  - select de papel (substitui Select)
  - tags/badges de status (substitui Tag)
- Preservar comportamento e authZ atuais do admin (settings mascarados, auditoria,
  roles, export, etc.) — só troca de apresentação.
- Atualizar docs já apontando B-104 se ainda restar menção a “usar PrimeNG”
  (ADR-002 emenda B-104 já documentada; design-system / frontend.mdc / riscos).
- Marcar UX-002 como `Done` em `docs/product/ux-findings.md` quando o arquivo
  existir e o banner tiver sumido (Docs pode fechar no pós-merge se o Build não
  tocar o registry).

## Fora de escopo

- Comprar, gerar ou configurar chave Community/Commercial do PrimeUI.
- Esconder o banner por CSS/JS enquanto o pacote existir.
- Redesign amplo do admin ou do chat shell.
- Adotar outra lib de UI de terceiros (Angular Material, Ng-Zorro, etc.) — ficar
  em composição própria + CDK.
- Polish visual além do necessário para paridade funcional do `/admin`.

## Contratos

Sem mudança de API, eventos, schemas ou claims. Só frontend.

## UX

- `/admin` continua utilizável com teclado; foco visível; contraste AA nos tokens.
- Sem cards desnecessários; sem look genérico de kit de terceiros.
- Light/dark via `data-theme` (já existente) — sem `darkModeSelector` do PrimeNG.
- Composer: **Anexar** e **Enviar** clicáveis, sem overlay de licença.

## Multi-tenant e authZ

Nada muda. Admin segue exigindo as mesmas permissões (`workspace.admin`,
`admin.dashboard`, etc.).

## Aceite

- [ ] `primeng` ausente de `package.json` e do lockfile
- [ ] Nenhum import `primeng/*` / `providePrimeNG` / preset Aura no repo
- [ ] `/admin` (DevAuth alice admin): tabela de membros, troca de role e tags
      funcionam sem regressão óbvia
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

- Reimplementar DataTable “na medida” demais → manter tabela HTML semântica + CSS
  dos tokens; sem virtual scroll salvo se já existir necessidade medida.
- Deixar CSS órfão do PrimeNG → apagar `_primeng.scss` por completo.
- Agente tentar “só comentar providePrimeNG” e deixar o pacote → o banner pode
  continuar; **desinstalar** o pacote é obrigatório.

## Ordem sugerida para o agente Build

1. Trocar `admin.page.ts` para composição própria (parar de importar PrimeNG).
2. Remover provider/preset/styles.
3. `npm uninstall primeng` e commit do lockfile.
4. Subir web, confirmar ausência do banner e admin ok.
5. Rodar testes da trilha web.
