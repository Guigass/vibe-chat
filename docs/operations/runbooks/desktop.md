# Runbook — Cliente desktop (B-141)

Flag `Desktop:Enabled` (**off** por default). Com a flag desligada a API
responde 404 `DesktopDisabled`. O chat e a PWA seguem.

## Ligar

1. Definir `Desktop:AllowedHosts` com os hosts aceitos no scheme `vibechat://`.
2. Para auto-update, definir `Desktop:UpdateFeedUrl` (`https`, porta padrão) e
   `Desktop:UpdatePublicKeyPem` (SPKI base64 ou PEM pública). Sem feed, o
   update fica off. A API não baixa o feed.
3. Para build assinado, definir `Desktop:SigningCredentialRef` com uma
   referência externa (`operator/desktop-signing`). Não colocar PEM, token ou
   senha nesse campo. O certificado fica fora do repositório.
4. `Desktop:Enabled=true` e reiniciar a API.
5. O pacote sai do `apps/desktop/tauri.conf.json` (Tauri 2, mesmo bundle web).
   Conferir `apps/desktop/shell.lock.json` antes de publicar.

## Desligar

1. `Desktop:Enabled=false` e reiniciar a API.
2. Aparelhos já registrados permanecem na tabela. Um logout remoto posterior
   exige a flag ligada.
3. O `Down` da migration remove só `identity.desktop_devices`.

## Falha

- Deep link recusado: host ausente da lista, scheme diferente de `v1` ou
  destino fora de `channel` / `thread` / `workspace`.
- Update recusado: feed vazio (`DesktopUpdateOff`) ou assinatura/host
  divergente (`DesktopUpdateRejected`).
- Token no body: `DesktopTokenRejected`. Nada é gravado.
- Logout de aparelho de outro usuário sem `workspace.admin`: 403. Outro
  tenant: 404.
