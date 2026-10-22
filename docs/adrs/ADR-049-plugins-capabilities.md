# ADR-049: Capabilities de plugin são configuração e callback

## Status: Accepted

## Contexto

ADR-028 instalou o plugin como manifesto mais bot e aceitou só
`messages.send`. B-066 adiciona slash, assinatura de eventos e ganchos de UI
sem loja pública (D-11) e sem executar código de terceiro. A flag lógica
`Features:Plugins:Enabled` já estava reservada no catálogo.

## Decisão

1. **Flag.** `Features:Plugins:Enabled` default false, escopo de instância
   (o mesmo binding dos outros `Features:*`; não há store de flag por tenant).
   Off rejeita capabilities além de `messages.send` com 404
   `PluginPlatformDisabled` e não dispara callback. Plugins de envio de
   B-109/B-110 continuam na flag `Integrations:Bots:Enabled`.
2. **Manifesto v1.** As capabilities conhecidas passam a ser `messages.send`,
   `commands.slash`, `events.subscribe` e `ui.hooks`. Campo fora dessa lista
   continua `UnknownPluginCapability`. `commands`, `callback` e `hooks` são os
   únicos objetos novos. `entry`, script ou pacote continuam
   `InvalidPluginManifest`.
3. **Catálogo.** O binário ganha o built-in `slash-note` (`/slash-note/nota`).
   Não há endpoint de loja, registry ou download. `GET …/plugins/builtins`
   lista só o que está compilado.
4. **Slash.** O comando entra na lista de B-087 com namespace `plugin/comando`.
   A execução exige capability, plugin ligado e canal no escopo do bot. O
   built-in publica a nota como o bot (`AuthorIsBot`). Manifesto customizado
   não roda código: chama a URL de callback.
5. **Callback.** HTTPS com HMAC `X-VibeChat-Signature` (paridade B-048).
   `http://127.0.0.1` e `localhost` só para lab. Host metadata, IP privado e
   redirect são recusados. O segredo sai uma vez e fica em envelope AES-GCM.
   Eventos de mensagem saem pelo outbox, depois do realtime. O eco do próprio
   bot não é reenviado.
6. **Ganchos.** Só `message.badge.bot` e `message.menu`, como dados. O browser
   não carrega JS do plugin.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Schema v2 paralelo | A spec versiona o mesmo `vibechat.plugin.manifest.v1` |
| Executar o handler do built-in como assembly carregado | É o runtime que a wave proíbe |
| Segunda tabela de deliveries | O efeito cabe no callback síncrono do comando e no outbox já existente |

## Rollback

1. `Features:Plugins:Enabled=false`. Slash avançado e callbacks param. Mensagens
   já gravadas e o envio `messages.send` permanecem.
2. Desinstalar o plugin revoga o token e apaga a linha, inclusive o segredo.
3. A migration `Down` remove colunas de callback, eventos e ganchos. Não apaga
   bots nem mensagens.

## Consequências

- **+** O admin instala `slash-note` e o comando publica só no canal concedido
- **+** Callback de outro tenant não recebe o evento
- **−** Registry, assinatura de pacote e SDK continuam em B-136/B-137
- **−** Revisão profunda de segurança fica para a rodada humana final
