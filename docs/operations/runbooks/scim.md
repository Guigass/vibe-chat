# SCIM 2.0 (B-128)

A flag `Identity:Scim:Enabled` nasce desligada. O adapter Keycloak
(`Identity:Scim:KeycloakAdapter`) também. Com os dois off, o chat e o login
não mudam.

## Onde fica cada coisa

| Peça | Lugar |
|------|--------|
| Protocolo | `/scim/v2/Users`, `/Groups`, `/Bulk` |
| Token (só hash) | `identity.scim_tokens` |
| Principal do tenant | `identity.scim_principals` |
| Mapeamento de papel | `identity.scim_group_mappings` |
| Revogação local | `identity.access_revocations` |
| UI | `/admin/scim` |
| Flags | `IDENTITY_SCIM_ENABLED`, `IDENTITY_SCIM_KEYCLOAK_ADAPTER` |

O segredo `vc_scim_` aparece uma vez, na criação ou na rotação. A lista mostra
os últimos 4 caracteres e o último uso.

## Ligar em laboratório

1. `IDENTITY_SCIM_ENABLED=true` no ambiente da API e suba de novo o serviço.
2. Entre como owner/admin e abra `/admin/scim`.
3. Gere um token, copie o segredo e configure o cliente SCIM com
   `Authorization: Bearer vc_scim_…`.
4. Crie o grupo, confira o dry-run e salve o mapeamento só com papel da lista
   (Member, Moderator, Auditor, Guest).

## Desprovisionar

`PATCH` com `active=false`, ou `DELETE /scim/v2/Users/{id}`, remove memberships
daquele tenant e grava a revogação. Mensagens autoradas permanecem. Reativar
não devolve o papel antigo: o mapeamento precisa rodar de novo.

## Falha e rollback

- Token vazado: rotacione ou revogue em `/admin/scim`. O hash antigo deixa de
  autenticar na hora.
- Mapeamento errado: apague o mapeamento. Membership criada só por aquele
  grant sai junto. Papel manual (sem grant) permanece.
- Desligar a feature: `IDENTITY_SCIM_ENABLED=false`. `/scim/v2` e o admin
  respondem 404 `ScimDisabled`. Quem já foi desativado continua sem membership.
- Adapter Keycloak off não impede SCIM. Se o adapter estiver on e o IdP falhar,
  a revogação local continua valendo.
- Break-glass: convite local do admin (B-068), com a flag desligada.

Não há certificação SOC 2/ISO nesta entrega (D-23).
