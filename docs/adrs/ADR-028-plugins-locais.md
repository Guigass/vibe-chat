# ADR-028: Plugin local é manifesto mais identidade de bot

## Status: Accepted

## Contexto

B-110 / W10-14 é a fachada de produto em cima do núcleo B-109 (ADR-027). D-11
continua: sem loja pública. O pacote
`docs/architecture/pacotes-decisao-r3.md` (Plugins) pede manifesto com
assinatura, publisher, callbacks e discovery. Esta fatia instala só
configuração na instância. Registry, assinatura e capabilities extras ficam em
B-066, B-111 e B-137. Divergência registrada aqui, sem reabrir D-11.

## Decisão

1. **O que é um plugin.** Uma linha em `integrations.plugins` mais um bot 1:1
   de B-109. O manifesto é JSON de configuração. Nenhum DLL, JS ou script entra
   no processo da API, do worker ou do browser.
2. **Manifesto v1.** Schema estrito `vibechat.plugin.manifest.v1` com `id`,
   `name`, `version` (`N.N.N`) e `capabilities`. Campo extra é
   `InvalidPluginManifest`. Na fatia 1 a única capability aceita é
   `messages.send`; qualquer outra é `UnknownPluginCapability` (400). Corpo
   acima de 4 KiB é `PluginManifestTooLarge`.
3. **Catálogo.** O único built-in é `incoming-messages` (“Incoming Messages
   API”), compilado no binário. Não há discovery remoto. Instalar exige
   exatamente um de `builtinId` ou `manifest`.
4. **Credencial.** O install cria o bot e o token de B-109 na mesma transação.
   O segredo sai uma vez. Rotacionar delega ao mesmo hash SHA-256. Desinstalar
   revoga o token e apaga a linha do plugin na mesma transação; o perfil do
   bot e as mensagens ficam.
5. **Enable.** `enabled=false` desliga o plugin e o bot. Send novo responde
   403 (`BotDisabled` ou `PluginDisabled`). O histórico não é apagado.
6. **Escopo.** Canais e `allowDms` reusam as regras de B-109. O plugin não
   herda o papel de quem instalou.
7. **Limite.** No máximo 20 plugins por workspace (`PluginLimitReached`).
   Install não é hot path; não há teste de carga separado.
8. **Flag.** Reusa `Integrations:Bots:Enabled` (default false). Off → 404
   `IntegrationDisabled` no admin de plugins e no send. Um segundo kill switch
   poderia deixar o token vivo com a fachada escondida.
9. **UI.** `/admin/plugins` deixa de criar bot cru e passa a instalar o
   built-in ou um manifesto. A API de bots de B-109 permanece.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Segunda flag `Features:Plugins:Enabled` | O catálogo reserva essa flag para B-066; aqui o risco é o mesmo token de B-109 |
| Aceitar capability desconhecida com aviso | A spec prefere rejeitar; aviso vira superfície que a UI teria de traduzir |
| Carregar pacote do manifesto | É o risco que a wave proíbe; código de terceiro fica fora do processo |
| Apagar o bot no uninstall | A mensagem já enviada perderia o autor; o token revogado basta |

## Rollback

1. `Integrations:Bots:Enabled=false`. Install, list, rotate, uninstall e send
   respondem 404. Mensagens já gravadas permanecem.
2. Desinstalar um plugin revoga só o token daquele bot.
3. A migration `Down` apaga `integrations.plugins`. Não apaga bots, tokens nem
   mensagens. Em dados reais, preferir o passo 1.

## Consequências

- **+** O admin instala “Incoming Messages” e o token posta no canal permitido
- **+** Manifesto inválido ou capability fora da fatia 1 não cria identidade
- **−** Revisão profunda de segurança fica para a rodada humana final
  (perfil econômico pré-release)
- **−** Assinatura, publisher e registry continuam fora desta fatia
