# SSO corporativo (B-164)

Brokering OIDC e SAML fica no Keycloak. O VibeChat só aceita o JWT do
Keycloak. A flag `Identity:Sso:Enabled` nasce desligada.

## Onde fica cada coisa

| Peça | Lugar |
|------|--------|
| IdPs de laboratório (`hub-oidc`, `hub-saml`), desligados | `infra/keycloak/realm-vibechat.json` |
| Claims `broker_issuer`, `broker_subject`, `broker_alias` | client scope `vibechat-broker` |
| Rótulo do botão | `displayName` do Identity Provider no Keycloak |
| Lista na tela de login | `GET /api/v1/auth/sso-providers` |
| Vínculo `(issuer, subject)` | `identity.external_identities` |
| Segredo do client de diretório | `SSO_DIRECTORY_CLIENT_SECRET` (placeholder de dev) |

Não há CRUD de IdP na UI do VibeChat.

## Ligar em laboratório

1. Confirme que o realm importado tem os dois IdPs desligados e o client
   `vibechat-sso-directory`.
2. No Keycloak, dê ao service account desse client o papel de cliente
   `realm-management` / `view-identity-providers`:

```bash
docker compose exec keycloak /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master \
  --user "$KEYCLOAK_ADMIN" --password "$KEYCLOAK_ADMIN_PASSWORD"
docker compose exec keycloak /opt/keycloak/bin/kcadm.sh add-roles \
  -r vibechat --uusername service-account-vibechat-sso-directory \
  --cclientid realm-management --rolename view-identity-providers
```

3. Edite o IdP (OIDC ou SAML). Troque `change_me` / `CHANGE_ME` pelo segredo
   ou certificado do IdP de laboratório. Não commite esse valor.
4. Ajuste o mapper `broker_issuer` para o issuer real do IdP (URL do OIDC ou
   `entityId` do SAML). O `broker_subject` sai da nota de sessão
   `identity_provider_identity`.
5. Preencha `displayName` (ex.: `Logar com HUB`). Vazio faz a tela usar o
   texto genérico do catálogo. O alias técnico não aparece como rótulo.
6. Opcional: `config.domains` com o domínio do e-mail (`hub.example`). A tela
   destaca o único IdP cujo domínio coincide.
7. Deixe `trustEmail=false`. No fluxo **first broker login**, não ative
   vinculação automática por e-mail. Sem isso, o Keycloak pode entregar o
   `sub` de outra pessoa e o app grava o par nesse usuário.
8. Habilite o IdP (`enabled=true`, `hideOnLogin=false`).
9. Ligue a flag: `IDENTITY_SSO_ENABLED=true` e suba de novo o profile `apps`.

## Provar o fluxo

1. Abra `/login`. O botão mostra o `displayName`.
2. Entre pelo IdP. O Keycloak devolve o code; a API recebe só o JWT do
   Keycloak (`iss` = `Authentication:Authority`).
3. Sem convite, o perfil nasce sem linha em `workspace_members`.
4. Com stub `pending:{email}`, o primeiro login reivindica esse usuário e
   mantém o papel. E-mail igual ao de um admin que não está `pending` cria
   outro perfil e não copia a membership.
5. Repetir o login não cria outro vínculo. Tentar o mesmo par com outro `sub`
   do Keycloak responde 409 `IdentityLinkConflict`.

O mesmo contrato vale para o IdP SAML: o app não vê a asserção SAML.

## Logout e sessão

O botão Sair do app faz o end-session OIDC no Keycloak (`signoutRedirect`).
O Keycloak pode propagar SLO ao IdP se o IdP e o client estiverem
configurados para isso; isso é best-effort e não é garantia do VibeChat.
Desativar ou remover a identidade externa não apaga o usuário. Revogar
sessões no deprovisionamento é B-128.

Break-glass: um usuário local do Keycloak (sem claims de broker) continua
entrando pelo botão Keycloak. DevAuth só existe em Development.

## Falha

Diretório fora do ar, segredo vazio ou três falhas seguidas: a lista fica
vazia por até 60s e o login Keycloak segue. O chat não depende dessa chamada.

## Rollback

1. `IDENTITY_SSO_ENABLED=false` (ou `Identity:Sso:Enabled=false`).
2. A API deixa de consultar `external_identities` e a rota de lista volta
   404 `SsoDisabled`.
3. Desabilite os IdPs no Keycloak se quiser impedir o broker na tela do
   Keycloak.
4. A migration `Down` remove só `identity.external_identities`. Perfis e
   memberships ficam.
