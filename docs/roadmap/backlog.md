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

Corrigir antes de novas features de diferenciação. Ordem sugerida: realtime → typing self → scroll → UI.

| ID | Item | Notas |
|----|------|-------|
| B-070 | Realtime: mensagens/eventos ao vivo | **Done (Wave 6)** — ingest hub `MessageCreated`/edit/delete/`ReactionChanged` (payload JsonNode) + gap-fill por `seq` no reconnect/overlap; E2E dois usuários |
| B-071 | Typing: ocultar indicador do próprio usuário | **Done (Wave 6)** — filtro no client + E2E; autor não vê o próprio “digitando…” |
| B-072 | Scroll só no bloco da conversa | **Done (Wave 6)** — shell sem scroll da página; timeline com overflow interno + composer fixo |

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
| B-048 | Webhooks outbound | **Done** — `integrations.webhook_endpoints` + admin settings; delivery `MessageCreated` HMAC via outbox; secret mascarado; só `workspace.admin` |
| B-049 | Temas light/dark polish | **Done (Wave 5)** — `color-scheme`, segue OS até pin do usuário, transição sutil |
| B-067 | Auditoria completa de conversas (ADMIN) | **Done (Wave 6)** — `GET /admin/conversations*` + threads; body soft-delete visível; authZ `admin.dashboard`; distinto de `audit_events` (B-042) |
| B-068 | Cadastro de usuário + diretivas (clareza + fluxo) | **Done (Wave 6)** — invite admin + claim pending no login; glossário Cadastro/Diretiva; sem self-signup |
| B-069 | Configurações sensíveis só admin | **Done (Wave 6)** — `GET/PUT /admin/settings` mascarado; exige `workspace.admin` (não Auditor); AI/SMTP em env; webhook secret gravável (B-048) |
| B-073 | UI polish com PrimeNG | **Done (Wave 6)** — histórico; emenda ADR-002 da época Accepted; PrimeNG 22 no `/admin`. **Superseded por D-15 / B-104** (sair do PrimeNG) |
| B-074 | API + Web (+ Worker) no Compose | **Done (Wave 6)** — profile `apps` caminho oficial (`task apps`); healthchecks api/web/worker; OIDC Authority + MetadataAddress; `GET /ready`; ops/README/`.env.example`; `task dev` só DX hot-reload |

## Sustentação — Wave 7

Itens de manutenção e hardening pós-Wave 6. B-076…B-078 saem dos **controles mínimos**
ainda em aberto em `docs/security/modelo-ameacas.md`. Lacunas fechadas fora de item de
backlog ficam no **Registro de GAPs** (`roadmap.md`).

| ID | Item | Notas |
|----|------|-------|
| B-075 | E2E Playwright na CI | **Done (W7-1)** — job **E2E (Playwright)** na CI com DevAuth; `infra/scripts/ci-e2e.sh` / `task test:e2e:ci` (#45) |
| B-104 | Remover PrimeNG (UI própria + CDK) | Planned (W7-6) — D-15 opção (c); spec `docs/product/specs/B-104-remover-primeng.md`; prioridade sobre W7-3…W7-5 (desbloqueia composer / UX-002) |
| B-076 | Atualização automatizada de dependências | Planned (W7-3) — sem `.github/dependabot.yml` nem renovate; `Dependency audit notes` na CI é informativo e nunca reprova o build |
| B-077 | CSP no web | Planned (W7-4) — nginx do profile `proxy` já manda HSTS/`nosniff`/`X-Frame-Options`/`Referrer-Policy`; falta CSP |
| B-078 | Limite de tamanho de body no envio | Planned (W7-5) — `Message.Body` limitado a 8000 só na coluna; `POST .../messages` não valida e devolve 500 em vez de 400 |

## Paridade de mensageria — Waves 8 a 10

Escopo autorizado por **D-11**; base em `docs/product/benchmark-mensageria.md`. Cada
item tem spec em `docs/product/specs/` — **sem spec, não é elegível para o Build**.

### Wave 8 — Composição de mensagem

| ID | Item | Notas |
|----|------|-------|
| B-079 | Anexos múltiplos, drag & drop, colar, progresso | Planned (W8-1) — hoje é 1 arquivo por botão; drag é enhancement, nunca caminho único (WCAG 2.2 · 2.5.7) |
| B-080 | Mensagem de áudio | Planned (W8-2) — MIME negociado no cliente, waveform nos metadados, transcrição opt-in (D-12 / D-06) |
| B-081 | Formatação de texto | Planned (W8-3) — Markdown restrito; `body` continua Markdown no banco |
| B-082 | Menções | Planned (W8-4) — token `<@userId>`, `message_mentions`, badge separado |
| B-083 | Emoji picker e reações livres | Planned (W8-5) — hoje 6 emojis fixos; validação por forma no servidor |
| B-084 | Responder citando | Planned (W8-6) — `ReplyToMessageId` já existe e nunca é usado |
| B-085 | Encaminhar mensagem | Planned (W8-7) — anexo por referência, contagem para o purge de B-047 |
| B-086 | Rascunho persistente | Planned (W8-8) — só cliente, por implicação de retenção (D-03) |
| B-087 | Comandos slash | Planned (W8-9) — lista vem do servidor; sem privilégio novo |

### Wave 9 — Leitura da timeline

| ID | Item | Notas |
|----|------|-------|
| B-088 | Agrupamento, separadores e não lidas | Planned (W9-1) |
| B-089 | Histórico paginado e pular para a mensagem | Planned (W9-2) — hoje janela fixa de 50 |
| B-090 | Preview de anexos | Planned (W9-3) — miniatura no worker; purge remove junto |
| B-091 | Link preview | Planned (W9-4) — **exige guarda de SSRF**; sem ela não passa |
| B-092 | Fixar mensagem | Planned (W9-5) — limite 20, permissão `message.pin` |
| B-093 | Salvos | Planned (W9-6) — revalida membership na leitura |
| B-094 | Recibos de leitura e não lidas | Planned (W9-7) — liga o `upsertReadCursor` órfão; `read-by` em canal é contagem |

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

## P3 — Escala / futuro

| ID | Item | Gatilho |
|----|------|---------|
| B-060 | OpenSearch | ADR-016 |
| B-061 | Bus externo | ADR-015 |
| B-062 | Helm/K8s | ADR-017 |
| B-063 | Clientes mobile nativos | demanda |
| B-064 | E2EE (se produto decidir) | mudança de modelo |
| B-065 | Federation multi-instância | pesquisa |
| B-066 | Marketplace de bots | pós-contratos estáveis |

## Ordem de consumo sugerida

Sempre esvaziar **P0** antes de P1. Em P1, preferir: editar/delete → DMs → anexos → busca → threads → rate-limit → dashboards → backup → spaces → presence → reações → PWA. (B-020…B-031 já Done na Wave 4.)

Pós-MVP: **P1.5** (B-070…B-072), Wave 6, B-048, B-045, B-046, B-047 e B-075 Done.

Ordem daqui para frente: **Sustentação** (B-076 → B-077 → B-078) e depois
**paridade de mensageria** na ordem das waves — 8 (composição), 9 (leitura), 10
(notificações, organização e acesso). Dentro de cada wave, seguir a numeração; itens
sem dependência entre si podem ir em paralelo por trilhas diferentes.

## Itens explicitamente rejeitados na fase 1

- Microserviços por módulo
- Clonar UI Slack/Discord
- Kubernetes obrigatório
- Elasticsearch obrigatório
- IA ligada por default
