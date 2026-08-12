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

| ID      | Área              | Achado                                                                | Severidade | Status                                       |
| ------- | ----------------- | --------------------------------------------------------------------- | ---------- | -------------------------------------------- |
| BUG-002 | Sidebar / unread  | Badges de novas mensagens não limpam de forma persistente após reload | Média      | **Done** — B-094 |
| BUG-008 | Presence          | Minimizar a janela marca ausente na hora                              | Média      | Done                                         |
| BUG-010 | Timeline / scroll | Ao abrir a conversa não rola até as mensagens mais recentes           | Média      | Done                                         |
| BUG-011 | Admin / shell     | Membros não veem o botão Admin                                        | Média      | Done — B-106 (Member sem link)               |
| BUG-012 | Timeline / bolha  | Toolbar de hover fica em cima da mensagem (texto, reações, cabeçalho) | Média      | Done                                         |
| BUG-013 | Timeline / pins   | “Ir até” mensagem fixada buga o scroll da timeline                    | Média      | Done                                         |
| BUG-014 | Timeline / áudio  | Bolha de áudio não mostra as waves do áudio tocando                   | Média      | Done                                         |
| BUG-015 | Timeline / scroll | Load older ao rolar pra cima devolve o scroll às mais recentes        | Média      | Done                                         |
| BUG-016 | Timeline / unread | Tarja “Novas mensagens” salta/estranha quando chegam outras novas     | Média      | Done                                         |
| BUG-017 | Composer / reply  | Clicar em “Responder” não foca o composer                            | Média      | Done                                         |
| BUG-018 | Timeline / scroll | Scroll não fica colado no fim ao enviar/receber (às vezes)           | Média      | Done                                         |

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

- Status: **Done** — B-094 (persistência definitiva em `messaging.read_cursors`).
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

- Status: **Done**
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
- Resolução: `visibilityState === 'hidden'` agenda `SetAway` após
  `AWAY_GRACE_MS` (120s); `visible` cancela o timer e chama `heartbeat`;
  limpeza em `stopPresenceLoop`. Regressão `chat-hub-presence.spec.ts`.

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

- Status: **Done** (2026-08-11)
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
- Causa confirmada: a ancoragem acontecia antes da estabilização do layout e
  o único rAF não acompanhava mudanças tardias de altura.
- Resolução: controller cancelável reaplica a âncora por frames e
  `ResizeObserver`, prioriza o divisor de não lidas e cede imediatamente em
  wheel/pointer/touch/teclado. Regressão em `timeline-scroll.spec.ts` e E2E
  `timeline-anchor.spec.ts` com histórico maior que a viewport.

### BUG-011 — Membros não veem o botão Admin

- Status: **Done** — decisão de produto alinhada a B-106 (Member sem `/admin`)
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
- Resultado esperado (fechamento): Member **não** vê o link Admin (matriz
  B-106); Owner/Admin/Auditor veem o link; deep-link `/admin` por Member
  mantém empty-state de BUG-005 sem abrir áreas privilegiadas.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: Member sem link Admin no shell; Owner/Admin/Auditor
  com link; regressão unit/smoke; finding `Done`.
- Resolução: `shell.page` renderiza Admin só com `hasAdminDashboard` nos
  workspaces; smoke em `shell-responsive.spec.ts` (Member vs WorkspaceOwner).

### BUG-012 — Toolbar de hover fica em cima das mensagens

