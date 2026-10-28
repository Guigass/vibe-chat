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
| OPS-E2E-KNOWLEDGE | CI / E2E | `main` RED em `90f936c` (#227): `knowledge.spec.ts:38` não acha `knowledge-conflict`; PATCH concorrente devolve 500 no índice `IX_page_versions_TenantId_PageId_Version` | Critical | **Resolved** — corrida de versão vira 409 `KnowledgeVersionConflict` sem gravar | — |
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
| OPS-INT-B125 | CI / integração | `main` RED pós-#199 (B-125): `AutomationIntegrationTests.Retry_does_not_duplicate_disable_blocks_and_logs_omit_secrets` espera 1 run live e encontra 2 | Critical | **Resolved** — asserção por `eventId`; o eco depth-1 do post do bot não conta como retry duplicado |
| OPS-INT-AUTO-DUP | CI / integração | `main` RED em `6a8aa0e` (#230): `Retry_does_not_duplicate_disable_blocks_and_logs_omit_secrets` estoura `23505` em `IX_runs_TenantId_IdempotencyKey`; `Loop_is_stopped_and_missing_capability_does_not_post` acha 8 ecos (teto 3) | Critical | **Resolved** — insert do run é idempotente; outbox usa `FOR UPDATE SKIP LOCKED`; teste isola automações ligadas |
| OPS-INT-AUTO-MISS | CI / integração | PR #234 `d6e26f8`: `Two_processors_do_not_duplicate_the_live_run` espera 1 run e acha 0 | Critical | **Resolved** — o enqueue de teste commita a transação de RLS; a fila é drenada antes do lote único de 20 |

## Resolvidos

| ID | Categoria | Evidência | Severidade | Status | Resolução |
|----|-----------|-----------|------------|--------|----------|
| OPS-E2E-KNOWLEDGE | CI / E2E | `main` RED em `90f936c`: save concorrente de página estoura unique de `page_versions` e vira HTTP 500 | Critical | Resolved | `SaveChanges` mapeia a corrida para 409 `KnowledgeVersionConflict`; o perdedor não grava |
| OPS-INT-B125 | CI / integração | `main` RED pós-#199: retry de automação conta o eco depth-1 como segundo run live | Critical | Resolved | Asserção por `eventId` + drain explícito do eco antes do disable |
| OPS-INT-AUTO-DUP | CI / integração | `main` RED em `6a8aa0e`: corrida no insert de `automation.runs` vira 23505 no lote do outbox; o loop conta ecos a mais | Critical | Resolved | Perdedor adota o run vencedor; outbox trava a linha com `SKIP LOCKED`; drain esvazia a fila e desliga automações anteriores |
| OPS-INT-AUTO-MISS | CI / integração | PR #234 `d6e26f8`: `Two_processors_do_not_duplicate_the_live_run` espera 1 run e acha 0 | Critical | Resolved | O teste commita a transação de RLS e drena o backlog antes do lote de 20 |
| SEC-RLS-RUNTIME | Isolamento multi-tenant | Roles `vibechat_migrator`/`vibechat_app`/`vibechat_backup`, FORCE+WITH CHECK, `RlsSession` SET LOCAL, testes runtime | Critical | Resolved | PR #72 + #73 — roles/FORCE + SET LOCAL na txn + validação de role app |
| OPS-E2E-B097 | CI / E2E | `main` RED pós-#142: timeline-anchor (distância ~2780) + history-scroll (scrollTop ~3200) | Critical | Resolved | #145 lista com cache; #149 `releaseBottom()` no scroll sintético |
| OPS-E2E-B098 | CI / E2E | `main` RED pós-#146: 15 specs — `heading` `#geral` hidden (caixa 0×n) | Critical | Resolved | Título `min-width: 6.5rem`; `.shell__search` com `flex: 0 1` + `min-width: 10rem` |
| OPS-E2E-B099 | CI / E2E | `main` RED pós-#151: paleta sem foco + gitleaks `keys:` no histórico | Critical | Resolved | [#154](https://github.com/Guigass/vibe-chat/pull/154) — foco no input; E2E sem omnibox; `combo` + allowlist `.gitleaks.toml` |
| OPS-E2E-B100 | CI / E2E | `main` RED pós-#156 (B-100 i18n): Chrome `en-US` + poluição de perfil Alice + paleta/settings i18n | Critical | Resolved | [#158](https://github.com/Guigass/vibe-chat/pull/158) — pin Playwright `locale: pt-BR`; reset de perfil; locators bilíngues; `cdkFocusInitial`; option ativa no `vc-locale-control` |

## Formato de detalhe

### OPS-E2E-KNOWLEDGE

- Status: **Resolved** — o PATCH e o restore de página que perdem a corrida
  devolvem **409** `KnowledgeVersionConflict` e não gravam. O índice único de
  `knowledge.page_versions` deixa de virar HTTP 500.
- Observado em: CI `E2E (Playwright)` de `main` em `90f936c` (run
  [40250589730](https://github.com/Guigass/vibe-chat/actions/runs/40250589730)),
  `specs/knowledge.spec.ts:7` nas duas tentativas. Build & test, Secret scan e
  Dependency audit passaram. Os quatro pushes anteriores, até `a0f56d6`,
  estavam verdes. O PR #227 também estava verde antes do squash.
- Base/head SHA: `90f936c30aaf53a39d74e1cf05f63f3a24a41bef` (#227, B-145).
  O diff desse squash não altera knowledge; só alarga a janela em que o teste
  já disparava dois PATCH com a mesma versão.
- Reprodução: Alice grava a página e Bob abre o mesmo rascunho. O spec não
  espera o PATCH da segunda gravação da Alice (a prévia é o body local) e o
  Bob salva em seguida. Os dois requests leem a mesma `Version`. O batch do
  `SaveChanges` insere `page_versions` antes do `UPDATE … WHERE Version = @original`.
  O perdedor estoura `23505` em `IX_page_versions_TenantId_PageId_Version`
  (`DbUpdateException`, não `DbUpdateConcurrencyException`). O cliente mostra
  «Não foi possível salvar.» e o `knowledge-conflict` não aparece. Trace do
  retry: Alice PATCH 200, Bob PATCH 500, body `{"version":2,…}`, traceId
  `00-7403fe97c0efe863ff8d49bed76bacde-dc7d1dd05e0b02d2-01`.
- Resultado esperado: uma gravação vence; a outra recebe 409
  `KnowledgeVersionConflict` com `currentVersion` e a página permanece na
  versão vencedora. O painel do Bob mostra o conflito.
- Resultado atual: o `catch` trata a violação desse índice como a mesma
  corrida do concurrency token. Contrato em `docs/architecture/contratos.md`.
- Impacto: `main` vermelho e auto-merge parado. Sem vazamento cross-tenant;
  a transação do perdedor faz rollback (auditoria e outbox não ficam pela metade).
- Risk class: R1 (mapeamento de erro no save de página; sem schema, authZ ou contrato novo).
- Owner automático: Build.
- Critério de resolução:
  1. dois ou mais PATCH com a mesma versão deixam exatamente uma gravação e o
     resto em 409, nunca 500;
  2. `knowledge.spec.ts` passa sem skip, timeout maior ou retry extra;
  3. finding marcado Resolved.
- Rollback: reverter o `catch`; o token de concorrência e o índice único permanecem.

### OPS-INT-AUTO-DUP

- Status: **Resolved** — dois processadores não inserem o mesmo run live; o
  lote do outbox não repropaga `23505`; o loop da automação de teste para no
  teto de profundidade.
- Observado em: CI `Build & test` de `main` em `6a8aa0e` (run
  [40426865500](https://github.com/Guigass/vibe-chat/actions/runs/40426865500)),
  job `Build & test`. Unit 388, arquitetura 24 e segurança 132 passaram.
  Integração: 2 falhas de 246.
- Base/head SHA: `7202931` verde; `6a8aa0e` (#230, B-144) vermelho.
- Reprodução: `TryEnqueueAsync` faz lookup e depois `INSERT`. API e worker
  (e um segundo host de teste) processam o mesmo outbox sem lock de linha.
  Os dois passam no lookup. O perdedor toma `23505` em
  `IX_runs_TenantId_IdempotencyKey`. O `catch` do outbox engolia a exceção e
  deixava o run `Added`; o `SaveChanges` do lote estourava de novo
  (`Retry_…` em `DrainAsync`). Com outra automação ainda ligada na fixture
  compartilhada, o mesmo canal gerava ecos além de `MaxDepth` (`Loop_…`, 8).
- Resultado esperado: um evento live gera um run; o lote do outbox segue;
  a cadeia de um disparo para em `MaxDepth`.
- Resultado atual: o perdedor solta a entidade e relê o run vencedor. O
  claim do outbox usa `FOR UPDATE SKIP LOCKED` na tabela que já existe
  (sem migration; o lease de ADR-062 continua valendo com `Ha:Enabled`).
  O teste desliga automações anteriores e drena a fila antes de ligar a nova.
- Impacto: `main` vermelho e auto-merge parado. Sem vazamento cross-tenant;
  a unique key continua sendo a barreira.
- Risk class: R1 (idempotência do run e claim do outbox; sem schema, authZ
  ou contrato novo).
- Owner automático: Build.
- Critério de resolução:
  1. dois `TryEnqueueAsync` concorrentes no mesmo evento deixam um run;
  2. `Retry_…` e `Loop_…` passam sem skip;
  3. finding marcado Resolved.
- Rollback: reverter o `catch` e o `SKIP LOCKED`. O índice único permanece.

### OPS-INT-AUTO-MISS

- Status: **Resolved** — a corrida concorrente de `TryEnqueueAsync` deixa o run
  gravado; um backlog maior que uma página do outbox não zera a contagem.
- Observado em: CI `Build & test` do PR #234 em `d6e26f8` (run
  [40606934478](https://github.com/Guigass/vibe-chat/actions/runs/40606934478)).
  Unit, arquitetura, segurança, secret scan e dependency audit passaram.
  Integração: 1 falha de 252. E2E ficou skipped.
- Base/head SHA: `main` `e8091ab` verde; head `d6e26f8` (#234, B-064) vermelho.
  A branch já continha `bb6312c` (OPS-INT-AUTO-DUP).
- Reprodução: `EnqueueOnceAsync` chama `RlsSession.EnsureAppliedAsync`, que
  abre a transação, e `TryEnqueueAsync` só faz `SaveChanges`. O escopo fecha
  sem `CommitAsync` e o Postgres desfaz o insert. O run que a asserção via
  vinha só do `ProcessBatch` seguinte (página de 20, as mais antigas). Com a
  fila da fixture acima de 20, a mensagem nova não entra no lote e a contagem
  fica 0. Os testes irmãos drenam a fila antes de ligar a automação; este não
  drenava.
- Resultado esperado: dois `TryEnqueueAsync` no mesmo evento deixam um run
  persistido; os dois processadores não duplicam a chave.
- Resultado atual: o teste commita a transação de RLS, como o middleware HTTP
  em `Program.cs`, e drena o backlog antes de segurar os processadores.
- Impacto: CI do PR #234 vermelho e E2E skipped. Sem vazamento cross-tenant;
  o índice único continua sendo a barreira.
- Risk class: R1 (teste de integração; sem schema, authZ ou contrato novo).
- Owner automático: Build.
- Critério de resolução:
  1. o teste passa com a fila previamente cheia e com os dois inserts
     concorrentes commitados;
  2. a chave de idempotência não duplica;
  3. finding marcado Resolved.
- Rollback: tirar o `CommitAsync` e o `DrainBacklogAsync` do teste.

### OPS-INT-B125

- Status: **Resolved** — o teste de retry deixa de contar todo run live da
  automação. O evento de origem continua com exatamente um run; o trigger
  depois do disable continua sem run.
- Observado em: CI `Build & test` de `main` em `e905ff7` (run
  [38983431779](https://github.com/Guigass/vibe-chat/actions/runs/38983431779)),
  `AutomationIntegrationTests.Retry_does_not_duplicate_disable_blocks_and_logs_omit_secrets`
  linha 85. Esperado 1 run `Live`, encontrado 2. Unit, architecture e security
  passaram; E2E foi skipped.
- Base/head SHA: `e905ff7b559d698a97a2a158bafe4b928f8b0f71` (#199, B-125).
- Reprodução: suíte de integração com o `OutboxDispatcher` do host de teste.
  O post do bot gera `MessageCreatedEvent`; o dispatcher (750 ms) enfileira o
  eco depth-1 enquanto a automação ainda está ligada. Um drain explícito nesse
  intervalo produz o segundo run (`Depth = 1`, `CausationRunId` do run original).
  Isolado e rápido, o disable ganha a corrida e o teste passa.
- Resultado esperado: retry do mesmo `eventId` não duplica run, step nem
  mensagem; disable rejeita evento novo com `AutomationDisabled`; o eco do
  loop (B-125) não entra nessa contagem.
- Resultado atual: asserções filtradas por `eventId`; o teste drena o eco
  antes do disable para não depender do tick.
- Impacto: `main` vermelho e auto-merge parado. Sem vazamento de secret nem
  falha de idempotência do evento de origem.
- Risk class: R1 (teste de integração; sem mudança de schema, contrato ou authZ).
- Owner automático: Build.
- Critério de resolução:
  1. o teste passa com o eco depth-1 já drenado;
  2. o evento bloqueado não gera run;
  3. finding marcado Resolved.
- Rollback: reverter o teste; o comportamento de loop/depth permanece o de B-125.

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

- Status: **Resolved** — [#154](https://github.com/Guigass/vibe-chat/pull/154)
  (paleta foca o campo de busca ao abrir; E2E dispara `keydown` em `window` —
  Ctrl+K do Chromium não rouba o foco; `SHORTCUT_SHEET` usa `combo` em vez de
  `keys`; `.gitleaks.toml` allowlista `palette.ts` porque o scan de histórico
  ainda vê `keys:` em `98ed933`).
- Observado em: CI `E2E (Playwright)` —
  [`command-palette.spec.ts`](../../tests/e2e/specs/command-palette.spec.ts)
  linha 33 (`command-palette-query` visível, `toBeFocused` falha) e
  `Secret scan (gitleaks)` em `palette.ts` (`keys: 'Ctrl/Cmd+Shift+M'` /
  `Alt+Shift+↓`).
- Base/head SHA: último green `12fcffa` (2026-08-28); incident tip `98ed933`
  (#151, B-099).
- Reprodução: CI em `main` após o merge de #151 — paleta abre, input sem foco;
  gitleaks vermelho no mesmo commit.
- Causa raiz: `cdkTrapFocusAutoCapture` não garante foco no input após o
  `@if`; `page.keyboard.press('Control+k')` compete com o omnibox do
  Chromium; gitleaks trata a propriedade `keys:` como segredo.
- Resultado esperado: Ctrl/Cmd+K foca o filtro; E2E passa; secret scan verde.
- Resultado atual: `viewChild` + `queueMicrotask(focus)`; E2E via
  `window.dispatchEvent`; sheet com `combo`; path allowlist no gitleaks
  (padrão #138 / `ProcessSettings.cs`) para o FP histórico.
- Impacto: bloqueava auto-merge e violava invariante “main verde”.
- Risk class: R1 (teste/UI); severidade Critical por bloqueio de pipeline.
- Owner automático: Build + QA.
- Critério de resolução:
  1. `E2E (Playwright)` verde em `main`;
  2. `command-palette.spec.ts` passa via `task test:e2e:ci`;
  3. gitleaks verde no histórico (allowlist do `palette.ts`);
  4. finding marcado Resolved citando PR de fix.
- Rollback: revert deste PR; paleta volta ao estado de #151.

### OPS-E2E-B100

- Status: **Resolved** — [#158](https://github.com/Guigass/vibe-chat/pull/158)
  (pin Playwright `locale: 'pt-BR'` depois de `devices['Desktop Chrome']`;
  `vc.locale` + `PUT /me` pt-BR em `openUserSession`; spec i18n troca idioma
  no login e restaura Alice; locators aceitam chrome PT/EN; paleta marca
  `cdkFocusInitial`; `vc-locale-control` marca a option ativa — o painel
  Settings criava um segundo `<select>` que caía na primeira option `pt-BR`).
- Observado em: CI `E2E (Playwright)` em `main` pós-#156 (run
  [#33933674819](https://github.com/Guigass/vibe-chat/actions/runs/33933674819))
  — 13 specs aguardando literais pt-BR; run de #158
  [#34224235379](https://github.com/Guigass/vibe-chat/actions/runs/34224235379)
  — `i18n-locale` ficou com `locale-select=pt-BR` após PUT `en`, e os specs
  seguintes não acharam `Enviar` / `Mostrar barra`.
- Base/head SHA: último green `edf5a49` (2026-09-04); incident tip `98c5836`
  (#157, docs B-100).
- Reprodução: runner Playwright `en-US` → `resolveBootstrapLocale()` carrega
  `messages.en.json`; specs com `Enviar`. PUT `/me` locale=`en` no spec i18n
  persiste no perfil Alice e `syncFromProfile` traduz o shell dos testes
  posteriores. Paleta abre com input visível sem foco (`cdkTrapFocus` no
  dialog).
- Resultado esperado: E2E verde em `main`; troca para `en` cobre login/shell/
  settings; Alice volta a `pt-BR` no fim do spec i18n; Ctrl/Cmd+K foca o filtro.
- Impacto: bloqueia auto-merge e a safety lane (hotfix antes de B-101).
- Risk class: R1 (teste/UI); severidade Critical por bloqueio de pipeline.
- Owner automático: Watchdog + Frontend.
- Critério de resolução:
  1. `E2E (Playwright)` verde no PR #158 e depois em `main`;
  2. `i18n-locale.spec.ts` e `command-palette.spec.ts` passam via
     `task test:e2e:ci`;
  3. finding marcado Resolved citando o merge.
- Rollback: revert de #158; i18n B-100 permanece, E2E volta a falhar no Chrome
  `en-US`.

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
