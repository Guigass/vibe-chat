# Catálogo de specs do harness — VibeChat

Índice das instruções versionadas que orientam **Cloud Agents**, automações Cursor,
regras do editor e seleção de trabalho. Não substitui ADRs de produto nem specs
`B-*` em `docs/product/specs/`.

**Hierarquia de autoridade** (em conflito, prevalece o item de cima):

1. Decisões D-* (`docs/roadmap/decisoes-pendentes.md`)
2. Contratos e segurança (`docs/architecture/contratos.md`, `docs/security/`)
3. Specs de produto `B-*` (`docs/product/specs/`)
4. Roadmap/backlog (`docs/roadmap/roadmap.md`, `horizonte-ambicioso.md`)
5. Git/PR/checks (estado factual da implementação)
6. Findings (`bug-findings.md`, `ux-findings.md`, `operational-findings.md`)
7. Memories das automações (cache/lease — nunca autoridade)

---

## Perfil ativo do pipeline

O repositório opera hoje no **perfil econômico pré-release** descrito em
[`docs/agents/autonomia.md`](../agents/autonomia.md) § Perfil temporário:

| Automação | Estado | Papel |
|-----------|--------|-------|
| Build (01) | Ativa (~12 h) | Implementa + testa + docs/status no **mesmo PR** |
| QA + Merge (02) | Ativa (PR opened) | CI-first; merge autônomo |
| Docs / Close (03) | **Inativa** | Fechamento incorporado ao Build |
| UX Review (04) | Inativa | Rodada humana/exploratória ao final |
| Security Review (05) | Inativa (CI + rodada final) | Check manual quando habilitado |
| Watchdog (06) | Ativa (diário) | Saúde + recovery determinístico |
| Harness Retrospective (07) | Inativa por padrão | Melhoria R0 do harness |
| PR Repair (08) | Inativa por padrão | Correção maker no PR reprovado |
| Readiness (00) | Manual | Gate de ativação read-only |

O perfil **24/7 completo** (Docs separado, Security/UX automáticos, Build a cada 2 h)
permanece documentado em [`go-live-2026-08-05.md`](../agents/go-live-2026-08-05.md) para
quando os gates externos (`OPS-QA-AUDIT`, `OPS-REQUIRED-CHECK`) estiverem resolvidos.

---

## Automações Cursor (prompts)