- Status: **Done** (2026-08-11)
- Observado em: relato de produto + capturas no lab Compose (`localhost:4200`),
  2026-08-10 / 2026-08-11 — no hover, a barra de reações rápidas + **Responder**
  - ⋯ **não fica ao lado/acima da bolha de forma limpa**; cobre o conteúdo da
    própria mensagem. Sintomas vistos nas capturas:
  1. **Cobre o corpo** — barra atravessa o meio/fundo da bolha (ex.: Alice
     “opaaa / falara galera / ta paz?”), obscurecendo a última linha e o
     badge de reação (✅ 1).
  2. **Cobre o topo** — em bolhas mine (ex.: “Olaaa”, “Demo / opaaa”) a barra
     corta a borda superior ou inferior da bolha teal; cabeçalho/texto ficam
     parcialmente ilegíveis.
  3. **Mensagem curta some atrás** — em uma palavra (ex.: “ae”) a toolbar
     ocupa quase toda a bolha; o texto quase desaparece sob a barra.
  4. **Âncora solta no stack** — em timeline agrupada (B-088) a barra flutua
     no vazio entre linhas / perto do divisor da sidebar, longe do texto
     hoverado (ex.: pill `e2e-hist-…` e barra acima da bolha da Alice),
     parecendo “solta” em cima do fluxo.
     Percepção de produto: “não fica legal, fica em cima das msgs — dá para
     melhorar muito.”
- Hipótese (duas âncoras quebradas no mesmo componente):
  - Bolha padrão (`.vc-msg__toolbar`): `position: absolute; top: -0.45rem` e,
    no hover, `transform: translateY(0)` — a barra **permanece sobreposta** à
    borda da bolha em vez de sair do retângulo do conteúdo.
  - Superfície `plain` / stack (`.vc-msg--plain .vc-msg__toolbar`):
    `top: 0.35rem; right: 0; transform: translate(0.35rem, -100%)` ancorada em
    `.vc-msg__column`, que herda a largura do
    `timeline__stack-body` (`width: max-content`). Em linha curta dentro de
    bloco mais largo, `right: 0` cola a barra na borda do **stack**, não do
    texto; o `-100%` + padding comprimido em `groupRole` `middle`/`end` faz a
    barra invadir a linha de cima, cobrir a própria linha ou flutuar no vazio.
- Arquivos: `apps/web/src/app/shared/ui/message-bubble/message-bubble.ts`
  (`.vc-msg__toolbar`, variantes `--plain` / `--grouped` / mine|theirs),
  `apps/web/src/app/features/chat/timeline/timeline.ts`
  (`.timeline__stack-body`), `timeline-items.ts` (agrupamento B-088).
  Spec de intenção: B-163 (toolbar discreta no hover, sem chrome em cima do
  conteúdo).
- Resultado esperado: hover/focus revela toolbar **fora** do retângulo legível
  da mensagem — tipicamente acima ou no inline-end da bolha/linha, com folga
  clara — sem cobrir texto, nome/hora, badge de reação nem linha vizinha do
  stack; estável em mine/theirs, curta/longa, agrupada e touch (long-press / ⋯).
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: regressão visual/unit ou E2E com (a) bolha curta,
  (b) bolha multi-linha com reação, (c) stack com linha curta após linha longa;
  em todos, a toolbar não intersecta o texto nem o badge de reação;
  finding `Done`.
- Causa confirmada: o posicionamento absoluto se ancorava à coluna compartilhada
  do stack e inevitavelmente invadia conteúdo próprio ou vizinho.
- Resolução: a toolbar expansível foi substituída por um único gatilho `⋯`,
  apresentado como handle circular compacto no lado externo livre da bolha
  (à esquerda para mine e à direita para theirs), sem reduzir a área do texto.
  O gatilho abre um popover CDK
  estável com reações, responder
  e demais ações; não existe gap de hover nem mudança de geometria. Cada item
  agrupado preserva sua própria bolha, mensagens do autor atual mantêm o mesmo
  inline-end e a largura útil chega a 44rem sem ocupar a timeline inteira. A
  primeira mensagem mostra autor + hora no cabeçalho; continuações revelam a
  hora no gutter em hover/foco, sem sobrepor ou criar uma segunda linha. Unit e
  E2E `timeline-toolbar-layout.spec.ts` cobrem mensagem longa do Demo, curta,
  agrupada, reação e popover dentro da viewport em 1280/900/390px; inspeção no
  Chrome em 1920px cobre também o alinhamento do grid largo.

