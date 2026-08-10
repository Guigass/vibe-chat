# Achados de bugs funcionais — VibeChat

Registro de defeitos funcionais observados no caminho principal (login, envio,
leitura, anexos, admin, tempo real). Distinto de [`ux-findings.md`](ux-findings.md)
(polimento visual) e de
[`../roadmap/operational-findings.md`](../roadmap/operational-findings.md)
(SEC/OPS/HOTFIX de plataforma).

A automação de Build consome findings `Alta` abertos pela **safety lane** antes
de itens `Planned` de produto — ver `.cursor/automations/01-build.prompt.md` e
`docs/agents/operacao-24x7.md`.

Regras do registro:

- Um `BUG-<n>` por achado, numeração contínua, nunca reaproveitada.
- Só entra o que foi **observado** rodando a aplicação (ou com evidência
  reproduzível de código + runtime). Suspeita sem evidência não entra.
- Severidade: **Alta** (bloqueia ou quebra uma tarefa), **Média** (atrapalha),
  **Baixa** (polimento).
- Achado fechado vira `Done` com o PR, não é apagado.
- Se o sintoma já tem item `B-*` Planned com spec, o finding aponta esse ID;
  o Build escolhe o `B-*` quando elegível e fecha o `BUG-*` no mesmo PR.
- Work-Item de correção dedicada: `BUG-<n>` (mesmo estilo de `UX-*`).
- Risk class no detalhe; finding Alta sem classe fica inelegível até reconciliar.

## Abertos

| ID      | Área             | Achado                                                                | Severidade | Status                      |
| ------- | ---------------- | --------------------------------------------------------------------- | ---------- | --------------------------- |
| BUG-002 | Sidebar / unread | Badges de novas mensagens não limpam de forma persistente após reload | Média      | Aberto — alívio aplicado; fecha em **B-094** |
| BUG-008 | Presence         | Minimizar a janela marca ausente na hora                              | Média      | Aberto                      |
| BUG-010 | Timeline / scroll | Ao abrir a conversa não rola até as mensagens mais recentes          | Média      | Aberto                      |
| BUG-011 | Admin / shell    | Membros não veem o botão Admin                                        | Média      | Aberto                      |

## Detalhamento

### BUG-001 — Mensagens duplicadas no envio

- Status: **Done**
- Observado em: `http://localhost:4200/app` (Compose profile `apps`), 2026-08-10;
  relato de produto + inspeção de código.
- Hipótese: race entre mensagem otimista no cliente e fan-out SignalR
  `MessageCreated` sem `clientMessageId` no payload do hub — o dedupe por id de
  cliente falha e a timeline mostra duas bolhas; Enter rápido também abre janela
  de double-submit no composer.
- Arquivos: `apps/web/src/app/core/services/message.store.ts`,
  `apps/web/src/app/core/services/chat-hub.service.ts`,
  `apps/web/src/app/features/chat/composer/composer.ts`,
  outbox `MessageCreated` em `src/VibeChat.Infrastructure/`.
- Resultado esperado: um envio = uma bolha; idempotência de servidor preservada;
  hub e HTTP reconciliam pelo mesmo correlacionador.
- Risk class: R2.
- Owner automático: Frontend (D) + Messaging/Realtime (C).
- Critério de resolução: regressão (unit/E2E) cobrindo optimistic + hub overlap;
  sem duplicata visual nem segunda linha no banco para a mesma
  `Idempotency-Key`.
- Resolução: lock síncrono no composer/thread + clear draft early; dedupe
  case-insensitive por `id`/`clientMessageId` (`message-sync.upsertRemoteMessage`);
  outbox/hub ecoa `clientMessageId`; unit `message-sync.spec` + E2E count===1.

### BUG-002 — Unread / notificações após reload

- Status: **Aberto** — alívio safety lane aplicado; fecha em **B-094**
  ([spec](specs/B-094-recibos-de-leitura.md)).
- Severidade: **Média** (sintoma F5 do caminho principal mitigado; escopo
  completo de recibos/DM/mark-unread permanece em B-094).
- Observado em: sidebar com badges após F5 / novo load; probe Playwright
  2026-08-10 em `localhost:4200` (badges presentes pós-login).
