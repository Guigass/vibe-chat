# ADR-053: Registry assinado de plugins por mirror offline

## Status: Accepted

## Contexto

B-110 instala manifesto local. B-066 e B-111 acrescentam capability e grant.
B-136 entrega o SDK sem feed público. D-18 permite registry assinado, catálogo
oficial, privado ou allowlisted, e proíbe billing e código in-process. B-137
precisa de descoberta, atualização, rollback e revogação sem baixar pacote de
um serviço público.

## Decisão

1. **Flag.** `Integrations:PluginRegistry:Enabled` nasce false. Off responde
   404 `PluginRegistryDisabled`. O chat, o install local de B-110 e o SDK de
   B-136 não dependem desta flag.
2. **Assinatura.** RSA-PSS com SHA-256, sal do tamanho do hash, chave pública
   SPKI configurada pelo operador (`TrustRoots`). O payload canônico
   `vibechat.registry-entry.v1` cobre publisher, origem, checksum `sha256=`,
   capabilities, versão e o manifesto. A assinatura é conferida antes de
   persistir ou ativar.
3. **Sem fetch.** Qualquer `CatalogUrl` ou `artifactRef` `http`/`https` responde
   `RegistryEgressBlocked` e não abre conexão. O mirror offline usa
   `mirror://`, `official://` ou `private://`. Catálogo `allowlisted` só entra
   se o id estiver na allowlist do operador e na trust root.
4. **Grant.** Instalar exige a lista explícita de capabilities. Atualizar não
   amplia capability sem `confirmCapabilities`. Downgrade pelo update responde
   `PluginDowngradeRejected`. Rollback restaura o payload anterior se a
   assinatura ainda vale, a versão não está em quarentena e as capabilities
   já foram concedidas.
5. **Revogação.** `POST …/revoke` grava a versão em
   `integrations.plugin_registry_quarantine` e desliga o plugin e o bot.
   Mensagens já gravadas permanecem. A lista `Revoked` do operador bloqueia a
   mesma versão. Reinstalar a versão revogada responde `PluginVersionRevoked`.
6. **AuthZ.** Rotas de registry exigem `workspace.admin`. Member recebe 403.
   Workspace sem membership recebe 403. Trust root é de instância. Instalação
   e quarentena são do tenant/workspace, com RLS FORCE. Auditoria não leva
   manifesto, assinatura crua nem token.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Baixar o pacote de uma URL allowlisted | O aceite pede mirror sem serviço público; fetch abre SSRF |
| Ed25519 via pacote novo | RSA-PSS já está na BCL e não acrescenta dependência |
| Executar o artefato no processo | D-18 e o pacote R3 proíbem código não confiável in-process |

## Rollback

1. `Integrations:PluginRegistry:Enabled=false`. As rotas de registry respondem 404.
2. `Down` remove só `plugin_registry_installs` e `plugin_registry_quarantine`.
   Bots, mensagens e `integrations.plugins` ficam.
3. Revogar a versão desliga o bot sem apagar histórico.

## Consequências

- **+** Assinatura inválida, checksum adulterado e URL remota não ativam plugin
- **+** Capability nova e downgrade ficam explícitos
- **−** Não há pull de registry público; o operador entrega o índice offline
