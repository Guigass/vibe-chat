# Backlog Priorizado — VibeChat

Prioridade: **P0** (fatia vertical) → **P1** (MVP usável) → **P2** (diferenciação) → **P3** (depois).

## P0 — Bloqueadores da fatia vertical

| ID | Item | Notas |
|----|------|-------|
| B-001 | Compose + seeds Keycloak/Postgres | **Done** |
| B-002 | OIDC login web + API JWT | **Done** |
| B-003 | TenantContext + Directory mínimo | **Done** |
| B-004 | Send message + seq + idempotency | **Done** |
| B-005 | Outbox + Worker | **Done** |
| B-006 | SignalR deliver message.created | **Done** |
| B-007 | History API | **Done** |
| B-008 | UI channel + composer | **Done** |
| B-009 | RLS + testes security | **Done** |
| B-010 | Health/ready + CI básica | **Done** |

## P1 — MVP usável em empresa pequena

| ID | Item | Notas |
|----|------|-------|
| B-020 | Spaces na UI + criar channel | **Done (Wave 4)** — `Channel.SpaceId`, API spaces + create channel, sidebar agrupada, membership/`channel.create`, testes |
| B-021 | DMs 1:1 | **Done (Wave 4)** — Directory members + get-or-create DM + UI |
| B-022 | Threads | **Done (Wave 4)** — API get-or-create + replies com seq próprio, authZ membership, outbox/hub `threadId`, UI painel + testes |
| B-023 | Editar / soft-delete mensagem | **Done (Wave 4)** — API authZ + hub + UI + testes |
| B-024 | Reações | **Done (Wave 4)** — toggle API + outbox/hub `ReactionChanged`, UI na bubble, authZ `message.react`, testes |
| B-025 | Anexos MinIO | **Done (Wave 4)** — presign upload/download, authZ, UI composer + bubble, testes |
| B-026 | Presence + typing | **Done (Wave 4)** — typing + reconnect rejoin; presence online/away Redis TTL + hub Heartbeat/SetAway + UI indicadores |
| B-027 | Busca FTS | **Done (Wave 4)** — tsvector/GIN, API Search, membership ACL, UI shell, testes |
| B-028 | Rate-limit | **Done (Wave 4)** — Redis INCR send/hub |
| B-029 | PWA | **Done (Wave 4)** — manifest/icons + SW (ngsw) installability + offline shell/banner |
| B-030 | Dashboards Grafana | **Done** — overview (requests, outbox, SignalR) provisionado |
| B-031 | Backup scripts | **Done (Wave 4)** — `backup.sh` + `restore-drill.sh` + doc drill |

## P1.5 — Polish pós-MVP (bugs / UX reportados)

Itens B-070…B-072 da Wave 6 estão **Done**. Bugs funcionais abertos do caminho
principal ficam em [`docs/product/bug-findings.md`](../product/bug-findings.md)
e entram na **safety lane** do Build (antes de novas features de wave).

| ID | Item | Notas |
|----|------|-------|
| B-070 | Realtime: mensagens/eventos ao vivo | **Done (Wave 6)** — ingest hub `MessageCreated`/edit/delete/`ReactionChanged` (payload JsonNode) + gap-fill por `seq` no reconnect/overlap; E2E dois usuários |
| B-071 | Typing: ocultar indicador do próprio usuário | **Done (Wave 6)** — filtro no client + E2E; autor não vê o próprio “digitando…” |
| B-072 | Scroll só no bloco da conversa | **Done (Wave 6)** — shell sem scroll da página; timeline com overflow interno + composer fixo |
| BUG-001 | Mensagens duplicadas no envio | **Done** — lock composer + dedupe store + `clientMessageId` no hub |
| BUG-002 | Unread/badges após reload | Aberto (Média) — alívio cursor; fecha em **B-094** (W9-7) |
| BUG-003 | Upload de arquivo com erro | **Done** — presign no PublicEndpoint + CORS fail-closed + resolveContentType |
| BUG-004 | Áudio do microfone não envia | **Done** — MIME base + erros visíveis + discard/onstop + regressão Vitest |
| BUG-005 | `/admin` não entra (Member) | **Done** — shell com empty-state; Demo/Owner intactos |
| BUG-006 | Realtime caindo com frequência | **Done** — timeouts SignalR + retry pós-start/`onclose` + jitter |
| BUG-007 | Modo escuro não funciona | **Done** — seletores `:root[data-theme]` + apply síncrono + Vitest |

