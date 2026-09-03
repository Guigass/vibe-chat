# Findings Operacionais — VibeChat

Fila canônica de problemas observados por Watchdog, QA, Security e UX quando não
são uma feature de produto. Build consulta esta fila antes do roadmap somente
para `Critical`, `High` de segurança/dados ou UX `Alta` no caminho principal.

## Regras

- evidência reproduzível obrigatória;
- um ID estável: `HOTFIX-*`, `SEC-*` ou `OPS-*`;
- não duplicar finding existente;
- `Open` exige ação no repositório;
- `External action` é R4 e não bloqueia trabalho independente;
- `Resolved` cita PR/check que resolveu;
- severidade não sobe por opinião; precisa de impacto demonstrável.

## Abertos

| ID | Categoria | Evidência | Severidade | Status | Próxima ação |
|----|-----------|-----------|------------|--------|--------------|
| OPS-QA-AUDIT | Permissão da automação | Token QA recebe 403 em comment/approve | High | External action | Conceder `pull-requests: write`; `actions:write` apenas se re-run for desejado |
| SEC-REVIEW-TEMPLATE | Cobertura de segurança | `privacy_guard` do template usa caminho interno inexistente e heurísticas de outro stack | High | External action | Substituir pelo prompt versionado `05-security-review.prompt.md` |
| OPS-REQUIRED-CHECK | Branch protection | O check de segurança ainda depende de disciplina do prompt | Critical | External action | Tornar `VibeChat Security Review` um required check |
| OPS-DOC-CHECKER | Integridade documental | Contrato e baseline DOC-006 existem, mas a CI ainda não executa checker offline | Medium | Open | Implementar as regras de `qualidade-documental.md` sem alterar prioridade das waves |
| OPS-PR-DRAFT | Tooling de PR | `open_git_pr` pode criar draft | Medium | Mitigated | Prompts convertem imediatamente para ready; monitorar |
| OPS-DOCS-RACE | Corrida Docs | Merges #72+#73 (~20s) abriram Docs #76+#77; #77 foi re-draftado e ficou CONFLICTING | High | External action | Colar prompts 03/06 atualizados no dashboard (repo já em #78) |
| OPS-E2E-NAV | CI / E2E | `main` vermelho desde #131 (B-184): `timeline-anchor.spec.ts` (botão Bob ausente) + `timeline-history-scroll.spec.ts` (scrollTop ~3200 vs &lt;120) | Critical | **Resolved** — specs alinhadas à nav B-184 (`Mensagem para …`) + dispatch de scroll para latch `nearBottom` |
| OPS-E2E-REALTIME | CI / E2E | `realtime-events.spec.ts` + `reply-citing.spec.ts` — helper E2E desatualizado após toolbar compacta (bdaf0d8); #123 corrigiu `reactionAriaLabel` | Critical | **Resolved** — #124 alinhou `clickMessageToolbarButton`; CI verde em `bdef969` |
| OPS-E2E-B097 | CI / E2E | `main` RED pós-#142 (B-097): `timeline-anchor.spec.ts:53` (scrollTop ~2780 vs &lt;5) + `timeline-history-scroll.spec.ts:41` (scrollTop ~3200 vs &lt;120) | Critical | **Resolved** — #145 cache/pin + #149 `releaseBottom()` cancela âncora bottom no scroll sintético |
| OPS-E2E-B098 | CI / E2E | `main` RED pós-#146 (B-098): 15 specs falham em `auth.ts:118` — timeout 20s aguardando `heading` `/geral/i`; h1 `#geral` existe mas está hidden (largura 0) | Critical | **Resolved** — título do canal com `min-width: 6.5rem`; busca encolhe (`flex: 0 1`) em vez de espremer o h1 |
| OPS-E2E-B099 | CI / E2E + gitleaks | `main` RED pós-#151 (B-099): `command-palette.spec.ts:33` (`command-palette-query` visível mas `inactive` vs focused) + gitleaks FP em `palette.ts:46,51` (`Ctrl/Cmd+Shift+M` / `Alt+Shift+↓` como `generic-api-key`) | Critical | Open — fix forward (autofocus E2E + allowlist gitleaks); revert de #151 descartado |

## Resolvidos

