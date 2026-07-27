# B-134 — Administração delegada e quotas

> Wave 14 · Trilha B/D/E · Deps: B-128 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

`workspace.admin` amplo não escala para organizações com times, unidades e
responsabilidades segregadas.

## Escopo

- Admin scopes: tenant, workspace, space, compliance, security, billing-read.
- Role assignments com expiry e reason.
- Quotas de users, channels, storage, messages, automations e AI budget.
- Usage dashboard e alertas antes do limite.
- Hard/soft limit por recurso.
- Break-glass temporário auditado.

## Fora de escopo

- Billing/checkout.
- Papel custom arbitrário sem catálogo.
- Bypass de RLS.

## Contratos

ScopedRoleAssignment/Quota/Usage tenant-scoped; permission checker central;
reason codes para quota. ADR atualiza catálogo de roles.

## UX

Admin vê somente áreas do seu scope. Quota mostra uso, tendência e ação; não
falha genericamente.

## Multi-tenant e authZ

Escopo nunca amplia além do grantor. Auto-elevação proibida; assignments e
break-glass auditados.

## Aceite

- [ ] Space admin não gerencia outro space.
- [ ] Grantor não concede acima do próprio scope.
- [ ] Expiry revoga.
- [ ] Quota concorrente não ultrapassa hard limit.
- [ ] Break-glass expira e alerta.

## Testes

Permission lattice/property, concurrent quota, expiry, cross-tenant e E2E
visibility.

## Riscos

Explosão de papéis e authZ inconsistente. Catálogo pequeno, scopes compostos e
testes de matriz.

