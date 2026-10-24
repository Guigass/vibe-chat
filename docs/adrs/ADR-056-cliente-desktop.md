# ADR-056: Cliente desktop empacotado sobre o web

## Status: Accepted

## Contexto

D-20 define a PWA como cliente canônico. B-141 pede um wrapper OSS para
Windows, macOS e Linux, com deep link, notificações, tray, launch-at-login
opt-in, token no secure storage e update verificável. Um cliente com UI ou
API próprias divergiria do contrato.

## Decisão

1. **Wrapper.** Tauri 2 (MIT ou Apache-2.0), system webview. Electron fica de
   fora: embute um browser e aumenta a cadeia de suprimento. O shell carrega
   o mesmo bundle de `apps/web` (`frontendDist` aponta para o web). Não há
   fork de UI nem endpoint privilegiado.
2. **Flag.** `Desktop:Enabled` nasce false. Desligada, `/api/v1/desktop/*`
   responde 404 `DesktopDisabled`. Chat e PWA seguem.
3. **Matrix.** `windows-x64`, `macos-arm64` e `linux-x64`. O artefato
   reproduzível é o hash de `apps/desktop/shell.lock.json`, um por OS. O
   binário é produzido pelo operador com o `tauri.conf.json` commitado. O CI
   confere o hash, a assinatura e o protocolo; não exige cross-compile.
4. **Scheme.** `vibechat://{host}/v1/{channel|thread|workspace}/{guid}`.
   Host só se estiver em `Desktop:AllowedHosts`. Userinfo, query, fragmento,
   versão diferente e destino desconhecido são rejeitados.
5. **Token.** O servidor não recebe nem grava access token, refresh token ou
   `Bearer`. A coluna não existe. O shell guarda o token no secure storage do
   OS. Logout remoto marca o aparelho e a resposta pede
   `wipeSecureStorage`. Quem não é o dono nem `workspace.admin` recebe 403.
6. **Update.** Feed vazio, `http` ou porta não padrão deixa o update off. A
   API não faz fetch. O manifesto `vibechat-update/1` precisa de RSA-PSS
   SHA-256 com a chave pública do operador. Assinatura ou host divergente é
   rejeitado. A chave privada não entra no repositório.
7. **Assinatura do binário.** `Desktop:SigningCredentialRef` é uma referência
   externa. PEM, `token`, `secret` ou `private` nesse campo é recusado e não
   volta na resposta. Sem certificado real no git.
8. **Capacidades nativas.** Notificações, tray e launch-at-login nascem
   desligados. Launch-at-login só muda com `true` explícito do dono.
9. **Rollback.** `Desktop:Enabled=false`. Aparelhos já registrados permanecem
   revogáveis quando a flag voltar. O `Down` remove só
   `identity.desktop_devices`.

## Consequências

- O browser continua sendo o fallback. O web não ganha layout novo.
- Supply chain do wrapper fica no config e no SBOM (`apps/desktop/sbom.json`),
  não num runtime Rust obrigatório do CI.
- B-063 (mobile) reutiliza a mesma API. Sync offline continua em B-143.
