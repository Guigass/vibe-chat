# Orientações para Agentes — VibeChat

Este documento guia agentes de código (backend, frontend, infra, QA, security, review) para manter consistência com a fundação.

> Contrato operacional principal: **`AGENTS.md`** (raiz) + regras em **`.cursor/rules/`**.  
> DX: **`task setup` / `task dev` / `task verify`** — ver `Taskfile.yml` e `docs/operations/desenvolvimento.md`.
> Autonomia contínua: **[`autonomia.md`](autonomia.md)** — autoridade delegada,
> classes de risco, auto-merge e únicas condições externas.
> Operação contínua: **[`operacao-24x7.md`](operacao-24x7.md)** — leases,
> concorrência, watchdog, evidências e recuperação.

## Regras universais

1. **Ler antes de codar:** glossário, ADRs relevantes, contratos, design system (se UI), `AGENTS.md`
2. **PT-BR** em docs voltadas a humanos; **inglês** em código (tipos, APIs)
3. **Não criar microserviços**, Kafka, OpenSearch ou K8s “por precaução”
4. **Não commitar secrets** nem credenciais de produção
5. **Não inventar termos** fora do glossário — se precisar, atualizar `glossario.md`
6. **Não criar arquivos de exemplo de código em `/docs`** — só documentação
7. Respeitar **decisoes-pendentes.md**; decisões D-01…D-28 já fechadas não devem ser reabertas por dificuldade técnica
8. Preferir o primeiro item elegível do roadmap a qualquer feature não planejada
9. Toda mutação de mensagem: **idempotência + seq + outbox**
10. Todo dado de negócio: **tenant_id + authZ + RLS**
11. Usar a classe R0–R3 declarada na spec; R3 começa pelo pacote em
    [`pacotes-decisao-r3.md`](../architecture/pacotes-decisao-r3.md)
12. API/Worker usam role runtime sem ownership/`BYPASSRLS`; migrations usam
    credencial separada

## Backend

### Fazer

- Trabalhar em módulos com fronteiras; contratos em `VibeChat.Contracts`
- Composition root só em `apps/api` e `apps/worker`
- Setar `TenantContext` / `app.tenant_id` em toda unit of work
- Publicar efeitos via outbox
- Testes de integração com Testcontainers quando tocar persistência
- Instrumentar OTel spans nos handlers críticos

### Não fazer

- Referenciar internals de outro módulo
- Confiar em `tenantId` do body
- Publish SignalR direto sem caminho de outbox para eventos de domínio duráveis
- Chamar OpenRouter dentro de `SendMessage` síncrono

### Checklist de PR backend

- [ ] Contratos atualizados se API pública mudou
- [ ] Migration + RLS
- [ ] Testes idempotency/seq se messaging
- [ ] Arch tests verdes

## Frontend

### Fazer

- Angular 22 standalone + Signals + CDK
- Tokens do `design-system.md` (Sora + IBM Plex Sans; teal/charcoal)
- Reutilizar assets de marca em `apps/web/public/` (logo, fundos, ícones PWA, sons) — ver `design-system.md` § Assets de marca
- OIDC PKCE; não guardar refresh tokens de forma insegura
- Ordenar mensagens por `seq`; gap-fill via history
- Idempotency-Key estável por tentativa de envio (UUID por compose submit)
- Light/dark via `data-theme`
- Motion sutil (2–3) sem poluição

### Não fazer

- Visual Slack/Discord/WhatsApp
- Cards no hero/shell sem necessidade
- Roxo/indigo / cream terracotta / Inter como marca
- Inventar logo/favicon/fundo genérico quando já existir arquivo catalogado
- State global pesado sem necessidade no MVP

### Checklist de PR frontend

- [ ] Tokens CSS usados (sem cores hardcoded soltas)
- [ ] A11y básica (foco, contraste)
- [ ] Reconnect SignalR tratado
- [ ] Empty/error states alinhados à marca

## Infra

### Fazer

- Compose reproduzível; healthchecks; volumes nomeados
- `.env.example` completo; sem secrets reais
- Keycloak realm export versionado
- Profiles opcionais para obs stack se pesada
- Scripts idempotentes em `infra/scripts`

### Não fazer

- Helm/K8s como requisito da fase 1
- Expor Postgres/Redis/MinIO publicamente no compose de prod reference
- Imagens `latest` sem pin em releases

## QA

### Fazer

- Cobrir critérios em `criterios-aceite-fatia-vertical.md`
- Priorizar testes security cross-tenant
- E2E do caminho feliz com dois usuários
- Reportar evidências (logs, traces ids)

### Não fazer

- Skip de testes flaky sem issue
- Aceitar fatia sem A5 (multi-tenant) verde

## Security

### Fazer

- Revisar PRs com checklist de `multi-tenant.md`
- Manter modelo de ameaças atualizado quando superfície muda
- Validar headers, rate-limit, uploads, hub authZ
- Negar features que enviem PII a AI sem flag + docs

### Não fazer

- Aprovar bypass de RLS “temporário” em main
- Pedir E2EE como blocker da fase 1 (está fora de escopo)

## Review (agente revisor)

### Olhar primeiro

1. Viola ADR-001/009/010?
2. Há caminho cross-tenant?
3. Outbox/idempotency corretos?
4. UI foge do design system?
5. Scope creep infra?
6. Docs/glossário desatualizados?

### Comentários úteis

- Apontar arquivo + regra ADR/doc
- Sugerir teste que falharia
- Diferenciar blocker vs nit

## Coordenação entre agentes

Usar IDs do `roadmap.md` (W0-*, W1-*, …). Declarar no PR/commit:

```text
Wave: W2-3
Trilha: C
Deps satisfeitas: W2-2, W1-1
```

Decisões técnicas reversíveis são do agente e podem exigir ADR. Parar somente nas
condições `R4 — Externo` de `autonomia.md`; blocker técnico segue o protocolo de
três tentativas e não bloqueia outras trilhas.

Pipeline Cloud (Build → QA+Merge → Docs): prompts em `.cursor/automations/`.
Build não marca `Done`; QA não abre feature; Docs não implementa o próximo item.

## Definition of Done (agente)

- Código compila / testes da trilha passam
- Docs tocadas se comportamento/ADR mudou
- Sem secrets
- Escopo limitado à tarefa da wave
- Push na branch de trabalho conforme processo do ambiente