- Hipótese: `ApiService.upsertReadCursor` existe e **não é chamado**;
  `selectChannel` zera badge só em memória; `refreshUnreads` reidrata contagens
  do servidor com `lastReadSeq` antigo.
- Arquivos: `apps/web/src/app/core/services/message.store.ts`,
  `apps/web/src/app/core/services/channel.store.ts`,
  `apps/web/src/app/core/api/api.service.ts`, endpoints `read-cursor` /
  `unread-count` na API.
- Resultado esperado: abrir/ler canal persiste cursor; F5 e multi-device
  refletem unread correto (contrato B-094).
- Risk class: R2.
- Owner automático: Messaging (C) + Frontend (D).
- Critério de resolução: B-094 Done + este finding `Done` no mesmo PR (ou PR
  seguinte de docs se o merge de B-094 já fechou o sintoma).
- Alívio (2026-08-10): `MessageStore.loadChannel` e `ingestRemote` (canal ativo)
  chamam `upsertReadCursor`; PUT monotônico com `Math.Max` na API; regressão
  `message.store.read-cursor.spec.ts`. Não substitui B-094 (debounce de scroll,
  DM “visto”, mark-unread, privacy, endpoint agregado).
- Próxima ação: quando W9-7 / B-094 for elegível, implementar a spec.

### BUG-003 — Upload de arquivo com erro

- Status: **Done**
- Observado em: relato de produto no lab Compose; stack MinIO healthy em
  2026-08-10; logs recentes da API sem exceção clara de attachment (ruído de
  outbox).
- Hipótese: fluxo presign → PUT XHR direto em `Minio:PublicEndpoint` (`:9000`);
  falha tipicamente por CORS/origin, URL pública inacessível do browser, ou
  headers assinados divergentes. Nginx do web não faz proxy do objeto.
- Arquivos: `apps/web/src/app/features/chat/composer/attachment-queue.service.ts`,
  `apps/web/src/app/core/api/api.service.ts`, `MinioObjectStorage` /
  `RewritePublicUrl`, CORS do bucket no Compose.
- Resultado esperado: anexar arquivo comum (imagem/PDF allowlist) completa
  upload e aparece na mensagem.
- Risk class: R2.
- Owner automático: Files (C) + Frontend (D) + Infra (A) se CORS/endpoint.
- Critério de resolução: upload E2E ou integração verde; erro de UI específico
  se falhar; doc/env coerente com endpoint público.
- Resolução: presign/download assinam com `MinioPresignClient` no
  `PublicEndpoint` (sem rewrite pós-assinatura); CORS do `createbucket`
  falha-fechado; `resolveContentType` no initiate da fila; TestHost
  Endpoint=`127.0.0.1` / PublicEndpoint=`localhost` + assert de host/PUT.

### BUG-004 — Áudio do microfone não envia

- Status: **Done** (reaberto e corrigido de novo em 2026-08-10)
- Observado em: relato de produto + repro ao vivo em `localhost:4200` (Compose
  `apps`), 2026-08-10 — grava, clica **Enviar áudio** (ou Parar) e cai em
  “Não foi possível finalizar a gravação.” / sem prévia.
- Hipótese (confirmada): o `effect` de troca de canal no `Composer` chama
  `audioRecorder.reset()`, e `reset()` lê o signal `previewUrl`. Quando
  `onstop` monta a prévia, o effect re-dispara e zera a gravação. O hand-off
  por polling de `phase()` em `waitForRecordingPreview` perde a corrida e
  reporta falha. Fixes parciais anteriores (MinIO, `activeChannelId`) não
  cobriam esse caminho.
- Arquivos: `apps/web/src/app/features/chat/composer/composer.ts`,
  `audio-recorder.service.ts`, specs `composer.spec.ts` /
  `audio-recorder.spec.ts`.
- Resultado esperado: gravar → Parar mostra prévia estável; gravar → Enviar
  áudio (sem Parar) finaliza, faz upload e cria mensagem `Audio`.
- Risk class: R2.
- Owner automático: Frontend (D) + Files (C).
- Critério de resolução: `stop()` resolve `Promise<RecordedAudio|null>` no
  `onstop`; effect de canal usa `untracked` nos side-effects; regressão
  Vitest do ciclo MediaRecorder + `onSubmit` com `phase===recording`.
