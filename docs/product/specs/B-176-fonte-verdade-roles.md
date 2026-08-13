# B-176 — Fonte de verdade de roles (DB vs Keycloak)

> Wave W7-12 · Trilha G/B · Deps: B-002, P2-1 · Decisões: D-07 · Risco R1

## Problema

`PermissionChecker` lê roles de `workspace_members`; claims JWT (`ClaimTypes.Role`)
e `CurrentUser.Roles` não governam authZ na API. Operadores podem assumir que
roles do Keycloak sincronizam permissões — hoje não sincronizam.

## Escopo

- Documentar explicitamente: **fonte de verdade = `workspace_members.role`** para
  authZ de produto; JWT prova identidade (`sub`, email), não papel de workspace.
- Atualizar `docs/architecture/contratos.md`, glossário e `docs/security/multi-tenant.md`.
- Atualizar runbook/realm Keycloak versionado: roles do realm são opcionais para
  futuro SSO; não substituem membership.
- (Opcional, se simples) Marcar em código que `ICurrentUser.Roles` não é usado
  para `HasPermissionAsync` — comentário ou remover uso enganoso no web se existir.

## Fora de escopo

- SCIM / sync automático Keycloak → workspace_members (B-128).
- Mudar modelo de roles ou catálogo de permissões.

## Contratos

Documentação; sem alteração de API.

## Aceite

- [x] Contratos e glossário descrevem a separação identidade vs autorização.
- [x] `multi-tenant.md` referencia a matriz de camadas com nota sobre DB roles.
- [x] Realm/docs Keycloak não contradizem o modelo DB-first.

## Evidência

- Links de docs no PR; sem mudança de comportamento de API obrigatória.