## P2 — Diferenciação e admin

**B-040** (guests) saiu desta seção: D-07 foi revisado em 2026-07-25 e o item virou
W10-10, com spec — ver *Paridade de mensageria*.

| ID | Item | Notas |
|----|------|-------|
| B-041 | Papéis granulares | **Done (P2)** — admin list/altera papel (`workspace.admin`); UI admin; Guest fora (D-07); testes security/integration |
| B-042 | Audit log (ações sensíveis) | **Done (Wave 5)** — eventos + `GET /admin/audit-events` + UI admin (`admin.dashboard`); **não** substitui auditoria de conversa (B-067) |
| B-043 | Notificações email (opcional) | **Done (P2)** — `IEmailSender` Null/SMTP genérico; Mailpit/env; off default; e-mail em mudança de papel via outbox (D-10) |
| B-044 | AI: resumo de thread (flag) | **Done (Wave 5)** — summarize canal + Mock/OpenRouter; off default; testes security/integration |
| B-045 | AI: sugerir resposta | **Done** — `POST .../ai/suggest-reply` + UI prefill; Mock/OpenRouter; off default; authZ `ai.suggest_reply`; testes security/integration |
| B-046 | Export de workspace | **Done** — `GET .../admin/workspaces/{id}/export` ZIP JSON (`vibechat.workspace.export.v1`); soft-delete incluído; metadados de anexos; authZ `workspace.admin`; audit `workspace.export`; UI admin |
| B-047 | Políticas de retenção configuráveis | **Done** — `messaging.message_retention_settings` + `retention.*` em admin settings; kill switch `MessageRetention:Enabled` (off default); worker purge soft-deletes; audit `message.purge`; UI admin (ADR-018 / D-03) |
| B-048 | Webhooks outbound | **Done** — `integrations.webhook_endpoints` + admin settings; delivery `MessageCreated` HMAC via outbox; secret mascarado; só `workspace.admin`. **Estender em B-108** |
| B-049 | Temas light/dark polish | **Done (Wave 5)** — `color-scheme`, segue OS até pin do usuário, transição sutil |
| B-067 | Auditoria completa de conversas (ADMIN) | **Done (Wave 6)** — `GET /admin/conversations*` + threads; body soft-delete visível; authZ `admin.dashboard`; distinto de `audit_events` (B-042) |
| B-068 | Cadastro de usuário + diretivas (clareza + fluxo) | **Done (Wave 6)** — invite admin + claim pending no login; glossário Cadastro/Diretiva; sem self-signup |
| B-069 | Configurações sensíveis só admin | **Done (Wave 6)** + **ADR-020** — `GET/PUT /admin/settings` tipado (AI/SMTP/webhook/retenção/Files/RateLimit); secrets só via `credentials/*/rotate` (AES-GCM); exige `workspace.admin` |
| B-073 | UI polish com PrimeNG | **Done (Wave 6)** — histórico; emenda ADR-002 da época Accepted; PrimeNG 22 no `/admin`. **Superseded por D-15 / B-104** (sair do PrimeNG) |
| B-074 | API + Web (+ Worker) no Compose | **Done (Wave 6)** — profile `apps` caminho oficial (`task apps`); healthchecks api/web/worker; OIDC Authority + MetadataAddress; `GET /ready`; ops/README/`.env.example`; `task dev` só DX hot-reload |

## Sustentação — Wave 7

Itens de manutenção e hardening pós-Wave 6. B-077…B-078 saem dos **controles mínimos**
ainda em aberto em `docs/security/modelo-ameacas.md`. Lacunas fechadas fora de item de
backlog ficam no **Registro de GAPs** (`roadmap.md`).