- Resolução: `untracked` no effect de canal; `AudioRecorderService.stop()`
  retorna Promise resolvida no `onstop`; `onSubmit` faz `await stop()` em
  vez de polling; regressão Vitest (`composer.spec`, lifecycle
  MediaRecorder em `audio-recorder.spec`); validado ao vivo Parar→prévia→
  Enviar e Enviar-durante-gravação.

### BUG-005 — Página admin não entra

- Status: **Done**
- Observado em: Playwright 2026-08-10 — DevAuth **Alice** em `/admin` redireciona
  para `/app`; DevAuth **Demo** abre `/admin/overview` (“Visão geral”).
- Hipótese: authZ por design — seed Alice/Bob = `Member`, Demo =
  `WorkspaceOwner`; `adminRouteGuard` exige claim `admin.dashboard` e redireciona
  **sem feedback**. Usuário Member percebe como “admin não entra”.
- Arquivos: `apps/web/src/app/features/admin/admin.guard.ts`,
  `admin-permissions.ts`, `admin-context.service.ts`, seed de papéis,
  rotas `/admin` em `app.routes.ts`.
- Resultado esperado: Admin/Owner/Auditor acessam o shell; Member recebe
  feedback claro (ou deep-link explicado) em vez de redirect silencioso; docs de
  lab apontam Demo para admin.
- Risk class: R1 (feedback/DX) — não enfraquecer authZ.
- Owner automático: Frontend (D) + Directory/Admin (B).
- Critério de resolução: Member não “some” sem explicação; caminho Demo/Admin
  documentado e verificável; authZ de API intacta.
- Resolução: `adminGuard` libera o shell; `adminLandingGuard` manda
  Owner/Admin/Auditor à primeira área e Member permanece em `/admin`;
  `adminAreaGuard` evita loop (`/admin` em vez de `overview`); empty-state
  explica DevAuth Demo; regressão em `admin.guard.spec.ts`.

### BUG-006 — Conexão em tempo real caindo com frequência

- Status: **Done**
- Observado em: relato de produto; cliente tem `withAutomaticReconnect` +
  gap-fill (B-070). Console do lab também reporta CSP bloqueando inline
  handlers (possível correlato a UI/eventos, não necessariamente ao hub).
- Hipótese: drops por Redis/backplane, proxy sem Upgrade em algum caminho,
  timeout de idle, ou UX de reconnect/gap-fill percebida como “caindo toda
  hora”.
- Arquivos: `apps/web/src/app/core/services/chat-hub.service.ts`,
  hub `/hubs/chat`, Redis backplane, `infra/proxy/nginx.conf` (`/hubs/`),
  `docs/architecture/signalr-ha.md`.
- Resultado esperado: conexão estável no lab Compose; reconnect raro e
  transparente; mensagens não somem (gap-fill); indicador de estado se offline.
- Risk class: R2.
- Owner automático: Realtime (C) + Frontend (D) + Infra (A) se proxy.
- Critério de resolução: evidência de sessão estável (logs/trace) + regressão
  de reconnect; sem storm de disconnect no caminho oficial `task apps`.
- Resolução: timeouts SignalR explícitos na API (`KeepAlive` 15s /
  `ClientTimeout` 90s) alinhados no cliente; backoff com jitter;
  retry manual após falha de `start()` / `onclose` (antes ficava
  `disconnected` até F5); retry em `online` e ao voltar a aba visível;
  proxy `/hubs/` já tinha Upgrade + timeout 3600s; regressão
  `chat-hub-reconnect.spec.ts`.

### BUG-007 — Modo escuro / layout sem tokens

- Status: **Done** (reaberto e corrigido de novo em 2026-08-10)
- Observado em: relato de produto + repro ao vivo em `localhost:4200` (imagem
  `vibechat-web:local` pós-commits de tema), 2026-08-10 — UI “estranha”,
  superfícies sem fundo/borda; `getComputedStyle(html)` retorna `--vc-*`
  vazio em light e dark.
