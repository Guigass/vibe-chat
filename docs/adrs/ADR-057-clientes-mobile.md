# ADR-057: Cliente mobile híbrido sobre o web

## Status: Accepted

## Contexto

D-20 define a PWA como cliente canônico e o mobile depois do desktop. B-063
pede um wrapper OSS para iOS e Android, com secure storage, biometria
opcional, logout remoto, deep link, share target, câmera, seletor de arquivo
e push, sem prender o servidor a uma loja. Um cliente com UI ou API próprias
divergiria do contrato. O pacote R3 de clientes pede wrapper, storage seguro
e matriz de suporte; fila offline e sync em background ficam em B-143.

## Decisão

1. **Wrapper.** Capacitor 7 (Apache-2.0), webview do sistema. O shell carrega
   o mesmo bundle de `apps/web` (`webDir` aponta para o web). Não há fork de
   UI nem endpoint privilegiado. Tauri mobile não entra: o desktop já usa
   Tauri para janela, tray e update; repetir essa cadeia no telefone não
   entrega sideload sem loja com menos dependência.
2. **Flag.** `Mobile:Enabled` nasce false. Desligada, `/api/v1/mobile/*`
   responde 404 `MobileDisabled`. Chat, PWA e desktop seguem.
3. **Matrix.** `android-arm64` e `ios-arm64`. O artefato reproduzível é o
   hash de `apps/mobile/shell.lock.json`, um por alvo. O binário é produzido
   pelo operador com o `capacitor.config.json` commitado. O CI confere o
   hash e o protocolo; não exige SDK de Android nem Xcode.
4. **Scheme e share.** O mesmo `vibechat://{host}/v1/{channel|thread|workspace}/{guid}`
   de ADR-056. Host só se estiver em `Mobile:AllowedHosts`. Share exige
   `confirm: true` e não cria mensagem (`sendsMessage: false`). `tenantId`,
   texto ou token no body são recusados. O tenant sai só da sessão.
5. **Token.** O servidor não recebe nem grava access token, refresh token,
   FCM, APNs ou `Bearer`. A coluna não existe. O shell guarda o token no
   secure storage do OS. Logout remoto marca o aparelho e a resposta pede
   `wipeSecureStorage`. Quem não é o dono nem `workspace.admin` recebe 403.
6. **Push.** Sem SDK de provedor no servidor. O preview aceita só `message`
   ou `mention` mais `conversationId`. O padrão é `containsContent: false`.
7. **Loja.** `Mobile:StoreCredentialRef` é uma referência externa. Vazia, a
   distribuição é `sideload` e `apps/mobile/sideload.json` documenta o
   artefato sem conta. PEM, `token`, `secret` ou `private` nesse campo é
   recusado e não volta na resposta.
8. **Capacidades nativas.** Biometria, notificação, câmera e seletor de
   arquivo nascem desligados. Biometria só muda com `true` explícito do
   dono. Bytes de arquivo continuam no endpoint de anexo já existente.
9. **Rollback.** `Mobile:Enabled=false`. Aparelhos já registrados permanecem
   revogáveis quando a flag voltar. O `Down` remove só
   `identity.mobile_devices`.

## Consequências

- O browser continua sendo o fallback. O web não ganha layout novo.
- Supply chain do wrapper fica no config e no SBOM (`apps/mobile/sbom.json`),
  não num runtime nativo obrigatório do CI.
- Sync offline continua em B-143. Publicar na loja continua `External action`.
