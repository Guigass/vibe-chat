# B-128 — SCIM 2.0 e sincronização de grupos

> Wave 13 · Trilha B/D/E · Deps: B-041, B-106, D-23 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Provisionamento manual não escala e mantém acesso de pessoas desligadas por
tempo excessivo.

## Escopo

- SCIM 2.0 Users e Groups no escopo de tenant.
- Bearer token hash, rotação e scopes.
- Create/update/deactivate, pagination, filter e bulk limitado.
- Group mapping para workspace memberships/roles allowlisted.
- Deprovision revoga sessões e memberships.
- Audit e dry-run de mapping.

## Fora de escopo

- SCIM cross-tenant com um token.
- Elevar para PlatformOwner via mapping.
- Apagar conteúdo autorado ao desativar usuário.

## Contratos

Endpoints `/scim/v2`; schemas RFC compatíveis; externalId unique por tenant;
erros SCIM padronizados. ADR registra interação Keycloak/profile/membership.

## UX

Admin gera token uma vez, copia, rotaciona e vê último uso. Mapping de grupos
mostra preview e conflitos.

## Multi-tenant e authZ

Token vinculado a um tenant e capabilities SCIM. RLS e suíte negativa em todos
os endpoints.

## Aceite

- [ ] Create/update/deactivate idempotentes.
- [ ] Token de tenant A não enumera B.
- [ ] Deactivate revoga sessão/acesso rapidamente.
- [ ] Mapping não cria papel proibido.
- [ ] Secret só aparece na criação.

## Testes

SCIM conformance subset, filters/pagination/bulk, token rotation, cross-tenant e
E2E provisioning.

## Riscos

Deprovision incorreto causa lockout ou acesso residual. Preview, audit, retry
seguro e break-glass local documentado.

