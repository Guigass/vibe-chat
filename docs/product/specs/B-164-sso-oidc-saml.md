# B-164 — SSO corporativo via OIDC / SAML (Keycloak brokering)

> Wave 13 · Trilha A/B/D/E · Deps: B-002, ADR-007; complementar a B-128 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Empresas exigem login no IdP corporativo (Entra ID, Okta, Google Workspace,
AD FS, etc.) via OIDC ou SAML 2.0, sem manter senhas locais no VibeChat.
O app já autentica só contra Keycloak (B-002); falta o caminho operacional e de
produto para federar esses IdPs.

## Escopo

- Identity Brokering no Keycloak para IdP externo **OIDC** e **SAML 2.0**.
- Exemplos versionados de realm/IdP (dev) + runbook de operador.
- Mapeamento `(issuer, subject)` → `ExternalIdentity` / User local, sem elevar
  papel; segue
  [`modelo-identidade-principals.md`](../../architecture/modelo-identidade-principals.md).
- UX de login: entrada SSO / domain hint / lista de IdPs discoverable (sem
  hardcode de vendor no cliente).
- **Rótulo personalizável** por IdP (ex.: “Logar com HUB”): o botão/lista usa o
  `displayName` configurado no Identity Provider do Keycloak (ops); fallback
  genérico só se o nome estiver vazio. Sem nomes de vendor hardcoded no cliente.
- Primeiro login: vincula só se houver convite/`pending` ou política explícita;
  sem auto-admin.
- Logout / SLO best-effort documentado; revogação de sessão alinhada a B-128.

## Fora de escopo

- SAML direto no Angular ou na API.
- Substituir Keycloak por outro IdP embutido.
- CRUD de Identity Providers na UI admin do VibeChat (configuração fica no
  Keycloak / ops).
- Multi-IdP por tenant na UI do app.
- SCIM, grupos e deprovisioning (B-128).

## Contratos

App ↔ Keycloak permanece **OIDC Authorization Code + PKCE**; API/SignalR
continuam validando apenas JWT emitido pelo Keycloak (issuer, audience,
lifetime). IdPs externos nunca emitem tokens aceitos diretamente pela API.

Keycloak autentica (incluindo via broker); VibeChat continua owner de User
local, memberships, papéis e authZ. Account linking e claim mapping são
auditáveis; e-mail não é identificador imutável.

## UX

Tela de login oferece caminho SSO claro (botão / domain hint). Lista de IdPs
vinda de discovery/configuração do realm; cada entrada exibe o rótulo
configurado pelo operador (ex.: “Logar com HUB”), não o alias técnico nem o
nome do protocolo. Erros de broker e linking falho são legíveis; DevAuth
permanece só em Development.

Runbook documenta onde setar o `displayName` no Keycloak e como validar na
tela de login.

## Multi-tenant e authZ

Login válido não cria acesso implícito a tenant/workspace. Linking cross-tenant
é negado. Claims do IdP externo não concedem `workspace.admin` nem
PlatformOwner. RLS e authZ do VibeChat permanecem a fonte de autorização.

## Aceite

- [ ] Fluxo broker OIDC (IdP externo → Keycloak → app) funciona em lab.
- [ ] Fluxo broker SAML 2.0 funciona em lab com o mesmo contrato app↔Keycloak.
- [ ] App valida só JWT Keycloak; tokens do IdP externo são rejeitados.
- [ ] Mapping `(issuer, subject)` é estável e auditável; e-mail sozinho não
      cria User privilegiado.
- [ ] Primeiro login sem convite/`pending`/política explícita não concede
      membership.
- [ ] Linking não eleva papel nem cruza tenant.
- [ ] Runbook + exemplos de realm/IdP documentados; secrets só em env.
- [ ] Rótulo do botão/lista SSO reflete o `displayName` configurado (ex.:
      “Logar com HUB”); sem vendor hardcoded; fallback só se vazio.
- [ ] Logout/SLO e revogação de sessão documentados e alinhados a B-128.

## Testes

Smoke de broker OIDC e SAML em lab; suíte negativa de mapping/linking;
cross-tenant negado; E2E de login SSO no modo `oidc` (não no caminho DevAuth
da CI).

## Riscos

Misconfig de broker (ACS/redirect, audience, certificate) causa lockout ou
aceite de identidade errada. Account linking por e-mail frágil habilita takeover.
Mitigações: exemplos versionados, checklist ops, linking só com política
explícita, audit de vínculo, break-glass local documentado.
