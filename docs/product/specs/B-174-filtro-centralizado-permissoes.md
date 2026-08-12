# B-174 — Filtro centralizado de permissões (`RequirePermission`)

> Wave W7-10 · Trilha B/E · Deps: P2-1, B-041 · Decisões: D-07 · Risco R2

## Problema

`RequirePermissionAttribute` existe em `BuildingBlocks`, mas a API Minimal em
`Program.cs` repete `HasPermissionAsync` manualmente em cada handler. Isso
aumenta risco de endpoint novo sem checagem explícita e dificulta revisão de
authZ.

## Escopo

- Implementar filtro/endpoint convention para Minimal APIs que lê
  `RequirePermissionAttribute` (ou equivalente) e chama `IPermissionChecker`.
- Resolver `tenantId` e `userId` do mesmo caminho que os handlers atuais
  (`ResolveWorkspaceAsync` / `ResolveChannelAsync` / membership admin).
- Migrar handlers existentes ao filtro (ou wrapper) — priorizar mutações e
  superfícies admin.
- Teste de arquitetura ou security: endpoint mutável sem gate de permissão
  documentado falha na CI.
- Remover duplicação óbvia onde o filtro cobre o caso.

## Fora de escopo

- Mudar o catálogo `RolePermissionCatalog` ou roles atribuíveis (B-041).
- Autorização baseada em claims JWT do Keycloak (ver B-176).
- Novas permissões de produto.

## Contratos

Sem mudança de API pública. Documentar em `architecture/contratos.md` o mecanismo
de authZ (filtro + membership) se o padrão de implementação mudar.

## Multi-tenant e authZ

O filtro deve usar `TenantId` derivado de membership/workspace/channel — nunca
de body/query solta. Falha fechada: sem tenant/membro → 403.

## Aceite

- [ ] Atributo/filtro aplicado e registrado no pipeline da API.
- [ ] Handlers de mutação críticos (mensagem, admin, settings, invite/role) usam
  o filtro ou helper unificado.
- [ ] Teste CI impede regressão de endpoint sem gate.
- [ ] `task test:security` verde.

## Evidência

- Saída de `task test:security` e arch tests.
- Lista de endpoints migrados no PR.
