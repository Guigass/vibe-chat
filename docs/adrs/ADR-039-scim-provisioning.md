# ADR-039 — SCIM 2.0 como adapter de provisioning

- Status: Accepted
- Data: 2026-10-18
- Relacionado: B-128, B-041, B-106, B-164, D-23, ADR-007, ADR-009, ADR-038

## Contexto

O provisionamento manual não escala e deixa acesso de quem saiu da empresa.
D-23 pede controles alinhados a LGPD/GDPR e SOC 2/ISO 27001 sem alegar
certificação. O pacote R3 exige flag off, porta independente do IdP, threat
model e rollback no mesmo PR.

## Decisão

1. SCIM 2.0 em `/scim/v2` é um adapter. Users e Groups chamam `IProvisioningPort`.
   O admin interno `POST /api/v1/admin/workspaces/{id}/scim/provisioning/users`
   usa o mesmo comando. O adapter não escreve `workspace_members`.
2. O bearer `vc_scim_` fica só como SHA-256 em `identity.scim_tokens`. A resposta
   de lista não devolve o segredo. Rotação revoga o hash anterior. A busca do
   token antes do tenant usa `app.scim_token_hash` (SET LOCAL), no mesmo modelo
   do token de bot. RLS é FORCE + WITH CHECK.
3. `externalId` é único por tenant e é a chave de idempotência. O mesmo par
   repete o mesmo principal. E-mail não adota perfil que não seja `pending:`.
4. Mapeamento de grupo só concede `Member`, `Moderator`, `Auditor` ou `Guest`.
   `Admin`, `WorkspaceOwner`, `PlatformOwner` e `Bot` respondem `ForbiddenRole`
   e não mudam membership. Se o usuário já tem outro papel, o dry-run lista
   conflito e o apply não rebaixa.
5. Desativar (`active=false` ou DELETE) remove memberships do tenant, marca
   `LeftAt` nos canais, grava `identity.access_revocations` e não apaga
   conteúdo. A próxima chamada autenticada sem membership restante recebe
   401 `SessionRevoked` em `/me` e 403 no workspace. Reativar não restaura grants.
6. Flag `Identity:Scim:Enabled` **off** em `appsettings.json`. Desligada, admin
   e `/scim/v2` respondem 404 `ScimDisabled`. `Identity:Scim:KeycloakAdapter`
   também nasce off. Com o adapter desligado, o revogador externo não é chamado
   e o SCIM local segue. Com o adapter ligado, falha externa não desfaz a
   revogação local.
7. Bulk aceita no máximo 20 operações. Filtro só `eq` em `userName`,
   `externalId` e `displayName`. Página `startIndex`/`count` com teto 100.

## Alternativas

- Escrever membership direto no endpoint SCIM: o IdP viraria dono do papel.
- Adotar usuário pelo e-mail: takeover de admin.
- Depender do admin API do Keycloak para desativar: um IdP ausente bloquearia
  outro provider.

## Consequências

- Rollback operacional: `Identity:Scim:Enabled=false`. Tokens e principals
  deixam de ser usados. Memberships já removidas não voltam sozinhas.
- Rollback de schema: `Down` remove as tabelas `identity.scim_*` e
  `identity.access_revocations`. Perfis e mensagens permanecem.
- Break-glass: um admin local continua convidando pelo fluxo B-068, com a flag
  desligada.
- Métrica `vibechat.scim.provisioned` só leva `result`
  (`created`/`updated`/`deactivated`). Sem e-mail, sem token.
- R3: sem serviço novo, sem bus, flag off. Sem credencial real de IdP.