### BUG-013 — “Ir até” mensagem fixada buga o scroll

- Status: **Done** (2026-08-11)
- Observado em: relato de produto, 2026-08-11 — no painel de mensagens fixadas,
  ao clicar **Ir até**, a timeline tenta ancorar na mensagem e o **scroll fica
  quebrado** (posição errada, trava, ou deixa de responder de forma estável
  depois do salto).
- Hipótese: `PinStore.jumpToPin` → `jumpToSequence` faz `replace` da página
  `around` + `queueMicrotask` → `jumpToMessage` (`scrollIntoView` smooth) antes
  do layout/altura final da lista; conflito com `setViewingLatest(false)`,
  sticky de data, load older no topo e/ou o mesmo timing frágil de **BUG-010**.
  Fechar o painel no meio do salto pode ainda alterar a largura do scroller.
- Arquivos: `apps/web/src/app/core/services/pin.store.ts` (`jumpToPin`),
  `apps/web/src/app/core/services/message.store.ts` (`jumpToSequence`,
  `jumpToMessage`), `apps/web/src/app/features/chat/pins-panel/pins-panel.ts`,
  `apps/web/src/app/features/chat/timeline/timeline.ts`.
- Resultado esperado: **Ir até** ancora a mensagem fixada no centro (ou
  visível) da viewport; scroll da timeline continua suave e utilizável
  (cima/baixo, jump to latest, load older) após o salto; highlight temporário
  sem travar o scroller.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: regressão E2E/unit que fixa mensagem fora da página
  atual, clica **Ir até**, afirma elemento visível e scroll ainda operável;
  finding `Done`.
- Causa confirmada: o store consultava o DOM antes de a página `around` e o
  resize causado pelo painel estarem estabilizados; auto-scrolls concorrentes
  podiam substituir o salto.
- Resolução: pedido interno tipado e versionado (`channelId`, `messageId`,
  `requestId`) é consumido pela timeline após render/layout; pedidos antigos
  são descartados, o painel fecha antes do salto e o pedido mantém prioridade
  até terminar a janela de estabilização (inclusive paginação tardia). Units
  cobrem lifecycle e ordem; E2E `timeline-anchor.spec.ts` valida
  centro/highlight, painel fechado e scroll utilizável após o salto.

### BUG-014 — Bolha de áudio sem waves na reprodução

- Status: **Done** (2026-08-11)
- Observado em: relato de produto, 2026-08-11 — na timeline, a bolha de
  mensagem de áudio **não exibe as waves** (waveform) do áudio enquanto
  toca; o player sobe (play/pause, tempo, scrubber), mas o canvas da
  waveform fica vazio, com linha plana ou sem progresso visual nas barras.
- Hipótese: `vc-audio-message` desenha o canvas uma vez via `effect` com
  `drawAudioWaveform(canvas, attachment.waveform)`; se `waveform` vier
  `undefined`/vazio (não persistido no envio, perdido no map HTTP/hub, ou
  ainda não reidratado), o util só pinta uma linha horizontal fraca. Além
  disso, o desenho é estático — `ontimeupdate` atualiza só `elapsedMs` /
  range, **sem** redesenhar barras com progresso (played vs restante), então
  mesmo com samples o usuário não vê “waves tocando”.
- Arquivos: `apps/web/src/app/shared/ui/audio-message/audio-message.ts`,
  `apps/web/src/app/shared/utils/audio.ts` (`drawAudioWaveform`),
  gravação/`waveform` em `audio-recorder.service.ts` e initiate em
  `attachment-queue.service.ts`, map de attachment em `api.service.ts` /
  `chat.models.ts`.
- Resultado esperado: bolha de áudio mostra waveform com barras; durante a
  reprodução o progresso fica visível nas waves (ou equivalente claro);
  áudio sem samples ainda tem fallback legível, não canvas “morto”.
