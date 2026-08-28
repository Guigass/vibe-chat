# Estado Atual do VibeChat

Snapshot factual para orientação rápida. Não substitui o
[roadmap executável](roadmap.md), o [backlog](backlog.md) nem evidência de testes.

- **Data de corte:** 2026-08-28
- **Fase:** Wave 10 — W10-5 / B-099 Done (paleta de comandos); seguinte W10-6 / B-100
- **Safety lane obrigatória:** OPS-E2E-B098 Resolved (#148); OPS-E2E-B097 Resolved (#145+#149 — cache/pin + `releaseBottom()` no scroll); sem BUG Alta aberto; BUG-006 Done; BUG-002 aliviado (Média, fecha em B-094); UX Alta do caminho principal: nenhuma; UX-001/#74, UX-002/#82, UX-003/#80 Done; UX-007 Done (B-165); UX-008 Done (B-173)
- **Próximo item elegível:** W10-6 / B-100 (internacionalização)
- **Escopo deste snapshot:** documentação e estrutura versionada do repositório

## Resumo executivo

O VibeChat já possui uma fatia vertical funcional e uma base self-hosted ampla:
autenticação OIDC/DevAuth, isolamento multi-tenant, channels, DMs, mensagens,
threads, anexos, reações, busca, presença, tempo real, administração, export,
webhooks, IA opcional, e-mail opcional, retenção e observabilidade. Waves 0–6,
W7-1, W7-3 e W7-6 estão marcadas como entregues. `SEC-RLS-RUNTIME` fechou via #72/#73
(roles separadas, FORCE+WITH CHECK, `RlsSession` SET LOCAL). UX-003 fechou via
#80 (sidebar colapsa em viewport ≤960px). B-104 / UX-002 fechou via #82
(`primeng` removido; spartan/ui no `/admin`). B-076 fechou via #84 (Dependabot
+ política em `dependencias.md`; bumps já abertos em #85…).

O trabalho aberto concentra-se em:

1. safety lane Alta esvaziada (BUG-006 Done; BUG-002 aliviado, fecha em B-094);
2. avançar Wave 10 (W10-1…W10-5 Done; seguinte W10-6 / B-100);
3. consumir o roadmap autorizado W11–W19 (Wave 19 = organização do código,
   recomendada antes de W11).

## Runtime e fronteiras

| Área | Estado observado | Fonte principal |
|------|-----------------|-----------------|
| API | ASP.NET Core .NET 10; composition root em `apps/api` | `apps/api/Program.cs` |
| Worker | Outbox/jobs e purge de retenção | `apps/worker/Program.cs` |
| Web | Angular 22 standalone, Signals, CDK, PWA | `apps/web/package.json`, `apps/web/src/` |
| Dados | PostgreSQL 16 + migrations + RLS | `compose.yaml`, `src/VibeChat.Infrastructure/Persistence/` |
| Tempo real | SignalR + Redis backplane | módulo Realtime + infraestrutura |
| Arquivos | MinIO/S3-compatible | módulo Files + Compose |
| Identidade | Keycloak OIDC; DevAuth apenas em Development; SSO corporativo OIDC/SAML planejado (B-164) | realm versionado + API |
| Deploy fase 1 | Docker Compose, profile `apps`; proxy/obs opcionais | `compose.yaml`, ADR-017 |

## Módulos existentes

| Assembly | Responsabilidade atual |
|----------|------------------------|
| Administration | Dashboard, settings sensíveis, export e consultas administrativas |
| AI | Portas e políticas para resumo/sugestão; provider opcional |
| Audit | Eventos de auditoria |
| BuildingBlocks | Contratos e tipos compartilhados de base |
| Conversations | Conversations, channels, threads e ordenação |
| Directory | Workspaces, spaces, memberships e papéis |
| Files | Metadados e políticas de anexos |
| Identity | Perfis e integração de identidade (perfil público rico: B-167 Planned) |
| Integrations | Webhooks outbound |
| Messaging | Mensagens, reações, idempotência, sequência e outbox |
| Moderation | Fronteira reservada; sem domínio material observado no snapshot |
| Notifications | E-mail e configurações de notificação |
| Realtime | Hub, presence, typing e fan-out |
| Search | Busca PostgreSQL FTS |
| Tenancy | TenantContext e isolamento lógico |

## Capacidades entregues

| Capacidade | Referências |
|------------|-------------|
| Login OIDC, tenant context e memberships | W1, B-002/B-003 |
| Mensagem com `seq`, idempotência, outbox e gap-fill | W2, B-004…B-007, B-070 |
| Policies RLS e testes cross-tenant | W3, B-009, GAPs de RLS/hub; `SEC-RLS-RUNTIME` Done (#72/#73) — roles app/migrator, FORCE+WITH CHECK, SET LOCAL |
| Threads, DMs, edit/delete, anexos e reações | W4, B-021…B-025 |
| Presence, typing, busca FTS e PWA | W4, B-026/B-027/B-029 |
| Backup, proxy TLS, load smoke e runbooks | W5 |
| Papéis, e-mail, IA, export, webhooks e retenção | P2/W6, B-041…B-048 |
| Admin, auditoria de conversas e settings mascarados | W6, B-067…B-069 |
| Apps containerizados e E2E na CI | B-074/B-075 |

## Fila aberta e ordem recomendada

| Ordem | Item | Motivo |
|-------|------|--------|
| 1 | W10-6 / B-100 | D-14 Decidido; W10-5 / B-099 Done |

W7-5 / B-078 **Done** (limite de body 8000). W7-7 / B-105 **Done** (catálogo config).
W7-9 / B-165 **Done** (versão/cache do cliente web; fecha UX-007).
W7-10 / B-174 **Done** (filtro `RequirePermission` na API Minimal).
W7-11 / B-175 **Done** (matriz authZ + gaps admin/typing).
W7-12 / B-176 **Done** (fonte de verdade de roles — DB vs Keycloak).
W7-13 / B-177 **Done** (DevAuth fail-closed).
W7-14 / B-187 **Done** (instalação configurável no admin; catálogo permanece em
`configuracao-env.md`; D-04/ADR-020 intactos — infra no env).
W8-9 / B-087 **Done** (slash commands + discovery + topic).
W10-1 / B-095 **Done** (Web Push VAPID, opt-in, outbox/worker, kill switch off default).
W10-2 / B-096 **Done** (enquetes: mensagem + `polls`/`votes`, `/enquete`, worker de prazo).
W10-3 / B-097 **Done** (preferências/DND).
W10-4 / B-098 **Done** (busca com filtros, chips, paginação e ordenação).
W10-5 / B-099 **Done** (paleta Ctrl/Cmd+K, atalhos de canal/menção/lido e folha `?`).

O horizonte pós-Wave 10 já foi promovido a roadmap executável W11–W19. Wave 19
(organização do código) é recomendada antes de W11. Ele não altera a prioridade
imediata: o Build só consome W11+ depois de W7–W10 `Done`.

## Baseline de planejamento

- 92 itens `Planned` entre W8–W19 (B-186/W10-15 — membros do canal; B-178…B-183
  catalogados em W19 — organização do código; B-104/W7-6 Done via #82;
  B-076/W7-3 Done via #84; B-165/W7-9 Done 2026-08-10; B-088/W9-1 Done;
  B-171/W9-9 Done via #129; B-173/W9-10 Done; B-177/W7-13 Done;
  B-184 catalogado em W9-11 — nav esquerda; B-095/W10-1 Done; B-096/W10-2 Done; B-099/W10-5 Done; B-167/B-185 catalogados em W11 —
  perfil e personalização visual; B-169 catalogado em W14 — gate auditoria↔E2EE;
  B-170 catalogado em W16 — performance/escalabilidade antes de HA;
  B-172 catalogado em W16 — backup de chat com tarefas e destinos remotos);
- specs em correspondência 1:1 com itens Planned;
- classes R0–R3 declaradas nas specs;
- nenhuma decisão D-* aberta;
- pacotes R3, release/support, catálogo de contratos/flags, manuais e métricas
  documentados no programa DOC-009…DOC-015;
- identidade, sync, projeções, lifecycle, anexos e SignalR HA formalizados em
  DOC-016…DOC-021;
- enforcement automático de DOC-006 ainda exige implementação na fase de código;
- B-153/B-154 adicionam migração/importação e diagnóstico seguro à Wave 11;
- safety lane Alta vazia (BUG-006 Done); BUG-002 aliviado para Média
  (fecha em B-094);
- três ações R4 do GitHub permanecem em `operational-findings.md`.

## Gaps documentais e operacionais conhecidos

| Gap | Situação em 2026-07-27 | Tratamento |
|-----|-------------------------|------------|
| `.env.example` contém chaves não injetadas pelo `compose.yaml` | **Fechado** (B-105) — EMAIL__* no api; retenção no worker; aliases documentados | B-105 Done |
| `.env.example` mistura infra com detalhe de integração (SMTP/IA) | **Fechado** — B-187: integração no `/admin` + DB; pins/portas/infra no template | B-187 Done |
| CSP ausente no proxy/web | **Fechado** — `infra/nginx/security-headers.conf` (B-077) | B-077 Done |
| Limite de mensagem só no banco | Registrado no roadmap | B-078 |
| Dependências sem bot de atualização | **Fechado** (#84) — Dependabot ativo; triagem em `dependencias.md` | B-076 Done |
| Cliente web preso em cache/PWA stale pós-deploy | **Fechado** — UX-007 / B-165 | W7-9 Done |
| `Moderation` é fronteira vazia | Assembly existe sem domínio material | Manter explícito; preencher só com feature autorizada |

## Decisões e limites vigentes

- Apache-2.0 e marca VibeChat estão decididas.
- Fase 1 permanece Compose; não adicionar K8s, bus externo ou OpenSearch sem os
  gatilhos dos ADRs 015–017.
- IA externa é opt-in e não entra no hot path de envio.
- E2EE opt-in (após B-169 / `contentAuditEnabled=false`), registry governado,
  voz/vídeo e documento colaborativo estão autorizados apenas nas Waves 15–17,
  com defaults restritivos e gates R3.
- Plugins locais seguem B-109 → B-110 → B-066 → B-111.
- Guests entram apenas pelo modelo restrito de D-07/B-040.
- Decisões técnicas reversíveis são autônomas; somente ações R4 externas param
  a etapa afetada.

## Como manter este snapshot

Atualizar ao fechar uma wave, mudar uma decisão D-*, aceitar/superseder um ADR
ou descobrir divergência material entre documentação e runtime. Toda afirmação
de entrega deve apontar para testes, código, migration, configuração ou artefato
operacional verificável.
