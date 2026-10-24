# Runbook — Cliente mobile (B-063)

Flag `Mobile:Enabled` (**off** por default). Com a flag desligada a API
responde 404 `MobileDisabled`. O chat, a PWA e o desktop seguem.

## Ligar

1. Definir `Mobile:AllowedHosts` com os hosts aceitos no scheme `vibechat://`
   e no share target.
2. Sem conta de loja, deixar `Mobile:StoreCredentialRef` vazio. A
   distribuição fica `sideload`. O artefato documentado é
   `apps/mobile/sideload.json` (APK/IPA não assinados). Não colocar PEM,
   token ou senha nesse campo.
3. Para uma referência de loja, usar um identificador externo
   (`operator/mobile-store`). A conta e o certificado ficam fora do
   repositório.
4. `Mobile:Enabled=true` e reiniciar a API.
5. O pacote sai do `apps/mobile/capacitor.config.json` (Capacitor 7, mesmo
   bundle web). Conferir `apps/mobile/shell.lock.json` antes de publicar.
   O CI não exige SDK de Android nem Xcode.

## Desligar

1. `Mobile:Enabled=false` e reiniciar a API.
2. Aparelhos já registrados permanecem na tabela. Um logout remoto posterior
   exige a flag ligada.
3. O `Down` da migration remove só `identity.mobile_devices`.

## Falha

- Deep link ou share recusado: host ausente da lista, scheme diferente de
  `v1`, destino fora de `channel` / `thread` / `workspace`, ou `confirm`
  diferente de `true`.
- Share com `tenantId`, texto ou token: `MobileTokenRejected`. Nada é enviado.
- Push com texto ou token de provedor: `MobileTokenRejected`. O preview
  aceito não carrega body.
- Token no registro: `MobileTokenRejected`. Nada é gravado.
- Logout de aparelho de outro usuário sem `workspace.admin`: 403. Outro
  tenant: 404.