- Risk class: R1.
- Owner automático: Frontend (D); Files (C) se o payload `waveform` não
  estiver chegando.
- Critério de resolução: mensagem `Audio` com waveform persistido renderiza
  barras; play atualiza visual de progresso na wave; regressão unit do
  draw/progresso; finding `Done`.
- Causa confirmada: o canvas era estático e o cleanup do próprio efeito
  interrompia o áudio quando um redesenho reativo era necessário; ausência de
  samples produzia apenas uma linha quase invisível.
- Resolução: `drawAudioWaveform` aceita progresso opcional, diferencia barras
  tocadas/restantes e usa samples fallback determinísticos. O componente
  redesenha em `timeupdate`, scrub e término, e limpa o áudio somente no
  destroy. Units cobrem samples, fallback, clamp, reprodução, scrub e término.

### BUG-015 — Load older devolve o scroll às mais recentes

- Status: **Done**
- Severidade: **Média**
- Observado em: relato de produto, 2026-08-11 — ao rolar a timeline para cima
  (histórico) e disparar o carregamento de mensagens mais antigas, o scroll
  **salta de volta** para as mensagens mais recentes (drift da viewport).
- Hipótese: (1) o `effect` de chegada em `timeline.ts` trata o crescimento da
  lista no prepend (`loadOlderMessages`) como “mensagens novas”:
  `list.slice(-added)` pega a cauda já visível; se alguma for `mine`, chama
  `scrollToBottom`. (2) a compensação `scrollTop += delta` após o prepend usa
  um único `rAF` e compete com skeleton/`ResizeObserver`/auto-scroll, então a
  âncora visual de B-089 falha.
- Arquivos: `apps/web/src/app/features/chat/timeline/timeline.ts`
  (`maybeLoadOlder`, effect de `forActiveChannel`),
  `apps/web/src/app/core/services/message.store.ts` (`loadOlderMessages` /
  prepend), `timeline-scroll.ts`.
- Resultado esperado: carregar página anterior preserva a mensagem sob o
  viewport (B-089); prepend nunca dispara auto-scroll para o fim nem infla
  contagem de novas.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: regressão unit/E2E — rolar ao topo, load older, a
  mesma âncora visual permanece; finding `Done`.
- Resolução: effect ignora prepend (`loadingOlder` / cauda estável); âncora
  `kind: 'prepend'` reaplica via double rAF + `ResizeObserver` curto sem
  competir com `scrollToBottom`. Regressão em `timeline-scroll.spec.ts`.

### BUG-016 — Tarja “Novas mensagens” estranha com novas chegando

- Status: **Done**
- Severidade: **Média**
- Observado em: relato de produto, 2026-08-11 — com a tarja/divisor “Novas
  mensagens” visível, ao chegarem outras mensagens novas a tarja **salta ou
  fica inconsistente** (posição e/ou contagem do botão “Ir para a mais
  recente”).
- Hipótese: `buildTimelineItems` recoloca o divisor como “antes das últimas N
  mensagens” (`unreadDividerAfterSeq` + `unreadSnapshot` fixo). Cada append
  no fim empurra o “bloco de N” para baixo e a tarja **migra** em direção ao
  bottom. Em paralelo, prepend de histórico (BUG-015) inflava `newWhileAway`
  com o tamanho da página, deixando o botão de salto com contagem absurda.
- Arquivos: `apps/web/src/app/features/chat/timeline/timeline.ts`,
  `timeline-items.ts` (`unreadDividerAfterSeq` / `dividerAfterSeq`).
- Resultado esperado: divisor congela no `afterSeq` da abertura do canal
  (B-088) até dismiss por scroll; novas no fim só atualizam o botão “Ir para
  a mais recente (N)”; load older não mexe na contagem.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: unit com `dividerAfterSeq` congelado sob append;
  contagem `newWhileAway` só sob append real; finding `Done`.
