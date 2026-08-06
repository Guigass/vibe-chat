# Estado Atual do VibeChat

Snapshot factual para orientação rápida. Não substitui o
[roadmap executável](roadmap.md), o [backlog](backlog.md) nem evidência de testes.

- **Data de corte:** 2026-08-06
- **Fase:** Wave 7 — Sustentação
- **Safety lane obrigatória:** nenhuma Alta aberta (UX-001/#74, UX-002/#82, UX-003/#80 Done)
- **Próximo item elegível:** W7-4 / B-077 — CSP no web
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

1. fechar hardening restante (CSP e validação de body);
2. consolidar o contrato de configuração self-host;
3. reconstruir o shell administrativo (B-106; depende de B-104 Done);
4. avançar as Waves 8–10 de paridade de mensageria;
5. consumir o roadmap autorizado W11–W17.

## Runtime e fronteiras

| Área | Estado observado | Fonte principal |
|------|-----------------|-----------------|
| API | ASP.NET Core .NET 10; composition root em `apps/api` | `apps/api/Program.cs` |
| Worker | Outbox/jobs e purge de retenção | `apps/worker/Program.cs` |
| Web | Angular 22 standalone, Signals, CDK, PWA | `apps/web/package.json`, `apps/web/src/` |
| Dados | PostgreSQL 16 + migrations + RLS | `compose.yaml`, `src/VibeChat.Infrastructure/Persistence/` |
| Tempo real | SignalR + Redis backplane | módulo Realtime + infraestrutura |
| Arquivos | MinIO/S3-compatible | módulo Files + Compose |
| Identidade | Keycloak OIDC; DevAuth apenas em Development | realm versionado + API |
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
| Identity | Perfis e integração de identidade |
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
| 1 | W7-4 / B-077 | Completa headers com CSP |
| 2 | W7-5 / B-078 | Evita body acima do limite virar erro 500 |
| 3 | W7-7 / B-105 | Torna configuração self-host explícita e auditável |
| 4 | W7-8 / B-106 | Consolida navegação e visibilidade do console admin |
| 5 | Waves 8–10 | Paridade de composição, leitura, notificação e acesso |

W7-4, W7-5 e W7-7 podem avançar em paralelo quando houver agentes por
trilha. W7-8 depende de W7-6 (**Done** via #82). W7-3 / B-076 **Done** via #84.

O horizonte pós-Wave 10 já foi promovido a roadmap executável W11–W17. Ele não
altera a prioridade imediata: o Build só o consome depois de W7–W10 `Done`.

## Baseline de planejamento

- 79 itens `Planned` entre W7–W17 (B-104/W7-6 Done via #82; B-076/W7-3 Done via #84);
- 81 specs em correspondência 1:1;
- 81 classes R0–R3 declaradas;
- nenhuma decisão D-* aberta;
- pacotes R3, release/support, catálogo de contratos/flags, manuais e métricas
  documentados no programa DOC-009…DOC-015;
- identidade, sync, projeções, lifecycle, anexos e SignalR HA formalizados em
  DOC-016…DOC-021;
- enforcement automático de DOC-006 ainda exige implementação na fase de código;
- B-153/B-154 adicionam migração/importação e diagnóstico seguro à Wave 11;
- três ações R4 do GitHub permanecem em `operational-findings.md`.

## Gaps documentais e operacionais conhecidos

| Gap | Situação em 2026-07-27 | Tratamento |
|-----|-------------------------|------------|
| `.env.example` contém chaves não injetadas pelo `compose.yaml` | Confirmado para e-mail, retenção e aliases legados | B-105; ver `operations/configuracao-env.md` |
| CSP ausente no proxy/web | Registrado no roadmap e threat model | B-077 |
| Limite de mensagem só no banco | Registrado no roadmap | B-078 |
| Dependências sem bot de atualização | **Fechado** (#84) — Dependabot ativo; triagem em `dependencias.md` | B-076 Done |
| `Moderation` é fronteira vazia | Assembly existe sem domínio material | Manter explícito; preencher só com feature autorizada |

## Decisões e limites vigentes

- Apache-2.0 e marca VibeChat estão decididas.
- Fase 1 permanece Compose; não adicionar K8s, bus externo ou OpenSearch sem os
  gatilhos dos ADRs 015–017.
- IA externa é opt-in e não entra no hot path de envio.
- E2EE opt-in, registry governado, voz/vídeo e documento colaborativo estão
  autorizados apenas nas Waves 15–17, com defaults restritivos e gates R3.
- Plugins locais seguem B-109 → B-110 → B-066 → B-111.
- Guests entram apenas pelo modelo restrito de D-07/B-040.
- Decisões técnicas reversíveis são autônomas; somente ações R4 externas param
  a etapa afetada.

## Como manter este snapshot

Atualizar ao fechar uma wave, mudar uma decisão D-*, aceitar/superseder um ADR
ou descobrir divergência material entre documentação e runtime. Toda afirmação
de entrega deve apontar para testes, código, migration, configuração ou artefato
operacional verificável.