| ID | Categoria | Evidência | Severidade | Status | Resolução |
|----|-----------|-----------|------------|--------|----------|
| SEC-RLS-RUNTIME | Isolamento multi-tenant | Roles `vibechat_migrator`/`vibechat_app`/`vibechat_backup`, FORCE+WITH CHECK, `RlsSession` SET LOCAL, testes runtime | Critical | Resolved | PR #72 + #73 — roles/FORCE + SET LOCAL na txn + validação de role app |
| OPS-E2E-B097 | CI / E2E | `main` RED pós-#142: timeline-anchor (distância ~2780) + history-scroll (scrollTop ~3200) | Critical | Resolved | #145 lista com cache; #149 `releaseBottom()` no scroll sintético |
| OPS-E2E-B098 | CI / E2E | `main` RED pós-#146: 15 specs — `heading` `#geral` hidden (caixa 0×n) | Critical | Resolved | Título `min-width: 6.5rem`; `.shell__search` com `flex: 0 1` + `min-width: 10rem` |

## Formato de detalhe

### SEC-RLS-RUNTIME

- Status: **Resolved** — [#72](https://github.com/Guigass/vibe-chat/pull/72)
  (roles/FORCE/WITH CHECK) + [#73](https://github.com/Guigass/vibe-chat/pull/73)
  (`RlsSession` SET LOCAL + commit/rollback da txn + validação de role app).
- Observado em: `compose.yaml`, `.env.example`,
  `infra/compose/postgres/03-rls.sql` e `security/multi-tenant.md`.
- Base/head SHA: baseline documental de 2026-07-27; fechamento 2026-08-05.
- Reprodução (pré-fix): API e Worker recebiam `POSTGRES_USER`; o mesmo usuário
  inicializava o banco/tabelas. O catálogo executava `ENABLE ROW LEVEL SECURITY`,
  mas não `FORCE ROW LEVEL SECURITY`, e não criava role runtime separada.
- Resultado esperado: processos de aplicação usam role sem ownership,
  `SUPERUSER` ou `BYPASSRLS`; tabelas tenant-aware forçam RLS inclusive ao owner;
  GUCs `app.*` via `SET LOCAL` dentro da transação.
- Resultado atual: entregue — `vibechat_app` / `vibechat_migrator` /
  `vibechat_backup`; catálogo FORCE+WITH CHECK; `RlsSession` + testes runtime.
- Impacto (pré-fix): defesa em profundidade cross-tenant podia ser ignorada por
  configuração privilegiada mesmo com policies presentes.
- Risk class: R3.
- Owner automático: Security + Infra + Backend.
- Critério de resolução:
  1. ADR-009 e configuração declaram `migration_owner`, `app_runtime` e, se
     necessário, `backup_operator`;
  2. API/Worker conectam como runtime sem ownership/`BYPASSRLS`/superuser;
  3. tabelas tenant-aware usam `ENABLE` + `FORCE ROW LEVEL SECURITY`;
  4. policy possui `USING` e `WITH CHECK`;
  5. contexto usa `SET LOCAL` dentro da transação e ausência falha fechado;
  6. migrations continuam possíveis por fluxo separado;
  7. integration/security tests usam a connection string real de runtime e
     tentam SELECT/INSERT/UPDATE/DELETE cross-tenant;
  8. backup/restore e seed não entregam credencial privilegiada à aplicação;
  9. threat model, catálogo de configuração e runbook de rotação são atualizados.
- Rollback: reverter roles/policies apenas em ambiente de teste; com dados,
  preferir roll-forward de grants sem desabilitar RLS.

### OPS-E2E-NAV

- Status: **Resolved** — specs E2E alinhadas à nav B-184: seletor `Mensagem para Bob`
  (aria-label do membro) e `#geral`; `timeline-history-scroll` dispara evento `scroll`
  após posicionar o leitor no topo para sincronizar o latch `nearBottom`.
- Observado em: CI `E2E (Playwright)` —
  [`timeline-anchor.spec.ts`](../../tests/e2e/specs/timeline-anchor.spec.ts) linha 61
  (`getByRole('button', { name: /^Bob$/i })` não encontrado) e
  [`timeline-history-scroll.spec.ts`](../../tests/e2e/specs/timeline-history-scroll.spec.ts)
  linha 40 (`scrollTop` ~3200 vs esperado &lt;120; `timeline-jump` ausente).
- Base/head SHA: último green `e109fb19` (2026-08-13T12:08Z); incident tip
  `6cfff162093f52162bd1926c7a0ec0d82234f01d` (#131 B-184, run
  [#31700054154](https://github.com/Guigass/vibe-chat/actions/runs/31700054154));
  HEAD incidente `33f77f92eed4a2c96fb42d8f4dc6493c43641325` (run
  [#31797011938](https://github.com/Guigass/vibe-chat/actions/runs/31797011938),
  2026-08-14T11:38Z).
- Reprodução: CI em `main` — 13 passed, 2 failed; regressão contínua em ≥6 pushes
  consecutivos desde #131.
- Causa raiz: B-184 refatorou a sidebar esquerda (modo compacto, blocos,
  filtro); specs ainda assumiam botão DM “Bob” visível na rail. O scroll em
  histórico falhava porque o `TimelineStickyBottomPin` ainda estava ativo quando
  a lista crescia (ResizeObserver antes do microtask) — corrigido em `timeline.ts`.
- Resultado esperado: DM acessível na nova nav; leitor em histórico não é
  arrastado; E2E passa em ambas as specs.
- Resultado atual: specs corrigidas; validação via `task test:e2e:ci`.
- Impacto: bloqueava auto-merge e violava invariante “main verde”.
- Risk class: R1 (teste/UI); severidade Critical por bloqueio de pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` verde em `main` por ≥2 runs consecutivos;
  2. `timeline-anchor.spec.ts` e `timeline-history-scroll.spec.ts` passam via
     `task test:e2e:ci`;
  3. finding marcado Resolved citando PR de fix.

### OPS-E2E-REALTIME

- Status: **Resolved** — [#124](https://github.com/Guigass/vibe-chat/pull/124)
  alinhou `clickMessageToolbarButton` ao menu CDK; CI verde em `bdef969`.
- Observado em: CI `E2E (Playwright)` —
  [`realtime-events.spec.ts`](../../tests/e2e/specs/realtime-events.spec.ts) linha 61
  (`Reagir com 👍` no hover) e
  [`reply-citing.spec.ts`](../../tests/e2e/specs/reply-citing.spec.ts) linha 41
  (`Responder` no hover).
- Base/head SHA: último green `ecb147c9` (2026-08-10T17:07Z); incident tip
  `654f68dc4fc8b6714b751a58c37cc5e975e0396d` (run
  [#31488977720](https://github.com/Guigass/vibe-chat/actions/runs/31488977720));
  pós-`bdaf0d8` run
  [#31547493734](https://github.com/Guigass/vibe-chat/actions/runs/31547493734).
- Reprodução: CI em `main` — 12 passed, 2 failed; falha em
  `clickMessageToolbarButton` (`message-actions.ts:33`) ao não encontrar botões
  que agora vivem no menu “Ações da mensagem”.
- Causa raiz (parcial, #123): `aria-label` da chip usava tooltip-only após hover.
- Causa raiz (residual, bdaf0d8): ações primárias migraram para menu CDK; helper
  E2E não abria o menu antes de clicar.
- Resultado esperado: reação/resposta via menu compacto; E2E passa em ambas as specs.
- Resultado atual: helper alinhado ao fluxo de `timeline-toolbar-layout.spec.ts`;
  critérios 1–2 abaixo ainda pendentes em `main`.
- Impacto: `main` vermelho bloqueia auto-merge e viola invariante operacional.
- Risk class: R1 (teste/a11y); severidade Critical por bloqueio de pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` verde em `main` por ≥2 runs consecutivos;
  2. `realtime-events.spec.ts` passa localmente via `task test:e2e:ci`;
  3. finding marcado Resolved citando PR de fix.

### OPS-E2E-B097

- Status: **Resolved** — [#145](https://github.com/Guigass/vibe-chat/pull/145)
  (`loadChannel` não desmonta lista com cache; pin reobserva `.timeline__list`)
  + [#149](https://github.com/Guigass/vibe-chat/pull/149) (`releaseBottom()` no
  `onScroll` ao sair do fim cancela a âncora `bottom` de 1200ms no resize).
- Observado em: CI `E2E (Playwright)` run
  [#32965658072](https://github.com/Guigass/vibe-chat/actions/runs/32965658072)
  (2026-08-26T11:53Z) —
  [`timeline-anchor.spec.ts`](../../tests/e2e/specs/timeline-anchor.spec.ts) linha 79
  (scrollTop ~2780 vs esperado &lt;5; timeout 15s) e
  [`timeline-history-scroll.spec.ts`](../../tests/e2e/specs/timeline-history-scroll.spec.ts)
  linha 41 (scrollTop ~3200 vs esperado &lt;120; `timeline-jump` ausente).
- Base/head SHA: último green `b73e685` (2026-08-26T00:40Z, pós-#141); incident tip
  `944e22786a5bdee0c6b251334dd22ede5c07d282` (#142, 2026-08-26T11:53Z).
- Reprodução: CI em `main` — 13 passed, 2 failed; Build/gitleaks/dep-audit verdes;
  regressão imediata após squash de #142 (não flaky — falha estável em retry).
- Causa raiz: `loadChannel` sempre ligava `loading=true`. O template da timeline
  destruía `.timeline__list` no skeleton; o pin ficava no nó morto e
  `afterChannelOpen` âncorava o skeleton. #142 piorou o timing (prefs +
  `selectChannelGeral` refetch). Com cache, a lista permanece montada.
- Resultado esperado: timeline ancora no conteúdo mais recente; leitor em histórico
  não é arrastado; E2E passa em ambas as specs.
- Resultado atual: fix forward no store/pin (#145); residual em
  `timeline-history-scroll` — âncora `bottom` de 1200ms ainda reaplicava no
  resize quando o E2E só disparava `scroll` (sem `wheel`). `releaseBottom()` no
  `onScroll` ao sair do fim cancela só essa âncora; prepend/element seguem.
- Impacto: bloqueia auto-merge e viola invariante “main verde”.
- Risk class: R1 (teste/UI); severidade Critical por bloqueio de pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` verde em `main` por ≥2 runs consecutivos;
  2. `timeline-anchor.spec.ts` e `timeline-history-scroll.spec.ts` passam via
     `task test:e2e:ci`;
  3. finding marcado Resolved citando PR de fix.
- Rollback: revert de #142 restauraria CI mas descarta B-097 — não seguro; preferir
  fix forward.

### OPS-E2E-B098

- Status: **Resolved** — título do canal deixa de colapsar a largura 0 quando a
  busca B-098 (`22rem` / `42vw`) e os botões rotulados de IA ocupam o header.
- Observado em: CI `E2E (Playwright)` run
  [#33026732374](https://github.com/Guigass/vibe-chat/actions/runs/33026732374)
  (2026-08-27T00:24Z) — 15 failed, 0 passed (retry incluído). Screenshot: header
  sem `#geral`; busca + “Resumir mensagens recentes” / “Sugerir resposta” no lugar.
- Base/head SHA: último green `2eb1271` (2026-08-26T12:33Z, pós-#145); incident tip
  `55b6cb7dc39e73697fa5e8d913c6c33b077aa976` (#146, 2026-08-27T00:24Z).
- Reprodução: CI em `main` — todas as falhas em `tests/e2e/helpers/auth.ts:118`
  (`getByRole('heading', { name: /geral/i }).waitFor` timeout 20s; locator resolve
  para `<h1> #geral </h1>` hidden). Snapshot a11y inclui o heading sem `ref`.
  Specs afetadas: `persistent-draft`, `realtime-events`, `reply-citing`,
  `shell-responsive`, `shell-scroll`, `theme-tokens`, `timeline-anchor`,
  `timeline-history-scroll`, `timeline-toolbar-layout`, `two-sessions-chat`,
  `web-push-opt-in`.
- Causa raiz: B-098 alargou `.shell__search` de `min(18rem, 34vw)` para
  `min(22rem, 42vw)`. `.shell__title` tinha `min-width: 0` + `h1 { overflow: hidden }`.
  No viewport Desktop Chrome (1280) com sidebar 280px e botões de IA rotulados, o
  flex deixa o título em 0px — Playwright trata caixa 0×n como hidden.
- Resultado esperado: canal `#geral` visível no header após DevAuth; E2E passa.
- Resultado atual: título com `min-width: 6.5rem`; busca `flex: 0 1` e
  `min-width: 10rem` encolhe antes de espremer o h1.
- Impacto: bloqueava auto-merge e violava invariante “main verde”.
- Risk class: R1 (teste/UI); severidade Critical por bloqueio de pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` verde em `main` por ≥2 runs consecutivos;
  2. specs acima passam via `task test:e2e:ci`;
  3. finding marcado Resolved citando PR de fix.
- Rollback: revert de #146 restauraria CI mas descarta B-098 — não seguro sem QA;
  preferir fix forward (este PR).

### OPS-E2E-B099

- Status: **Open** — `main` RED imediato após squash de
  [#151](https://github.com/Guigass/vibe-chat/pull/151) (2026-09-03T10:02Z).
- Observado em: CI run
  [#33742187221](https://github.com/Guigass/vibe-chat/actions/runs/33742187221)
  — `E2E (Playwright)` 15 passed, 1 failed; `Secret scan (gitleaks)` 2 leaks;
  `Build & test` + `Dependency audit notes` success.
- Base/head SHA: último green `12fcffa` (2026-08-28T11:22Z); incident tip
  `98ed933ce32fab4b76adbf787dcc01772bca3a80` (#151 B-099, 2026-09-03T10:02Z).
- Reprodução: CI em `main` — `command-palette.spec.ts:33`
  `expect(getByTestId('command-palette-query')).toBeFocused()` timeout 15s
  (elemento presente, estado `inactive`; falha estável em retry). Gitleaks:
  `apps/web/src/app/shared/command-palette/palette.ts` linhas 46 e 51 —
  strings de atalho de teclado em `SHORTCUT_SHEET[].keys` (falso positivo
  `generic-api-key`).
- Resultado esperado: paleta abre com foco no campo de busca; gitleaks ignora
  atalhos documentados; CI verde em `main`.
- Resultado atual: `#151` mergeou com gitleaks + E2E vermelhos no PR (QA/Docs
  automations SUCCESS — `OPS-REQUIRED-CHECK` ainda External); B-099 já marcado
  **Done** no roadmap na mesma squash.
- Impacto: bloqueia auto-merge e viola invariante “main verde”.
- Risk class: R1 (teste/UI + allowlist); severidade Critical por bloqueio de
  pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` + `Secret scan (gitleaks)` verdes em `main` por ≥2 runs
     consecutivos;
  2. `command-palette.spec.ts` passa via `task test:e2e:ci`;
  3. finding marcado Resolved citando PR de fix.
- Rollback: revert de #151 restauraria CI mas descarta B-099 — não seguro; preferir
  fix forward.

### OPS-DOCS-RACE

- Status: **External action** — fix no repositório em
  [#78](https://github.com/Guigass/vibe-chat/pull/78); #77 fechado como
  supersedido. Resta R4: colar prompts 03/06 atualizados no dashboard.
- Observado em: PRs Docs #76 (merged) e #77 (draft → CONFLICTING → closed);
  merges #72+#73 do mesmo `SEC-RLS-RUNTIME` (~20s).
- Reprodução: dois feature PRs do mesmo ID mergeiam em sequência → dois triggers
  Docs → um survivor mergeia → o outro, se draft/órfão, fica `DIRTY`.
- Resultado esperado: um único Docs close ready; extras fechados com
  `superseded by #<n>`; nunca `convert_to_draft` para “pausar”.
- Resultado atual: prompts 03/06 + `operacao-24x7` no repo exigem dedup pré-PR,
  orphan sweep e proíbem re-draft (#78); Watchdog fecha draft/CONFLICTING
  supersedidos. Comportamento no dashboard só muda após colar os prompts.
- Impacto (pré-fix): PR draft bloqueia QA; órfão conflitado exige intervenção
  humana.
- Risk class: R1 (processo/automação).
- Owner automático: Docs + Watchdog.
- Critério de resolução:
  1. prompts 03/06 no dashboard exigem dedup pré-PR, orphan sweep e proíbem
     re-draft;
  2. Watchdog fecha Docs draft/CONFLICTING supersedidos;
  3. um ciclo Docs com merge paralelo do mesmo ID não deixa PR aberto órfão.

```markdown
### OPS-exemplo

- Observado em:
- Base/head SHA:
- Reprodução:
- Resultado esperado:
- Resultado atual:
- Impacto:
- Logs/artefatos:
- Risk class:
- Owner automático:
- Critério de resolução:
```