- Hipótese (confirmada): build production do Angular
  (`optimization.styles.inlineCritical`, default on) emite
  `<link rel="stylesheet" href="styles-*.css" media="print"
  onload="this.media='all'">`. O CSP em
  `infra/nginx/security-headers.conf` tem `script-src 'self'` (sem
  `'unsafe-inline'`), então o `onload` nunca roda e o CSS global fica preso
  em `media=print`. Forçar `link.media='all'` via DevTools restaura o layout.
  Ajustes de seletor/`ThemeService` sozinhos não bastam.
- Arquivos: `apps/web/angular.json`, `infra/nginx/security-headers.conf`,
  `apps/web/src/styles.scss`, `apps/web/src/styles/_tokens.scss`,
  E2E `tests/e2e/specs/theme-tokens.spec.ts`.
- Resultado esperado: após load, `--vc-ink` / `--vc-brand` / `--vc-surface`
  preenchidos; toggle dark/light troca a superfície de forma visível e
  persistente; stylesheet com `media=all` (ou equivalente efetivo).
- Risk class: R1.
- Owner automático: Frontend (D) + Infra (A) se CSP.
- Critério de resolução: `inlineCritical: false` na config production;
  rebuild da imagem web; E2E/smoke que falha se `--vc-ink` vier vazio.
- Resolução: `optimization.styles.inlineCritical: false` em
  `apps/web/angular.json` (production); CSP intacto; index deixa de emitir
  `media=print`/`onload`; E2E `theme-tokens.spec.ts`; validado ao vivo
  `--vc-ink` preenchido em light e dark.

### BUG-008 — Ausente imediato ao minimizar

- Status: **Aberto**
- Observado em: relato de produto, 2026-08-10 — ao minimizar a janela/aba o
  usuário passa a **ausente** imediatamente; esperado só após idle prolongado
  ou quando a sessão/máquina fica bloqueada (não por minimize/troca de foco
  breve).
- Hipótese: `ChatHubService` escuta `visibilitychange` e chama `SetAway` no
  mesmo instante em que `document.visibilityState === 'hidden'` (minimize,
  troca de aba, etc.), sem debounce/grace period nem distinção de lock de
  tela / inatividade real.
- Arquivos: `apps/web/src/app/core/services/chat-hub.service.ts`
  (`visibilityHandler` / `setAway`), hub `SetAway` /
  `Heartbeat`, presence Redis (B-026).
- Resultado esperado: minimize/troca de aba curta mantém **online**; **away**
  só após threshold de idle (ex.: minutos sem interação) e/ou sinal de
  bloqueio de tela / sessão; voltar a focar cancela o timer e restaura online
  via heartbeat.
- Risk class: R1.
- Owner automático: Frontend (D) + Realtime (C).
- Critério de resolução: minimizar não marca away na hora; after timer (ou
  lock) marca away; regressão cobrindo o grace period do visibility handler.
- Próxima ação: introduzir debounce/idle timer no handler de visibility;
  avaliar `navigator.userActivation` / eventos de input + APIs de lock quando
  disponíveis; alinhar copy/contrato de presence se o threshold for produto.

### BUG-009 — Reply de thread sem update em tempo real

- Status: **Done**
- Observado em: relato de produto + inspeção de código, 2026-08-10 — ao
  responder numa thread o peer não vê a reply nem o contador na timeline sem F5.
- Hipótese: `MessageStore.ingestRemote` só faz bump de `replyCount` quando o
  pai local já tem `threadId`; peers que não abriram a thread ficam com pai
  `threadId: null`. `ThreadStore.ingestRemote` usa `===` em vez de `idsEqual`
  e só injeta com o painel aberto; abrir thread não publica evento hub.
- Arquivos: `apps/web/src/app/core/services/message.store.ts`,
  `thread.store.ts`, `message-sync.ts`; outbox `MessageCreated` com
  `parentMessageId`.
- Resultado esperado: replyCount sobe no peer sem abrir a thread; com painel
  aberto a reply aparece via hub; reconnect faz gap-fill da thread aberta.
- Risk class: R2.
- Owner automático: Frontend (D) + Messaging/Realtime (C).
- Critério de resolução: unit do bump por `parentMessageId`/`replyToMessageId`;
  E2E ou integração cobrindo fan-out; finding `Done`.
