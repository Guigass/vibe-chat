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

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| IdentityServer / Duende | Custo/licensing e manutenção; Keycloak OSS atende SSO |
| Auth0 / Cognito | SaaS — conflita com self-hosted first |
| Auth caseiro JWT | Inseguro e incompleto (MFA, federation) |
| Only ASP.NET Identity local | Sem SSO enterprise de verdade |

## Consequências

- **+** SSO, MFA, federation disponíveis
- **+** Desacopla gestão de credenciais do app
- **−** Operar Keycloak (upgrade, realm, temas)
- **−** Precisa sync/espelho de perfil no VibeChat para memberships
