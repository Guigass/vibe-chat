# Roadmap Executável — VibeChat

Roadmap para **times de agentes** trabalharem em paralelo. Cada item tem ID, dependências e trilha.

Snapshot resumido e fila corrente: [`estado-atual.md`](estado-atual.md). Portal da
documentação: [`docs/README.md`](../README.md).

## Legend

| Trilha | Foco |
|--------|------|
| A | Infra / Compose / DX |
| B | Backend Platform + Identity + Directory |
| C | Backend Messaging + Realtime + Worker |
| D | Frontend Angular |
| E | Security + QA |
| F | Observabilidade |
| G | Docs / Design system |

Dependências: itens só começam quando deps = done.

**Coluna `Status` é obrigatória em toda tabela de wave.** A automação de Build trata
linha sem `Status` como elegível — tabela sem a coluna faz o agente reabrir trabalho
já entregue (foi o que aconteceu com W3-2, reimplementado como `GAP-hub-t3` / #37).

---

## Wave 0 — Fundação (paralelo total)

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W0-1 | A | Compose: Postgres, Redis, Keycloak, MinIO | — | **Done** |
| W0-2 | A | Scripts seed-dev + .env.example | W0-1 | **Done** |
| W0-3 | B | Skeleton solução .NET 10 (Api, Worker, Contracts, Platform) | — | **Done** |
| W0-4 | D | Skeleton Angular 22 standalone + tokens CSS | — | **Done** |
| W0-5 | G | Design system tokens aplicados no web | W0-4 | **Done** |
| W0-6 | F | OTel collector + Prometheus + Grafana + Loki + Tempo no Compose | W0-1 | **Done** — profile `observability` |
| W0-7 | E | Pipeline CI: build + unit vazio + arch test stub | W0-3 | **Done** — `.github/workflows/ci.yml` |

## Wave 1 — Identidade e Directory

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W1-1 | B | OIDC validation + TenantContext middleware | W0-3, W0-1 | **Done** |
| W1-2 | B | Módulo Directory: Tenant, Workspace, Space, Channel, Membership | W1-1 | **Done** |
| W1-3 | B | Seed dados acme + alice/bob | W1-2, W0-2 | **Done** |
| W1-4 | D | Login OIDC PKCE + guard de rotas | W0-4, W1-1 | **Done** |
| W1-5 | E | Testes auth negativos (401/403) | W1-1 | **Done** |

## Wave 2 — Fatia de mensagem

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W2-1 | C | Conversation/Message/seq/idempotency + migrations | W1-2 | **Done** |
| W2-2 | C | Outbox writer + worker processor | W2-1, W0-3 | **Done** |
| W2-3 | C | SignalR hub + Redis backplane config | W2-2, W1-1 | **Done** |
| W2-4 | C | History API + gap model | W2-1 | **Done** |
| W2-5 | D | UI channel: lista + composer + hub client | W1-4, W2-3, W2-4 | **Done** |
| W2-6 | E | Testes integração send+idempotency+seq | W2-1 | **Done** — `tests/integration` |
| W2-7 | E | E2E Playwright dois usuários | W2-5, W1-3 | **Done** — `tests/e2e/specs`; na CI via W7-1 |

## Wave 3 — Hardening multi-tenant

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W3-1 | B | RLS policies em tabelas de negócio | W2-1, W1-2 | **Done** — B-009 + GAP-rls-retention (#35) + GAP-rls-catalog (#36) |
| W3-2 | E | Suíte tests/security cross-tenant (API+hub) | W3-1, W2-3 | **Done** — hub T3 via GAP-hub-t3 (#37) |
| W3-3 | C | Rate-limit Redis em send/hub | W2-3 | **Done** |
| W3-4 | F | Dashboards: requests, outbox lag, SignalR | W0-6, W2-2 | **Done** |
| W3-5 | E | Critérios de aceite fatia — sign-off | W2-7, W3-2 | **Done** — A1…A6 marcados em `criterios-aceite-fatia-vertical.md` |

Hardening posterior a esta wave está no **Registro de GAPs** no fim do documento.

## Wave 4 — Extensões pós-fatia (paralelizável)

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W4-0 | G | Fechar D-01…D-10 + ADR-018 retenção | — | **Done** |
| W4-0b | C/D | Editar / soft-delete (B-023) + DMs 1:1 (B-021) | W3-5 | **Done** |
| W4-1 | C/D | Threads | W3-5 | **Done** |
| W4-2 | C/D | Anexos MinIO | W3-5, W0-1 | **Done** |
| W4-3 | C/D | Presence + typing polidos | W3-5 | **Done** |
| W4-3b | B/D | Spaces UI + criar channel (B-020) | W3-5 | **Done** |
| W4-4 | C | Search FTS Postgres | W3-5 | **Done** |
| W4-5 | C | AI NoOp + OpenRouter adapter + flag | W3-5 | **Done** — Mock default em lab; `Ai:Enabled=false` prod; OpenRouter opt-in+key; summarize authZ/503; fora do hot path SendMessage (D-06) |
| W4-6 | B | Audit log admin | W3-5 | **Done** — `GET /admin/audit-events` + UI admin + authZ `admin.dashboard` |
| W4-7 | D | PWA installability + offline shell (B-029) | W3-5 | **Done** |
| W4-8 | C/D | Reações (B-024) | W3-5 | **Done** |

## Wave 5 — Produção self-host

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W5-1 | A | Backup automatizado + drill doc validado | W3-5 | **Done** — scripts + doc |
| W5-2 | A | TLS / proxy reference config | W3-5 | **Done** — nginx Compose profile `proxy` + certs script |
| W5-3 | E | Load smoke k6 | W3-5 | **Done** — `tests/load/smoke.js` + `task load:smoke` |
| W5-4 | G | Runbooks finais ops | W5-1 | **Done** — `docs/operations/runbooks/` (incidentes, backup/restore, TLS/proxy, upgrade) |

## P2 — Admin / diferenciação

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| P2-1 | B/D | Papéis granulares (B-041) | Wave 5, D-07 | **Done** — `PUT .../members/{userId}/role` + UI admin + authZ `workspace.admin` |
| P2-2 | A/B | Notificações email SMTP (B-043) | D-10, P2-1 | **Done** — Null/SMTP; off default; e-mail role-change via outbox |

## Wave 6 — Refinamento UX + Admin

Wave 6 entregue (B-068/B-069/B-067/B-073/B-074) + B-048 webhooks + B-045 suggest-reply + B-046 export + B-047 retenção/purge. Trabalho corrente: **Wave 7 — Sustentação**, depois **Waves 8–10 — Paridade de mensageria**.

| ID | Trilha | Tarefa | Deps | Status |
|----|--------|--------|------|--------|
| W6-1 | C/D | Realtime estável: MessageCreated/edit/delete/reações + gap-fill reconnect (B-070) | Wave 5 | **Done** |
| W6-2 | C/D | Typing: não mostrar indicador para o próprio usuário (B-071) | W6-1 ou paralelo | **Done** — filtro client + E2E |
| W6-3 | D | Layout: scroll só no container da conversa (B-072) | — | **Done** — shell sem scroll de página |
| W6-4 | B/D/G | Cadastro de usuário + diretivas documentados e fluxo admin (B-068) | P2-1 | **Done** — invite admin + glossário |
| W6-5 | B/D/E | Settings sensíveis (tokens, webhooks, AI/SMTP) só admin (B-069, B-048) | W6-4 | **Done** — B-069 settings mascarados; B-048 webhooks `MessageCreated` + HMAC |
| W6-6 | B/D/E | Auditoria completa de conversas no ADMIN (B-067) | W4-6, W6-4 | **Done** — viewer admin canal/DM/thread + body soft-delete; authZ `admin.dashboard` |
| W6-7 | D/G | UI polish com PrimeNG + tema tokens (B-073; emenda ADR-002) | W6-3 | **Done** — histórico; **superseded por D-15 / B-104** (sair do PrimeNG) |
| W6-8 | A/G | API + Web (+ Worker) containerizados no Compose como caminho oficial (B-074) | W5-2 | **Done** — `task apps` + healthchecks; OIDC Authority/MetadataAddress; ops/README |

### Critérios de aceite Wave 6 (resumo)

- Dois usuários: mensagem/edit/delete/reação aparecem sem F5; reconnect preenche lacunas de `seq`
- Autor de typing não vê o próprio “digitando…”
- Página do shell não rola inteira; só a timeline
- Docs/glossário deixam claro: Keycloak autentica; membership/diretivas autorizam
- Tokens/webhooks/keys inacessíveis a não-admin
- Admin consegue auditar conversa (não só `audit_events`)
- Identidade visual VibeChat preservada (tokens); saída do PrimeNG (D-15) + kit OSS spartan/ui (D-27) executados em B-104
- `task apps` (Compose profile `apps`) sobe **api** + **web** (+ worker) healthy; caminho self-host documentado (dev hot-reload continua via `task dev`)

## Wave 7 — Sustentação

Fila explícita para a automação de Build depois da Wave 6. Enquanto houver linha
`Planned` aqui, o Build pega dela. **W7-6 (B-104) Done** via [#82](https://github.com/Guigass/vibe-chat/pull/82)
(spartan/ui + CDK; fecha UX-002). **W7-3 (B-076) Done** via [#84](https://github.com/Guigass/vibe-chat/pull/84)
(Dependabot + `dependencias.md`). **W7-4 (CSP) Done**; **W7-5 (limite de body) Done**;
**W7-8 (admin shell) Done**. Próximo elegível na Wave 7: W8 (paridade composição).

| ID | Trilha | Tarefa | Deps | Spec/evidência | Status |
|----|--------|--------|------|----------------|--------|
| W7-1 | E | E2E Playwright na CI (B-075) | W2-7, W6-8 | `infra/scripts/ci-e2e.sh`; `task test:e2e:ci`; #45 | **Done** |
| W7-6 | D/G | Remover PrimeNG — spartan/ui + CDK; fecha UX-002 (B-104; D-15; D-27; emenda ADR-002) | W6-7, D-15, D-27 | [B-104](../product/specs/B-104-remover-primeng.md); #82 | **Done** |
| W7-2 | B/D | Guests / link de canal (B-040) | P2-1, D-07 | [B-040](../product/specs/B-040-guests-por-convite.md) | **Moved** — W10-10 |
| W7-3 | E/A | Atualização automatizada de dependências (Dependabot) | W0-7 | [B-076](../product/specs/B-076-atualizacao-dependencias.md); #84 | **Done** |
| W7-4 | D/E | CSP no web; headers básicos existem, CSP não | W6-8 | [B-077](../product/specs/B-077-csp-web.md) | Done |
| W7-5 | C/E | Limite de tamanho de body no envio; limite atual existe só na coluna | W2-1 | [B-078](../product/specs/B-078-limite-body-mensagem.md) | Done |
| W7-7 | A/G | Catálogo de configuração admin mínima no `.env`; Compose/template alinhados | W6-8, W0-2 | [B-105](../product/specs/B-105-catalogo-configuracao.md) | Done |
| W7-8 | D/G | Admin shell — nav, toolbars, listagens, filtros e hide por papel; após saída do PrimeNG | W7-6 | [B-106](../product/specs/B-106-admin-shell.md) | Done |

### Critérios de aceite W7-7 (resumo)

- `.env.example` cobre **100%** das variáveis exigidas para `task apps` em produção (data plane + api + web + worker + proxy opcional)
- Cada variável tem: descrição, serviço afetado, default, obrigatoriedade em prod, e se é secret (`CHANGE_ME` / `*_change_me`)
- Matriz **env vs admin UI** documentada: o que só o operador de infra mexe no `.env` vs o que o `workspace.admin` mexe em `/admin/settings`
- Gaps entre `appsettings*.json`, `compose.yaml` e `.env.example` fechados ou listados como follow-up explícito
- `docs/operations/operacao.md` aponta para o catálogo como fonte da verdade de configuração

### Critérios de aceite W7-8 (resumo)

- `/admin` tem menu lateral por área + toolbar contextual + rotas filhas
- Listagens (membros, audit, conversas) com busca/filtro útil e empty states
- **Visibilidade:** Auditor não vê Settings/Export/convidar; Admin vê tudo; Member fora
- Sem banners “Sem permissão…” (UX-005 fechado); deep-link sem claim → área permitida
- AuthZ de API intacta (`workspace.admin` / `admin.dashboard`); tokens `--vc-*`
- Sem lib de UI comercial; B-104 já mergeado (sem PrimeNG)

### Modo manutenção (sem item `Planned`)

Quando **toda** linha de wave estiver `Done` ou `Blocked`, o Build entra no Step B do
`01-build.prompt.md`: uma lacuna pequena por run, com ID `GAP-<curto>`. Isso é
esperado, não é falha — mas o resultado tem que aparecer no **Registro de GAPs**
abaixo, senão o trabalho fica invisível no roadmap.

## Programa documental transversal

Este programa não altera a ordem do Build e não autoriza código. Ele mantém o
roadmap executável e reduz ambiguidade antes das próximas waves.

| ID | Entregável | Status |
|----|------------|--------|
| DOC-001 | Portal canônico `docs/README.md` e precedência entre fontes | **Done (2026-07-27)** |
| DOC-002 | Snapshot factual `roadmap/estado-atual.md` | **Done (2026-07-27)** |
| DOC-003 | Specs completas para todos os itens Planned da Wave 7 | **Done (2026-07-27)** |
| DOC-004 | Reconciliar mapa de módulos com assemblies existentes | **Done (2026-07-27)** |
| DOC-005 | Reconciliar checklists de segurança com Done/Planned | **Done (2026-07-27)** |
| DOC-006 | Contrato de auditoria automatizada de links/IDs/specs | **Done (2026-07-27)** — baseline 79/79 em [`qualidade-documental.md`](qualidade-documental.md); enforcement rastreado em OPS-DOC-CHECKER |
| DOC-007 | Matriz de configuração e evidência de `docker compose config` | **Done (2026-07-27)** — parse válido e gaps executáveis preservados em B-105 |
| DOC-008 | Revisão ADR por ADR contra implementação atual | **Done (2026-07-27)** — `architecture/aderencia-adrs.md` |
| DOC-009 | Classe R0–R3 declarada em todas as specs W7–W17 | **Done (2026-07-27)** — 79/79 |
| DOC-010 | Rastreabilidade de todos os findings UX abertos | **Done (2026-07-27)** — B-ID, B-103 ou safety lane |
| DOC-011 | Pacotes de decisão para capacidades R3 | **Done (2026-07-27)** — [`architecture/pacotes-decisao-r3.md`](../architecture/pacotes-decisao-r3.md) |
| DOC-012 | Política de release, versionamento e suporte | **Done (2026-07-27)** — [`operations/release-versionamento-suporte.md`](../operations/release-versionamento-suporte.md) |
| DOC-013 | Catálogo futuro de contratos, dados e flags | **Done (2026-07-27)** — [`architecture/catalogo-evolucao-contratos.md`](../architecture/catalogo-evolucao-contratos.md) |
| DOC-014 | Guias de colaborador, admin, integração e operador | **Done (2026-07-27)** — [`product/guias/`](../product/guias/) |
| DOC-015 | Métricas e evidence bundles por wave | **Done (2026-07-27)** — [`product/metricas-e-evidencias.md`](../product/metricas-e-evidencias.md) |
| DOC-016 | Modelo canônico de identidade, principals, devices e memberships | **Done (2026-07-27)** — [`architecture/modelo-identidade-principals.md`](../architecture/modelo-identidade-principals.md) |
| DOC-017 | Protocolo de mensagens, realtime e sincronização | **Done (2026-07-27)** — [`architecture/protocolo-sync-realtime.md`](../architecture/protocolo-sync-realtime.md) |
| DOC-018 | Separação entre estado, outbox, audit e projeções | **Done (2026-07-27)** — [`architecture/estado-eventos-auditoria-projecoes.md`](../architecture/estado-eventos-auditoria-projecoes.md) |
| DOC-019 | Matriz transversal de ciclo de vida dos dados | **Done (2026-07-27)** — [`security/ciclo-vida-dados.md`](../security/ciclo-vida-dados.md) |
| DOC-020 | State machine e pipeline canônico de anexos | **Done (2026-07-27)** — [`architecture/pipeline-anexos.md`](../architecture/pipeline-anexos.md) |
| DOC-021 | Contrato SignalR multi-instância e HA | **Done (2026-07-27)** — [`architecture/signalr-ha.md`](../architecture/signalr-ha.md) |

---

## Waves 8–10 — Paridade de mensageria

Escopo autorizado por **D-11**. A base factual é
`docs/product/benchmark-mensageria.md` (comparação com Slack, Teams, Discord e
WhatsApp) e **cada item tem spec** em `docs/product/specs/`.

**Regra:** item sem spec não é elegível para o Build. A coluna Spec é obrigatória.

Ordem sugerida: 8 → 9 → 10. Dentro da wave, a numeração é a ordem preferida, mas itens
sem dependência entre si podem ir em paralelo por trilhas diferentes.

### Wave 8 — Composição de mensagem

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| W8-1 | C/D | Anexos múltiplos, drag & drop, colar e progresso (B-079) | B-025 | [B-079](../product/specs/B-079-anexos-multiplos-drag-drop.md) | Done |
| W8-2 | C/D | Mensagem de áudio (B-080) | W8-1, D-12 | [B-080](../product/specs/B-080-mensagem-de-audio.md) | Done |
| W8-3 | C/D | Formatação de texto (B-081) | — | [B-081](../product/specs/B-081-formatacao-de-texto.md) | Done |
| W8-4 | B/C/D | Menções (B-082) | W8-3 | [B-082](../product/specs/B-082-mencoes.md) | Done |
| W8-5 | C/D | Emoji picker e reações livres (B-083) | B-024 | [B-083](../product/specs/B-083-emoji-e-reacoes-livres.md) | Planned |
| W8-6 | C/D | Responder citando (B-084) | — | [B-084](../product/specs/B-084-responder-citando.md) | Planned |
| W8-7 | C/D | Encaminhar mensagem (B-085) | W8-6 | [B-085](../product/specs/B-085-encaminhar-mensagem.md) | Planned |
| W8-8 | D | Rascunho persistente (B-086) | — | [B-086](../product/specs/B-086-rascunho-persistente.md) | Planned |
| W8-9 | C/D | Comandos slash (B-087) | W8-4 | [B-087](../product/specs/B-087-comandos-slash.md) | Planned |

### Wave 9 — Leitura da timeline

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| W9-1 | D | Agrupamento, separadores e não lidas (B-088) | — | [B-088](../product/specs/B-088-timeline-agrupamento-separadores.md) | Planned |
| W9-2 | C/D | Histórico paginado e pular para a mensagem (B-089) | W9-1 | [B-089](../product/specs/B-089-historico-paginado.md) | Planned |
| W9-3 | C/D | Preview de anexos (B-090) | W8-1 | [B-090](../product/specs/B-090-preview-de-anexos.md) | Planned |
| W9-4 | C/D | Link preview (B-091) | — | [B-091](../product/specs/B-091-link-preview.md) | Planned |
| W9-5 | C/D | Fixar mensagem (B-092) | W9-2 | [B-092](../product/specs/B-092-fixar-mensagem.md) | Planned |
| W9-6 | C/D | Salvos (B-093) | W9-2 | [B-093](../product/specs/B-093-salvos.md) | Planned |
| W9-7 | C/D | Recibos de leitura e não lidas persistentes (B-094) | W9-1 | [B-094](../product/specs/B-094-recibos-de-leitura.md) | Planned |

### Wave 10 — Notificações, organização e acesso

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| W10-1 | B/C/D | Web Push (B-095) | W8-4, W9-7, D-13 | [B-095](../product/specs/B-095-web-push.md) | Planned |
| W10-2 | C/D | Enquetes (B-096) | — | [B-096](../product/specs/B-096-enquetes.md) | Planned |
| W10-3 | B/D | Preferências de notificação e DND (B-097) | W10-1 | [B-097](../product/specs/B-097-preferencias-notificacao-dnd.md) | Planned |
| W10-4 | C/D | Busca com filtros (B-098) | W9-2 | [B-098](../product/specs/B-098-busca-com-filtros.md) | Planned |
| W10-5 | D | Paleta de comandos e atalhos (B-099) | W8-9, W10-4 | [B-099](../product/specs/B-099-paleta-de-comandos.md) | Planned |
| W10-6 | D/G | Internacionalização (B-100) | D-14 | [B-100](../product/specs/B-100-i18n.md) | Planned |
| W10-7 | B/C/D | DM em grupo (B-101) | B-021 | [B-101](../product/specs/B-101-dm-em-grupo.md) | Planned |
| W10-8 | C/D | Seguir thread (B-102) | B-022, W10-1 | [B-102](../product/specs/B-102-seguir-thread.md) | Planned |
| W10-9 | D/E | Acessibilidade WCAG 2.2 AA (B-103) | W10-5 | [B-103](../product/specs/B-103-acessibilidade.md) | Planned |
| W10-10 | B/D/E | Guests por convite (B-040) | P2-1, W10-4, D-07 | [B-040](../product/specs/B-040-guests-por-convite.md) | Planned |
| W10-11 | B/C/D | Políticas de edição/apagar mensagem (B-107) | B-023, B-069 | [B-107](../product/specs/B-107-politicas-edicao-mensagem.md) | Planned |
| W10-12 | B/C/D | Extender webhooks outbound (B-108) | B-048, B-069 | [B-108](../product/specs/B-108-extender-webhooks.md) | Planned |
| W10-13 | B/C/D/E | Núcleo plugin — bot/token + envio msgs (B-109) | B-004, B-069, B-021 | [B-109](../product/specs/B-109-api-integracao-envio-mensagens.md) | Planned |
| W10-14 | B/C/D | Instalar/gerir plugins na instância (B-110) | W10-13 | [B-110](../product/specs/B-110-instalar-plugins.md) | Planned |

### Itens de maior risco nestas waves

| Item | Risco | Controle exigido |
|------|-------|------------------|
| W9-4 / B-091 | SSRF — o servidor passa a buscar URL fornecida pelo usuário | Allowlist de esquema, recusa de IP privado/loopback/link-local/metadata **após cada redirect**, timeout, limite de corpo, cache por tenant |
| W10-10 / B-040 | Escalada de guest para o workspace | Suíte negativa cobrindo **todos** os endpoints de workspace, não uma amostra; membership de canal, nunca de workspace |
| W10-7 / B-101 | Vazamento de histórico ao adicionar participante | Janela de visibilidade por `seq` de entrada, com teste dedicado |
| W10-13 / B-109 | Token de integração exfiltrado / bot sem escopo | Token só hash no DB; escopo explícito de canais; rate-limit; suíte security cross-tenant |
| W10-14 / B-110 | Plugin confundido com runtime de código | Manifesto = config; sem carregar DLL/JS de terceiro; capabilities extras só em B-066 |

### Fora de escopo das Waves 7–10 (D-11)

Chamada de voz/vídeo ao vivo e screen share; superfície de documento colaborativo
(Canvas/Loop); registry remoto; E2EE. Não antecipar nessas waves: essas
capacidades foram decididas e ordenadas somente para W15–W17.

**Trilha de plugins locais** (permitida): B-109 (núcleo) → B-108 (outbound) →
B-110 (install) → **B-066 / B-111 em W15** (capabilities avançadas) →
B-136/B-137 (SDK e registry governado).

## Depois da Wave 10

O roadmap executável posterior está em
[`horizonte-ambicioso.md`](horizonte-ambicioso.md). Waves 11–17 estão `Planned`,
possuem specs e são elegíveis para o Build somente depois da conclusão das
Waves 7–10 e de suas dependências. D-16…D-28 registram os defaults já decididos;
`docs/agents/autonomia.md` governa decisões técnicas e risco.

A visão de produto correspondente está em
[`product/visao-longo-prazo.md`](../product/visao-longo-prazo.md).

## Registro de GAPs

Lacunas fechadas fora das linhas de wave. Uma linha por `GAP-*`; a automação de Docs
escreve aqui em vez de espalhar notas soltas pelas seções.

| GAP | Trilha | O que fechou | PR |
|-----|--------|--------------|-----|
| `GAP-rls-retention` | E | RLS em `messaging.message_retention_settings` | #35 |
| `GAP-rls-catalog` | E | RLS nas tabelas com `TenantId` que faltavam + arch test de catálogo | #36 |
| `GAP-hub-t3` | E | Caso T3: `JoinChannel`/`SendTyping` cross-tenant em `tests/security` | #37 |
| `GAP-agent-docker` | A | `ensure_docker()` no `agent-setup.sh` (Docker em VMs de agente; log seguro + pacotes) | #41, #42, #43 |
| `GAP-test-local-stack` | E | Suítes deixam de vazar estado quando o data plane local é reaproveitado | #44 |
| `GAP-signalr-groups` | C | Grupos SignalR `t:{tenantId}:c:{channelId}` e `t:{tenantId}` (presence) | #47 |
| `GAP-redis-keys` | C | Keys de presence/typing/rate-limit com prefixo `t:{tenantId}:` | #49 |
| `GAP-web-node-boot` | A | `BOOT_ONLY` / `task ux:stack` + `ensure_web_node` com piso Angular CLI (`^22.22.3 \|\| ^24.15 \|\| >=26`) | #61 |

---

## Parallelismo sugerido por time de agentes

```text
Agent-Infra     → W0-1, W0-2, W0-6, W5-*, W6-8, W7-7
Agent-Backend   → W0-3, W1-*, W2-1..W2-4, W3-1, W3-3, W4-*, W6-1, W6-2, W6-4..W6-6,
                  W8-4, W9-4, W10-1, W10-7, W10-11, W10-12, W10-13, W10-14
Agent-Frontend  → W0-4, W0-5, W1-4, W2-5, W4-7, W6-1..W6-3, W6-7, W7-6, W7-8,
                  W8-1..W8-3, W8-5..W8-8, W9-1..W9-3, W9-7, W10-5, W10-6
Agent-QA        → W0-7, W1-5, W2-6, W2-7, W3-2, W3-5, W5-3, W6-1 E2E, W6-8 smoke, W7-1
Agent-Security  → W3-1/W3-2 review, W6-5/W6-6 authZ + threat model, W7-3..W7-5,
                  W9-4 (SSRF), W10-9, W10-10 (guests), W10-13 (integration tokens),
                  W10-14 (plugins)
Agent-Obs       → W0-6, W3-4
```

Após W10, a atribuição vem da trilha da própria linha em
`horizonte-ambicioso.md`. Até três PRs podem correr em paralelo quando não há
dependência nem sobreposição material; R2/R3 sempre recebem QA/Security
independente. A ordem W11→W17 prevalece sobre afinidade do agente.

## Definição de “Wave 3 completa”

Fatia vertical aceita (`criterios-aceite-fatia-vertical.md`) + RLS testado + dashboards mínimos.
