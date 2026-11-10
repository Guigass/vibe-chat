# Bots de IA — catálogo

A flag `Features:AiBots:Enabled` nasce **false**. Com ela desligada, criar,
publicar e invocar respondem 404 `AiBotsDisabled`. O chat e as mensagens já
enviadas por um principal `Role.Bot` continuam.

## Publicar e voltar

1. O admin salva um rascunho e publica. A publicação grava uma
   `ai.bot_versions` nova e aponta `ActiveVersionNumber`.
2. Rollback escolhe um número já publicado. As versões posteriores permanecem.
3. Desativar impede invocação nova e não apaga o usuário do bot.

## Falha

- 409 `BotRevisionConflict`: outra gravação chegou antes. Recarregar o bot.
- 400 `ProviderKeyRejected`: a chave do provedor foi recusada e não foi gravada.
- 409 `BotQuotaExceeded` ou `BotBudgetExceeded`: o workspace passou do teto
  (20 bots, USD 50/dia por default).

Não há chamada de modelo nesta etapa. Se o catálogo precisar sair do ar,
basta desligar a flag. Isso não reescreve o histórico.