Arquivos em `.cursor/automations/*.prompt.md`. Copiar manualmente para
[cursor.com/automations](https://cursor.com/automations) — a API **não** sincroniza texto.

| Path | Título | O que manda fazer | Consumidor |
|------|--------|-------------------|------------|
| `.cursor/automations/00-readiness.prompt.md` | Readiness / Preflight | Snapshot read-only: `task agent:check`, gates externos, veredito PASS/BLOCKED/UNKNOWN | Automação 0 (manual); gate antes de schedules |
| `.cursor/automations/01-build.prompt.md` | Build / Melhorias | Safety lane → roadmap → manutenção; 1 Work-Item; PR ready; status `Done` no mesmo PR | Automação 1 (schedule 12 h); Cloud Agents ad hoc |
| `.cursor/automations/02-qa-merge.prompt.md` | QA + Merge | CI-first; revisão focada; squash merge; limpa lease | Automação 2 (PR opened) |
| `.cursor/automations/03-docs.prompt.md` | Docs / Close | Fecha roadmap pós-merge; dedup de PRs docs | Automação 3 (**inativa** — referência e recovery) |
| `.cursor/automations/04-ux-review.prompt.md` | UX Review | `task ux:stack`; checklist; registra `UX-*` | Automação 4 (inativa) |
| `.cursor/automations/05-security-review.prompt.md` | Security Review | Revisão read-only por superfície; check `VibeChat Security Review` | Automação 5 (inativa / manual) |
| `.cursor/automations/06-watchdog-recovery.prompt.md` | Watchdog / Recovery | Saúde diária; recovery PR determinístico | Automação 6 (09:00 BRT) |
| `.cursor/automations/07-harness-retrospective.prompt.md` | Harness Retrospective | Retrospectiva 7 dias; PR R0 do harness | Automação 7 (inativa) |
| `.cursor/automations/08-pr-repair.prompt.md` | PR Repair | Corrige PR reprovado (maker); ≤3 ciclos | Automação 8 (inativa) |
| `.cursor/automations/README.md` | — | Wiring, IDs dashboard, incidentes conhecidos, fluxo | Humanos + Retrospective |

Contrato transversal de loop: [`docs/agents/loop-engineering.md`](../agents/loop-engineering.md)
(`RUN_RESULT`, stop reasons, orçamentos).

---

## Regras Cursor (`.cursor/rules/`)

Aplicadas pelo editor a paths matching `globs`. `alwaysApply: true` vale em todo run.

| Path | Título | O que manda fazer | Consumidor |
|------|--------|-------------------|------------|
| `.cursor/rules/00-core.mdc` | Core | Núcleo: arquitetura, RLS, safety lane, roadmap, Docker | Todos os agentes (always) |
| `.cursor/rules/docker-runtime.mdc` | Docker Runtime | Compose/task only; proíbe Node/dotnet no host | Todos os agentes (always) |
| `.cursor/rules/automation-harness.mdc` | Automation Harness | Edição de `.cursor/`, hooks, prompts; `task agent:check` | Retrospective; edits no harness |
| `.cursor/rules/backend.mdc` | Backend | Módulos, TenantContext, outbox, Testcontainers | Build/Repair em `apps/api`, `modules/` |
| `.cursor/rules/frontend.mdc` | Frontend | Angular 22, design system, spartan/ui, E2E | Build/Repair em `apps/web/` |
| `.cursor/rules/infra.mdc` | Infra | Compose, profiles, `.env.example`, Keycloak | Build/Repair em `infra/`, Compose |
| `.cursor/rules/security.mdc` | Security | Multi-tenant, authZ, `task test:security` | Security Review; paths sensíveis |
| `.cursor/rules/testing.mdc` | Testing | Pirâmide de testes, comandos `task test:*` | Build, QA, Repair |

---

## Contratos centrais de agentes (`docs/agents/`)

| Path | Título | O que manda fazer | Consumidor |
|------|--------|-------------------|------------|
| `AGENTS.md` | Agentes (raiz) | Entrada universal; regras por trilha; Cloud caveats | Todos os prompts 00–08; regras always |
| `docs/agents/loop-engineering.md` | Loop Engineering | Loop fechado gather→stop; fontes de verdade; hooks | Todos os prompts; validate-harness |
| `docs/agents/autonomia.md` | Autonomia | Autoridade R0–R4; gates; perfil econômico; merge | Build, QA, Security, orientacoes |
| `docs/agents/operacao-24x7.md` | Operação 24/7 | Leases, concorrência, cadência, watchdog, evidência PR | Build, Watchdog, Readiness, go-live |
| `docs/agents/orientacoes.md` | Orientações | Checklists por trilha; coordenação Wave/Trilha | Build; agentes ad hoc |
| `docs/agents/go-live-2026-08-05.md` | Go-live harness | Ativação Readiness PASS; perfil 24/7 completo | Readiness (00); operadores |

---

## Roadmap e seleção de trabalho (`docs/roadmap/`)

| Path | Título | O que manda fazer | Consumidor |
|------|--------|-------------------|------------|
| `docs/roadmap/roadmap.md` | Roadmap W0–W10 | Ordem de execução; status; deps; GAPs | Build (scan W0→W10) |
| `docs/roadmap/horizonte-ambicioso.md` | Horizonte W11–W19 | W19 (org. código) recomendada antes de W11; W11–W18 | Build (scan pós-W10) |
| `docs/roadmap/backlog.md` | Backlog espelho | Resumo por wave; espelho de horizonte | Build, Docs |
| `docs/roadmap/estado-atual.md` | Estado atual | Snapshot operacional do programa | Build (contexto) |
| `docs/roadmap/decisoes-pendentes.md` | Decisões D-* | D-01…D-28 fechadas; não reabrir | Build, qualidade-documental |
| `docs/roadmap/operational-findings.md` | Findings OPS/SEC/HOTFIX | Safety lane operacional | Build, Watchdog, Readiness |
| `docs/roadmap/qualidade-documental.md` | Qualidade documental | Invariantes IDs/specs/links; contrato DOC-CHECK | Docs; Build (drift) |
| `docs/roadmap/riscos.md` | Riscos programa | R-* registrados | Contexto; não seleção direta |

**Ordem de scan do Build** (canônica):

```text
Safety lane → W0…W10 (roadmap.md) → W19 (horizonte) → W11…W18 (horizonte) → manutenção
```

Wave 19 é **recomendada** antes de W11, não obrigatória — itens W11+ só ultrapassam W19
elegível quando deps e scan order permitirem.

---

## Specs de produto (`docs/product/specs/B-*.md`)

Cada item `Planned` com ID `B-*` exige spec 1:1. Seções obrigatórias (ver
[`qualidade-documental.md`](../roadmap/qualidade-documental.md)):

Problema · Escopo · Fora de escopo · Contratos · UX · Multi-tenant e authZ · Aceite · Testes · Riscos

| Consumidor | Uso |
|------------|-----|
| Build (01) | Fonte de verdade do escopo; classe R0–R3; critérios testáveis |
| QA (02) | Verifica escopo vs diff; exige evidência nos gates da classe |
| Security (05) | Threat model e superfícies da feature |
| UX Review (04) | Feature com spec existente = backlog, não finding |

Lista completa: ~80+ arquivos `docs/product/specs/B-*.md` — indexados pelo ID no
roadmap/backlog, não duplicados aqui.

---

## Findings e checklists de produto

| Path | Título | O que manda fazer | Consumidor |
|------|--------|-------------------|------------|
| `docs/product/bug-findings.md` | Bugs funcionais | Safety lane `BUG-*` Alta | Build (Step A) |
| `docs/product/ux-findings.md` | Achados UX | Safety lane `UX-*` Alta; fila manutenção | Build; UX Review |
| `docs/product/ux-review-checklist.md` | Checklist UX | Percurso obrigatório; regras de finding | UX Review (04) |
| `docs/product/criterios-aceite-fatia-vertical.md` | Aceite fatia vertical | Critérios A1–A5 multi-tenant | QA; testing.mdc |

---

## Harness mecânico (hooks e ambiente)

| Path | Função | Consumidor |
|------|--------|------------|
| `.cursor/hooks.json` | Registra `beforeShellExecution` e `stop` | Cursor runtime |
| `.cursor/hooks/guard-shell.mjs` | Bloqueia force push, push em `main`, volumes, SQL destrutivo | Todo shell do agente |
| `.cursor/hooks/stop-check.mjs` | Checker ao encerrar; ≤1 follow-up corretivo | Fim de run |
| `.cursor/hooks/validate-harness.mjs` | Valida prompts, hooks, environment, referências | `task agent:check` |
| `.cursor/hooks/guard-shell.test.mjs` | Testes do guard (incl. BOM UTF-8) | `task agent:check` |
| `.cursor/environment.json` | `install`/`start` = `agent-setup.sh` | Cloud Agent VM |

Verificação:

```bash
task agent:check
```

---

## Documentação de arquitetura e segurança (leitura contextual)

Lidas **sob demanda** pelo Build/QA/Security conforme paths tocados — não carregar
inteiras “por segurança” ([`loop-engineering.md`](../agents/loop-engineering.md)):

| Área | Paths principais | Consumidor |
|------|------------------|------------|
| Arquitetura | `docs/architecture/visao-geral.md`, `contratos.md`, `diagrama-modulos.md`, `pacotes-decisao-r3.md` | Build, QA |
| ADRs | `docs/adrs/ADR-*.md` | Build (R3); Review |
| Segurança | `docs/security/multi-tenant.md`, `modelo-ameacas.md`, `ciclo-vida-dados.md` | Security (05); security.mdc |
| Design system | `docs/architecture/design-system.md` | Frontend; UX Review |
| Glossário | `docs/product/glossario.md` | Build (novos termos) |

---

## Skills e governança opcional

| Path | Uso |
|------|-----|
| `.cursor/skills/repo-governance-audit/SKILL.md` | Auditoria/atualização de governança do repo (não faz parte do pipeline 24/7) |

---

## Metadados obrigatórios de PR (automação)

Todo PR de automação deve incluir no corpo:

```text
Work-Item: <ID>
Wave: <W*-*|maintenance|docs|recovery>
Trilha: <A|B|C|D|E|F|G>
Deps satisfeitas: <ids ou —>
Automation: build|qa|docs|ux-review|watchdog|…
Risk: R0|R1|R2|R3
Lease: <run-id>
```

Encerramento de run:

```text
RUN_RESULT
Automation: <nome>
Result: …
Stop reason: GOAL_MET | …
Work-Item: …
Head-SHA: …
Evidence: …
Next safe action: …
```

---

## Manutenção deste índice

Atualizar `docs/specs/README.md` quando:

- novo prompt, regra ou contrato de agente entrar em `.cursor/` ou `docs/agents/`;
- perfil do pipeline mudar (ex.: reativar Docs ou Security automático);
- ordem de scan do roadmap mudar.

Não duplicar o conteúdo das specs — apenas indexar path, consumidor e contrato.
