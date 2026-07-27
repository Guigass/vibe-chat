# B-125 — Automation builder

> Wave 13 · Trilha B/C/D · Deps: B-108, B-110, B-124 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Webhooks e plugins exigem código para processos simples; faltam automações
auditáveis que conectem eventos, condições e ações.

## Escopo

- Modelo trigger → conditions → actions, declarativo e versionado.
- Triggers de message, form, task, schedule e webhook.
- Ações allowlisted: post message, create/update task, notify, call connector.
- Dry-run, enable/disable, run history e replay controlado.
- Idempotency, depth limit, rate-limit, timeout e circuit breaker.
- Identidade `Automation` com capabilities.

## Fora de escopo

- Código arbitrário, eval ou container fornecido pelo usuário.
- Loop infinito/recursão sem limite.
- Ação com privilégio superior ao owner/configuração.

## Contratos

Manifesto `automation.definition.v1`; execution/run/step records; eventos
versionados; secret references sem valor em claro. ADR define engine interno sem
novo bus até gatilho ADR-015.

## UX

Builder por passos, validação, teste com dados redigidos, histórico e erro por
step. Mostra identidade e permissões usadas.

## Multi-tenant e authZ

Somente admins criam automação de workspace; capabilities e channel scopes
explícitos. Toda execução restabelece TenantContext.

## Aceite

- [ ] Retry não duplica efeitos.
- [ ] Loop é interrompido e auditado.
- [ ] Action sem capability falha fechada.
- [ ] Secret nunca aparece no run log.
- [ ] Desativar impede novos runs.

## Testes

State machine/property tests, idempotência, loop/depth, security scopes, worker
resilience e E2E create/test/run.

## Riscos

Confused deputy e runaway automation. Identidade própria, capability intersection
e limites são invariantes.