- Resolução: `bumpChannelParentForThreadReply` + `parentMessageId` no hub;
  `idsEqual` no `ThreadStore`; gap-fill da thread aberta no reconnect; B-084
  no mesmo trabalho.

### BUG-010 — Abrir conversa sem scroll para o fim

- Status: **Aberto**
- Observado em: relato de produto, 2026-08-10 — ao abrir um canal/conversa a
  timeline **não** posiciona nas mensagens mais recentes; o usuário vê
  histórico antigo (topo) e precisa rolar manualmente até o fim.
- Hipótese: `Timeline.afterChannelOpen` chama `scrollToBottom` (ou
  `scrollIntoView` no divisor de não lidas) em `queueMicrotask`, antes do
  layout/altura final da lista (imagens, agrupamento B-088, sticky de data) —
  `scrollHeight` ainda está baixo e o `scrollTop` fica no topo; o rAF único
  em `scrollToBottom` não cobre carga assíncrona posterior.
- Arquivos: `apps/web/src/app/features/chat/timeline/timeline.ts`
  (`afterChannelOpen`, `scrollToBottom`), possivelmente
  `timeline-items.ts` / shell flex que define a altura do scroller.
- Resultado esperado: abrir canal (sem unread divisor, ou após dismiss) deixa
  a viewport no fim (mensagens mais recentes + composer); com divisor “Novas
  mensagens”, ancora no divisor como B-088, sem deixar o scroll no topo por
  engano.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: regressão E2E/unit que abre canal com histórico
  maior que a viewport e afirma `scrollTop` perto do fim; finding `Done`.
- Próxima ação: reancorar scroll após paint/layout estável no open (e após
  mutação de altura se necessário); cobrir no E2E de timeline/shell.

### BUG-011 — Membros não veem o botão Admin

- Status: **Aberto**
- Observado em: relato de produto, 2026-08-10 — usuário com papel **Member**
  (ex.: DevAuth Alice/Bob) **não vê** o botão/link **Admin** no shell; só
  consegue chegar ao admin com conta Owner/Admin (ex.: Demo) ou URL direta.
- Hipótese: entrada Admin no footer da sidebar fica fora da viewport (lista
  de canais / viewport estreita / sidebar recolhida no narrow) ou o ator
  Member não encontra o link e interpreta como “não existe”; distinto de
  **BUG-005** (Done — redirect silencioso em `/admin`), que corrigiu feedback
  na rota mas não garante descoberta do botão. Spec B-106 prevê Member sem
  áreas admin; landing explicativa ainda depende do link ser visível.
- Arquivos: `apps/web/src/app/layout/shell.page.html` (footer Admin),
  `shell.page.scss` (`.shell__sidebar` / `.shell__sidebar-footer`),
  `apps/web/src/app/features/admin/admin.guard.ts`, seed de papéis.
- Resultado esperado: Member vê o link Admin (ou alternativa clara no shell)
  e, ao abrir, recebe o empty-state/explicação de BUG-005 sem sumir o
  ponto de entrada; Owner/Admin/Auditor seguem com acesso às áreas.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: sessão Member mostra o controle Admin no shell
  (desktop e narrow com sidebar aberta); regressão E2E/smoke; finding `Done`.
- Próxima ação: confirmar se o footer está clipado/oculto no layout; se o
  produto quiser esconder Admin de Member, documentar e alinhar B-106 —
  senão garantir visibilidade do link + empty-state.

## Fechados

| ID      | Área                | Achado                                            | Severidade | Status |
| ------- | ------------------- | ------------------------------------------------- | ---------- | ------ |
| BUG-001 | Composer / timeline | Mensagens aparecem duplicadas ao enviar           | Alta       | Done   |
| BUG-003 | Anexos              | Upload de arquivo falha com erro                  | Alta       | Done   |
| BUG-004 | Composer / áudio    | Áudio do microfone não envia / prévia some        | Alta       | Done   |
| BUG-005 | Admin               | Página `/admin` “não entra” (Member sem feedback) | Alta       | Done   |
| BUG-007 | Theme / layout      | Layout quebrado — CSS global bloqueado pelo CSP   | Alta       | Done   |
| BUG-009 | Threads / realtime  | Reply de thread sem update em tempo real          | Alta       | Done   |