| ID | Item | Notas |
|----|------|-------|
| B-075 | E2E Playwright na CI | **Done (W7-1)** — job **E2E (Playwright)** na CI com DevAuth; `infra/scripts/ci-e2e.sh` / `task test:e2e:ci` (#45) |
| B-104 | Remover PrimeNG (spartan/ui + CDK) | **Done (W7-6)** — [#82](https://github.com/Guigass/vibe-chat/pull/82); D-15 (c) + D-27; `primeng` removido; `@spartan-ng/brain` + Helm select; fecha UX-002 |
| B-076 | Atualização automatizada de dependências | **Done (W7-3)** — [#84](https://github.com/Guigass/vibe-chat/pull/84); `.github/dependabot.yml` (nuget/npm/actions/docker/compose) + [`dependencias.md`](../operations/dependencias.md); PRs de bump já abertos pós-merge (#85…) |
| B-077 | CSP no web | **Done (W7-4)** — `infra/nginx/security-headers.conf` no proxy + web; [spec](../product/specs/B-077-csp-web.md) |
| B-078 | Limite de tamanho de body no envio | **Done (W7-5)** — `MessageBodyPolicies` (8000 UTF-16); 400 `MessageBodyTooLong`; contador no composer/thread/edit |
| B-105 | Catálogo de configuração admin mínima no `.env` | **Done (W7-7)** — EMAIL__* injetado no api; retenção no worker; alias OpenRouter unificado; testes `ComposeConfigCatalogTests`; [spec](../product/specs/B-105-catalogo-configuracao.md) |
| B-106 | Admin shell (nav, toolbars, listagens, filtros, visibilidade por papel) | Done (W7-8) — shell `/admin/*`, hide por claim (UX-005); matriz Admin/Auditor/Member; spec `docs/product/specs/B-106-admin-shell.md` |
| B-165 | Controle de versão do cliente web (cache / PWA) | **Done** (W7-9) — buildId + `version.json` + SwUpdate/CTA + headers anti-cache; fecha UX-007; [spec](../product/specs/B-165-controle-versao-cliente-web.md) |

## Paridade de mensageria — Waves 8 a 10

Escopo autorizado por **D-11**; base em `docs/product/benchmark-mensageria.md`. Cada
item tem spec em `docs/product/specs/` — **sem spec, não é elegível para o Build**.

### Wave 8 — Composição de mensagem

| ID | Item | Notas |
|----|------|-------|
| B-079 | Anexos múltiplos, drag & drop, colar, progresso | Planned (W8-1) — hoje é 1 arquivo por botão; drag é enhancement, nunca caminho único (WCAG 2.2 · 2.5.7) |
| B-080 | Mensagem de áudio | Done (W8-2) — MIME negociado no cliente, waveform nos metadados, transcrição opt-in (D-12 / D-06) |
| B-081 | Formatação de texto | Done (W8-3) — Markdown restrito; `body` continua Markdown no banco |
| B-082 | Menções | Done (W8-4) — token `<@userId>`, `message_mentions`, badge separado |
| B-083 | Emoji picker e reações livres | Done (W8-5) — picker compartilhado, validação Unicode no servidor, tooltip de quem reagiu |
| B-084 | Responder citando | **Done (W8-6)** — `replyTo` no history/hub; UI composer + bolha; Responder ≠ Abrir thread; BUG-009 realtime thread |
| B-085 | Encaminhar mensagem | **Done (W8-7)** — `POST .../messages/{id}/forward`; anexos por referência + `ReferenceCount`; UI seletor ≤5; audit `message.forward` |
| B-086 | Rascunho persistente | Done (W8-8) — só cliente, por implicação de retenção (D-03) |
| B-087 | Comandos slash | **Done (W8-9)** — discovery `GET …/commands`; `/topico` + campo Topic; autocomplete no composer |

### Wave 9 — Leitura da timeline

| ID | Item | Notas |
|----|------|-------|
| B-163 | Message bubble moderno (layout tipado + ações + preview) | **Done** (W9-0) — alinhamento mine/theirs p/ todos os tipos; preview Image/PDF/Audio/Video; toolbar só no hover/focus + context menu; base antes de pin/salvos; [spec](../product/specs/B-163-message-bubble-context-menu.md) |
| B-088 | Agrupamento, separadores e não lidas | **Done (W9-1)** — agrupamento 5 min, sticky de data, divisor local, jump + auto-scroll só no fim |
| B-089 | Histórico paginado e pular para a mensagem | Done (W9-2) |
| B-090 | Preview de anexos | Done (W9-3) — miniatura no worker; UI tipada em B-163; purge remove junto |
| B-091 | Link preview | **Done** (W9-4) — outbox + guarda SSRF (ADR-021) |
| B-092 | Fixar mensagem | **Done (W9-5)** — limite 20, permissão `message.pin`, barra/painel, hub `PinChanged` |
| B-093 | Salvos | **Done** (W9-6) — revalida membership na leitura |
| B-094 | Recibos de leitura e não lidas | Planned (W9-7) — persistência definitiva em `messaging.read_cursors`; liga o `upsertReadCursor` órfão; badges sobrevivem a F5/multi-device; `read-by` em canal é contagem |
| B-168 | Anexo de vídeo | Planned (W9-8) — aceitar `video/mp4`/`webm`; prévia no composer; player tipado na bolha (B-163); sem transcode; soft-dep poster B-090 |
| B-171 | Painéis de contexto (sidebar direita) | Planned (W9-9) — trilho direito um pouco maior e melhor encaixado (thread/pins/salvos/contexto); só UI/tokens; [spec](../product/specs/B-171-paineis-contexto-direita.md) |
| B-173 | Editar mensagem no composer | Planned (W9-10) — tirar textarea inline da bolha; modo edição no composer (padrão B-084); atalho `↑` em B-099; políticas de janela/papéis em B-107; [spec](../product/specs/B-173-editar-mensagem-no-composer.md) |

### Wave 10 — Notificações, organização e acesso

| ID | Item | Notas |
|----|------|-------|
| B-095 | Web Push | Planned (W10-1) — VAPID próprio, opt-in, payload mínimo (D-13) |
| B-096 | Enquetes | Planned (W10-2) — enquete é mensagem; anonimato é requisito de API |
| B-097 | Preferências de notificação e DND | Planned (W10-3) — fuso IANA; matriz em tabela-verdade |
| B-098 | Busca com filtros | Planned (W10-4) — filtro só restringe; membership no servidor |
| B-099 | Paleta de comandos e atalhos | Planned (W10-5) |
| B-100 | Internacionalização | Planned (W10-6) — pt-BR + en; catálogo incompleto reprova na CI (D-14) |
| B-101 | DM em grupo | Planned (W10-7) — reusa `Channel`; janela por `seq` de entrada |
| B-102 | Seguir thread | Planned (W10-8) — auto-inscrição na mesma transação |
| B-103 | Acessibilidade WCAG 2.2 AA | Planned (W10-9) — axe-core como gate na CI |
| B-040 | Guests por convite | Planned (W10-10) — deixou de estar Blocked; D-07 revisado em 2026-07-25 |
| B-107 | Políticas de edição/apagar mensagem | Planned (W10-11) — janela de tempo (`messaging.edit|delete.windowMinutes`), papéis e override de moderação; settings admin; UI de editar no composer (B-173); preservação de body no audit → B-169; [spec](../product/specs/B-107-politicas-edicao-mensagem.md) |
| B-108 | Extender webhooks outbound | Planned (W10-12) — mais eventos, multi-endpoint, filtros de canal, ping de teste; spec `docs/product/specs/B-108-extender-webhooks.md` |
| B-109 | Núcleo plugin — bot/token + envio msgs | Planned (W10-13) — capability `messages.send`; base da trilha; spec `docs/product/specs/B-109-api-integracao-envio-mensagens.md` |
| B-110 | Instalar/gerir plugins na instância | Planned (W10-14) — manifesto local, built-in Incoming Messages; deps B-109; spec `docs/product/specs/B-110-instalar-plugins.md` |

## P3 — Capacidades condicionais e itens promovidos

| ID | Item | Estado / destino |
|----|------|------------------|
| B-060 | OpenSearch | Conditional — somente gatilho ADR-016 medido |
| B-061 | Bus externo | Conditional — somente gatilho ADR-015 medido |
| B-062 | Helm/K8s | Conditional — somente gatilho ADR-017 medido |
| B-063 | Clientes mobile nativos | Planned — W15 |
| B-170 | Performance e escalabilidade | Planned — W16; hot paths/gargalos após B-146; gate antes de B-144 |
| B-172 | Backup de chat (export/import, tarefas, destinos remotos) | Planned — W16; SFTP/FTP, SMB, S3, Drive, WebDAV; complementa B-031/B-046; [spec](../product/specs/B-172-backup-chat-destinos.md) |
| B-064 | Canais confidenciais E2EE | Planned — W16; deps B-169 (gate `contentAuditEnabled`) |
| B-065 | Federação entre instâncias | Planned — W16 |
| B-066 | Plugins — capabilities avançadas | Planned — W15, após B-109/B-110 |
| B-111 | Interações governadas para plugins | Planned — W15, após B-066 |

## Waves 11–17 — Horizonte promovido

Os itens abaixo estão catalogados e ordenados no roadmap executável
[`horizonte-ambicioso.md`](horizonte-ambicioso.md). Eles não competem com Waves
7–10 porque a conclusão destas é um gate de sequência.

| Faixa | Tema | Estado |
|-------|------|--------|
| B-112…B-118, B-153/B-154, B-166/B-167 | Organização, onboarding, contatos, perfil, migração, diagnóstico, inbox e tópicos | Planned — W11 |
| B-119…B-124 | Conhecimento, decisões, RAG, tarefas e formulários | Planned — W12 |
| B-125…B-128, B-131, B-139, B-164 | Automações, incidentes, conectores, SSO e contratos | Planned — W13 |
| B-129/B-130/B-132…B-134/B-146/B-169 | Segurança enterprise, SIEM, policies, auditoria de conteúdo (`contentAuditEnabled` + snapshot de body em `message.delete`) e capacidade | Planned — W14 |
| B-135…B-138, B-140/B-141, B-063/B-066/B-111 | Plataforma, registry, bridges, branding e clients | Planned — W15 |
| B-170, B-143…B-145, B-172 + B-064/B-065 | Performance/escalabilidade, offline, HA, storage, backup de chat (destinos), federação e E2EE | Planned — W16 |
| B-147…B-149, B-152 | Live media, notas de reunião e canvas | Planned — W17 |

Todos possuem decisão e spec. A elegibilidade concreta continua dependendo de
deps `Done`, ausência de PR concorrente e gates R0–R3 de
`docs/agents/autonomia.md`.

## Ordem de consumo sugerida

Sempre esvaziar **P0** antes de P1. Em P1, preferir: editar/delete → DMs → anexos → busca → threads → rate-limit → dashboards → backup → spaces → presence → reações → PWA. (B-020…B-031 já Done na Wave 4.)

Pós-MVP: **P1.5** (B-070…B-072 Done; safety lane: BUG-006 Alta; BUG-002 aliviado —
ver `docs/product/bug-findings.md`), Wave 6, B-048, B-045, B-046, B-047 e B-075 Done.

Ordem daqui para frente: **safety lane** (`BUG-*` Alta) antes de inventar ou
avançar feature de wave; depois **paridade de mensageria** na ordem das
waves — 8 (composição; W8-9 / B-087 Done), 9 (leitura; W9-0 / B-163 Done; W9-1 / B-088 Done; B-094 fecha
BUG-002 / não lidas persistentes), 10 (notificações, organização, acesso e
núcleo de plugins: B-109 → B-108 → B-110). Depois seguir W11–W17; a trilha
avançada de plugins continua em W15 com B-066 → B-111/B-136. Dentro de cada
wave, seguir a ordem da tabela; itens sem dependência entre si podem ir em
paralelo por trilhas diferentes.

Depois disso, usar a sequência de validação do horizonte ambicioso: comunicação
organizada → conhecimento/ações → automação/governança (W13: B-164 SSO antes de
B-128 SCIM) → plataforma → apostas arquiteturais. Abrir no máximo uma aposta
arquitetural por vez.

## Itens explicitamente rejeitados na fase 1

- Microserviços por módulo
- Clonar UI Slack/Discord
- Kubernetes obrigatório
- Elasticsearch obrigatório
- IA ligada por default
