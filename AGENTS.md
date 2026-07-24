# AGENTS.md — VibeChat

Regras para Cloud Agents e agentes de código que trabalham neste repositório.
Complementa `docs/agents/orientacoes.md`.

## Antes de editar

1. Ler `docs/architecture/` (especialmente `visao-geral.md`, `contratos.md`, `diagrama-modulos.md`) e os ADRs relevantes em `docs/adrs/`.
2. Identificar os módulos afetados (`modules/*`, `apps/*`, `src/*`, `infra/*`, `tests/*`).
3. Planejar mudanças grandes antes de codar (escopo, contratos, testes, docs).
4. Consultar `docs/product/glossario.md` e `docs/roadmap/decisoes-pendentes.md`.

## Regras universais

- **Não alterar contratos compartilhados em silêncio** — mudanças em APIs públicas, eventos, schemas ou claims exigem atualização de `docs/architecture/contratos.md` e testes.
- **Não mudar arquitetura sem ADR** — novos serviços, buses, OpenSearch, K8s, etc. exigem ADR aprovado/documentado.
- **Rodar testes relevantes** da trilha tocada (`task test`, `task test:architecture`, etc.).
- **Adicionar testes para correções** — bugfix sem regressão coberta não está pronto.
- **Preservar isolamento multi-tenant** — `tenant_id`, authZ e RLS em todo caminho de dado de negócio.
- **Sem dependências proprietárias** — preferir OSS; não adicionar SDKs fechados sem decisão explícita.
- **Sem secrets em logs ou commits** — usar `.env.example` com placeholders; nunca credenciais reais.
- **Atualizar documentação** quando comportamento, DX ou decisão mudar.
- **PRs pequenos e revisáveis** — uma intenção clara por mudança.
- **Apresentar evidência de funcionamento** — saída de `task verify` / testes, screenshots ou traces quando fizer sentido.

## Coordenação

Declarar no PR/commit quando aplicável:

```text
Wave: WX-Y
Trilha: A|B|C|…
Deps satisfeitas: …
```

Se bloqueado por decisão humana (D-*), parar e documentar — não inventar licença, marca ou retenção.

## Backend

- Trabalhar dentro das fronteiras de módulo; composition root só em `apps/api` e `apps/worker`.
- Setar `TenantContext` / `app.tenant_id` em toda unit of work.
- Mutações de mensagem: idempotência + `seq` + outbox.
- Não publicar SignalR direto sem caminho de outbox para eventos duráveis.
- Não chamar provedores de IA de forma síncrona no hot path de `SendMessage`.
- Testes de integração com Testcontainers quando tocar persistência.
- Checklist: contratos, migration/RLS, testes de messaging, arch tests verdes.

## Frontend

- Angular 22 standalone + Signals + CDK; tokens do `docs/architecture/design-system.md`.
- OIDC PKCE; ordenar mensagens por `seq`; Idempotency-Key estável por envio.
- Light/dark via `data-theme`; motion sutil (2–3), sem poluição visual.
- Não clonar visual Slack/Discord/WhatsApp; sem cards desnecessários no shell.
- Tratar reconnect SignalR, empty/error states e a11y básica.

## Infra

- Compose reproduzível com healthchecks e volumes nomeados.
- Scripts idempotentes em `infra/scripts`; usar `task setup` / `task dev`.
- `.env.example` completo; sem secrets reais.
- Realm Keycloak versionado; profiles opcionais (`tools`, `observability`, `apps`).
- Sem Helm/K8s como requisito da fase 1; não expor Postgres/Redis/MinIO publicamente em referências de prod.

## QA

- Cobrir `docs/product/criterios-aceite-fatia-vertical.md`.
- Priorizar testes de segurança cross-tenant e E2E de duas sessões (`tests/e2e`).
- Não skippar flaky sem issue; fatia vertical sem isolamento multi-tenant não passa.
- Reportar evidências (logs, trace ids, artefatos CI).

## Security

- Revisar PRs com checklist de `docs/security/multi-tenant.md`.
- Manter `docs/security/modelo-ameacas.md` quando a superfície mudar.
- Validar headers, rate-limit, uploads, hub authZ.
- Negar features que enviem PII a IA sem flag + docs.
- Não aprovar bypass temporário de RLS em main.

## Review

Olhar primeiro:

1. Viola ADR-001 / ADR-009 / ADR-010?
2. Há caminho cross-tenant?
3. Outbox / idempotency corretos?
4. UI foge do design system?
5. Scope creep de infra?
6. Docs/glossário desatualizados?

Comentários úteis apontam arquivo + regra, sugerem teste que falharia e diferenciam blocker vs nit.

## Definition of Done

- Código compila; testes da trilha passam
- Docs atualizadas se comportamento/ADR mudou
- Sem secrets
- Escopo limitado à tarefa
- Evidência de verificação anexada ou citada no PR
