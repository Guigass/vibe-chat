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

| ID | Área | Achado | Severidade | Status |
|----|------|--------|------------|--------|
| BUG-002 | Sidebar / unread | Badges de novas mensagens não limpam de forma persistente após reload | Alta | Aberto — fecha em **B-094** |
| BUG-005 | Admin | Página `/admin` “não entra” (Member redireciona sem feedback) | Alta | Aberto — safety lane |
| BUG-006 | Realtime | Conexão em tempo real caindo com frequência | Alta | Aberto — safety lane |
| BUG-007 | Theme | Modo escuro não funciona | Alta | Aberto — safety lane |
| BUG-008 | Presence | Minimizar a janela marca ausente na hora | Média | Aberto |

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

- Status: **Aberto** — fecha em **B-094**
  ([spec](specs/B-094-recibos-de-leitura.md)).
- Observado em: sidebar com badges após F5 / novo load; probe Playwright
  2026-08-10 em `localhost:4200` (badges presentes pós-login).
- Hipótese: `ApiService.upsertReadCursor` existe e **não é chamado**;
  `selectChannel` zera badge só em memória; `refreshUnreads` reidrata contagens
  do servidor com `lastReadSeq` antigo.
- Arquivos: `apps/web/src/app/core/services/channel.store.ts`,
  `apps/web/src/app/core/api/api.service.ts`, endpoints `read-cursor` /
  `unread-count` na API.
- Resultado esperado: abrir/ler canal persiste cursor; F5 e multi-device
  refletem unread correto (contrato B-094).
- Risk class: R2.
- Owner automático: Messaging (C) + Frontend (D).
- Critério de resolução: B-094 Done + este finding `Done` no mesmo PR (ou PR
  seguinte de docs se o merge de B-094 já fechou o sintoma).
- Próxima ação: quando W9-7 / B-094 for elegível, implementar a spec; se safety
  lane exigir alívio antes e couber no budget, fix mínimo do cursor no caminho
  principal sem substituir B-094.

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

- Status: **Done**
- Observado em: relato de produto; feature B-080 entregue no código; depende do
  mesmo path MinIO que BUG-003.
- Hipótese: falha em `getUserMedia` / contexto inseguro / MIME do `MediaRecorder`,
  ou upload do blob de áudio via `uploadRecordedAudio` (mesmo presign MinIO).
  Picker de arquivo rejeita áudio pela allowlist — só o fluxo Mic é válido.
- Arquivos: `apps/web/src/app/features/chat/composer/audio-recorder.ts`,
  `audio-recorder.service.ts`, `composer.ts` (`sendRecording`),
  `attachment-queue.service.ts`.
- Resultado esperado: gravar → enviar cria mensagem com anexo `Audio` e
  waveform/metadados conforme B-080.
- Risk class: R2.
- Owner automático: Frontend (D) + Files (C).
- Critério de resolução: caminho Mic → mensagem persistida com teste de
  regressão; erros de permissão/MIME visíveis ao usuário.
- Resolução: path MinIO herdado de BUG-003; `normalizeAudioContentType` no
  initiate/File; erros explícitos em blob inválido e falha de `messages.send`;
  `discard()` não zera `discardOnStop` antes do `onstop`; clear da fila após
  envio ok; regressão Vitest (`audio-recorder.spec`, `attachment-queue.audio.spec`).

### BUG-005 — Página admin não entra

- Status: **Aberto**
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
- Próxima ação: UX de negação + nota no guia/lab; não conceder admin a Alice por
  default.

### BUG-006 — Conexão em tempo real caindo com frequência

- Status: **Aberto**
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
- Próxima ação: monitorar eventos SignalR no browser + logs API/Redis durante
  idle e troca de canal; validar WS via proxy se aplicável.

### BUG-007 — Modo escuro não funciona

- Status: **Aberto**
- Observado em: relato de produto, 2026-08-10 — toggle / preferência de tema
  escuro não aplica (ou não persiste) a UI esperada.
- Hipótese: `ThemeService` altera `html[data-theme]` / `colorScheme`, mas o
  caminho de bootstrap (`App` inject), o toggle (`theme-toggle`) ou tokens
  `[data-theme='dark']` / variante Tailwind `dark` não reagem de ponta a
  ponta; possível race com `data-theme="light"` fixo em `index.html` ou
  estilos hardcoded que ignoram o atributo.
- Arquivos: `apps/web/src/app/core/services/theme.service.ts`,
  `apps/web/src/app/app.ts`,
  `apps/web/src/app/shared/ui/theme-toggle/theme-toggle.ts`,
  `apps/web/src/styles.scss`, `apps/web/src/styles/_tokens.scss`,
  `apps/web/src/index.html`.
- Resultado esperado: ativar tema escuro troca tokens/`data-theme` de forma
  visível e persistente (`vc.theme`); reload mantém a escolha do usuário.
- Risk class: R1.
- Owner automático: Frontend (D).
- Critério de resolução: toggle dark/light altera `data-theme` e a superfície
  principal; preferência sobrevive a F5; regressão unit/componente no
  `ThemeService` / toggle.
- Próxima ação: reproduzir no lab (`localhost:4200`), inspecionar
  `document.documentElement.dataset.theme` após o toggle e mapear superfícies
  que não leem `[data-theme='dark']`.

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

## Fechados

| ID | Área | Achado | Severidade | Status |
|----|------|--------|------------|--------|
| BUG-001 | Composer / timeline | Mensagens aparecem duplicadas ao enviar | Alta | Done |
| BUG-003 | Anexos | Upload de arquivo falha com erro | Alta | Done |
| BUG-004 | Composer / áudio | Áudio do microfone não envia | Alta | Done |
| BUG-009 | Threads / realtime | Reply de thread sem update em tempo real | Alta | Done |
