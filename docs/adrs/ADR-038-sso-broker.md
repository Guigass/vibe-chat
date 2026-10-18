# ADR-038 — SSO corporativo via brokering Keycloak

- Status: Accepted
- Data: 2026-10-18
- Relacionado: B-164, B-002, B-068, B-128, ADR-007, ADR-009

## Contexto

O app já autentica só contra Keycloak (ADR-007). Empresas precisam entrar por
um IdP corporativo OIDC ou SAML sem senha local no VibeChat e sem o app falar
SAML. O pacote R3 exige flag off, ADR, threat model e rollback no mesmo PR.

## Decisão

1. Identity Brokering fica no Keycloak. O app continua OIDC Authorization Code
   + PKCE e só aceita JWT cujo `iss` é o Authority configurado. Token de IdP
   externo não autentica a API nem o hub.
2. O par estável é `(broker_issuer, broker_subject)`, gravado em
   `identity.external_identities` (sem `tenant_id`, como `user_profiles`).
   O `sub` do Keycloak continua sendo o subject local. E-mail não escolhe um
   usuário que não esteja `pending:`.
3. Com a flag ligada e claims de broker presentes:
   - par já vinculado a outro `sub` do Keycloak → 409 `IdentityLinkConflict`,
     sem trocar o vínculo nem o papel;
   - convite `pending:{email}` → reivindica o stub e grava o par;
   - `sub` do Keycloak já existente → grava o par nesse usuário, sem alterar
     membership;
   - nenhum dos anteriores → cria perfil sem membership e grava o par.
4. Flag `Identity:Sso:Enabled` **off** em `appsettings.json`. Desligada, os
   claims de broker são ignorados e `GET /api/v1/auth/sso-providers` responde
   404 `SsoDisabled`. O login Keycloak e o DevAuth de Development seguem.
5. A lista da tela de login vem do Admin API do Keycloak (`displayName`,
   alias, `config.domains`). Timeout de 3s, cache de 30s, circuito aberto por
   60s após 3 falhas. Falha devolve lista vazia; o botão Keycloak permanece.
   `displayName` vazio usa o fallback genérico do catálogo i18n.
6. O realm de desenvolvimento versiona os IdPs `hub-oidc` e `hub-saml`
   **desligados**, com segredo `change_me`. O client
   `vibechat-sso-directory` só lista IdPs. O fluxo first broker login do
   Keycloak não pode vincular conta existente por e-mail automaticamente.
7. Logout do app continua o end-session OIDC do Keycloak. SLO até o IdP
   externo é best-effort e depende do IdP. Revogação de sessão por
   deprovisionamento continua em B-128.

## Alternativas

- SAML ou OIDC direto no Angular/API: duplica o IdP e quebra o contrato único.
- Vincular pelo e-mail em qualquer perfil: takeover de admin.
- CRUD de IdP na UI admin do VibeChat: fora de escopo; a configuração fica no
  Keycloak.

## Consequências

- Rollback operacional: `Identity:Sso:Enabled=false`. Vínculos já gravados
  deixam de ser consultados. O chat não muda. Runbook:
  `docs/operations/runbooks/sso-broker.md`.
- Rollback de schema: `Down` remove `identity.external_identities`. Perfis e
  memberships permanecem.
- Capacidade: a listagem não roda no hot path de mensagem. Métricas
  `vibechat.sso.link_created` e `vibechat.sso.link_denied` não levam e-mail
  nem subject.
- R3: sem serviço novo, sem bus, flag off por default. Sem credencial real de
  IdP corporativo.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-164). Login não cria membership.
Claims de papel do IdP externo não alteram `workspace_members.role`. O mesmo
subject em outro issuer é outro par.