- Resolução: `frozenUnreadAfterSeq` na abertura; effect distingue prepend vs
  append; unit `timeline-items.spec.ts` cobre divisor congelado sob append.

### BUG-017 — “Responder” não foca o composer

- Status: **Done**
- Severidade: **Média**
- Observado em: relato de produto, 2026-08-11 — ao clicar **Responder** na
  bolha (toolbar/menu), a barra de citação aparece no composer (B-084), mas o
  foco **não** vai para o textarea; o usuário precisa clicar de novo no
  composer para digitar.
- Hipótese: `Timeline.onReply` / `ThreadPanel` só chamam
  `setReplyTarget(message)`; o `Composer` reage a `replyTarget` para mostrar a
  barra de citação, mas não há `focus()` no `vc-textarea` após o reply (mesmo
  padrão esperado em B-173 para edição).
- Arquivos: `apps/web/src/app/features/chat/timeline/timeline.ts` (`onReply`),
  `apps/web/src/app/features/chat/thread-panel/thread-panel.ts`,
  `apps/web/src/app/core/services/message.store.ts` (`setReplyTarget`),
  `apps/web/src/app/features/chat/composer/composer.ts`.
- Resultado esperado: ao iniciar resposta (canal ou thread), o composer recebe
  foco no textarea imediatamente, com a citação já visível, pronto para digitar.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: unit/component — “Responder” → `document.activeElement`
  é o textarea do composer; finding `Done`.
- Resolução: `effect` no `Composer` foca o `vc-textarea` quando `replyTarget`
  fica preenchido (canal e thread); regressão em `composer.spec.ts`.

### BUG-018 — Scroll sticky ao enviar/receber

- Status: **Done**
- Severidade: **Média**
- Observado em: relato de produto, 2026-08-12 — às vezes ao enviar ou receber
  mensagem o scroll **não permanece colado** no fim, mesmo com a viewport já
  nas mensagens mais recentes. Esperado: colado enquanto o usuário está no
  fundo; descola só quando ele rola para cima e navega o histórico.
- Hipótese (confirmada): o effect de chegada em `timeline.ts` remedia
  `isNearBottom(el)` **depois** do append no DOM; o `scrollHeight` já cresceu,
  então quem estava no fim passa do threshold (`NEAR_BOTTOM_PX`) e o stick
  falha de forma intermitente (pior com bolha alta/mídia).
- Arquivos: `apps/web/src/app/features/chat/timeline/timeline.ts`,
  `timeline-scroll.ts` (`shouldStickTimelineToBottom`,
  `TimelineStickyBottomPin`).
- Resultado esperado: no fim, enviar/receber mantém o fundo; rolando para cima
  não força o fim; voltar ao fim (scroll/botão) recoloca o pin.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: latched `nearBottom` (sem remedir pós-append);
  `ownArrival` sempre cola; pin contínuo via `ResizeObserver` enquanto colado;
  regressão unit; finding `Done`.
- Resolução: decisão `ownArrival || nearBottom()` antes do microtask; pin
  contínuo enquanto `nearBottom`; unit em `timeline-scroll.spec.ts`.

## Fechados

| ID      | Área                | Achado                                            | Severidade | Status |
| ------- | ------------------- | ------------------------------------------------- | ---------- | ------ |
| BUG-001 | Composer / timeline | Mensagens aparecem duplicadas ao enviar           | Alta       | Done   |
| BUG-003 | Anexos              | Upload de arquivo falha com erro                  | Alta       | Done   |
| BUG-004 | Composer / áudio    | Áudio do microfone não envia / prévia some        | Alta       | Done   |
| BUG-005 | Admin               | Página `/admin` “não entra” (Member sem feedback) | Alta       | Done   |
| BUG-007 | Theme / layout      | Layout quebrado — CSS global bloqueado pelo CSP   | Alta       | Done   |
| BUG-009 | Threads / realtime  | Reply de thread sem update em tempo real          | Alta       | Done   |
