# ADR-007: Keycloak

## Status: Accepted

## Contexto

Corporações exigem SSO, federação (LDAP/AD), MFA e gestão de realm. Construir IdP próprio seria risco e desvio de foco. OIDC é o padrão de integração.

## Decisão

Usar **Keycloak** como Identity Provider:

- Fluxo **OAuth 2.0 / OIDC Authorization Code + PKCE** no Angular
- API e SignalR validam JWT (issuer, audience, lifetime)
- Claims de usuário (e mapeamento de tenant) documentados; tenant não é “escolhido” pelo cliente sem autorização
- Realm de desenvolvimento exportado/versionado em `infra/keycloak`
- SSO corporativo via **Identity Brokering** para IdPs externos **OIDC** e
  **SAML 2.0** (produto operacional em B-164); o app **não** fala SAML nem
  aceita tokens emitidos diretamente pelo IdP federado

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| IdentityServer / Duende | Custo/licensing e manutenção; Keycloak OSS atende SSO |
| Auth0 / Cognito | SaaS — conflita com self-hosted first |
| Auth caseiro JWT | Inseguro e incompleto (MFA, federation) |
| Only ASP.NET Identity local | Sem SSO enterprise de verdade |
| SAML direto no Angular/API | Duplica IdP; foge do contrato OIDC único; Keycloak já faz brokering |

## Consequências

- **+** SSO, MFA, federation disponíveis
- **+** IdPs corporativos (Entra ID, Okta, Google, AD FS, etc.) via OIDC/SAML no Keycloak
- **+** Desacopla gestão de credenciais do app
- **−** Operar Keycloak (upgrade, realm, temas, brokers e certificados SAML)
- **−** Precisa sync/espelho de perfil no VibeChat para memberships
- **−** Account linking e claim mapping exigem política explícita (sem auto-admin)

## Emenda (2026-08-10)

B-164 documenta o caminho ops-first de brokering OIDC/SAML. CRUD de IdP na UI
admin do VibeChat permanece fora de escopo; configuração fica no Keycloak/ops.
O rótulo do botão de login SSO (ex.: “Logar com HUB”) vem do `displayName` do
Identity Provider no Keycloak, consumido pela tela de login. Provisionamento
lifecycle continua em B-128 (SCIM).
