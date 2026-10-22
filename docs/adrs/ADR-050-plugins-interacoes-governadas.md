# ADR-050: Interações de plugin são dados, grant e callback

## Status: Accepted

## Contexto

ADR-049 deixou o plugin como manifesto, bot e callback HMAC. B-111 precisa de
botões, leitura de histórico e anexos sem entregar o workspace inteiro e sem
executar JS ou DLL de terceiro.

## Decisão

1. **Mesma flag.** `Features:Plugins:Enabled` continua default false. As
   capabilities novas são `messages.interact`, `history.read` e
   `attachments.send`. Off responde 404 `PluginPlatformDisabled`. `messages.send`
   de B-109/B-110 não muda.
2. **Ações são dados.** O manifesto declara `actions[]` (`name` + `description`).
   A mensagem guarda o cartão (`pluginId`, nome, expiração, ações namespaced).
   O browser não carrega código do plugin. O built-in `action-ack` confirma no
   servidor. Manifesto customizado só recebe POST HMAC `plugin.action.invoked`.
3. **Histórico é grant separado.** Canal no escopo do bot não autoriza leitura.
   O admin concede uma janela de no máximo 7 dias. Fora do grant a resposta é
   404 `PluginHistoryGrantNotFound`, igual para canal inexistente. Janela
   parcial é 403 `PluginHistoryWindowDenied`. Revogar grava `RevokedAt` e a
   leitura seguinte falha na hora.
4. **Anexo reusa o pipeline de arquivos.** Tipo, tamanho, quota de storage,
   tenant e scan são os da API de anexos. Nomes `.js`, `.dll`, `.exe` e
   `.html` e content-types de script são recusados antes do upload. O envio
   com `attachmentIds` exige `attachments.send`.
5. **Quota e idempotência.** Cada capability governada tem teto diário por
   plugin. A mesma idempotency key não consome de novo e não dispara o
   callback outra vez depois de `Accepted`. Falha de callback fica `Failed` e
   a mesma chave pode repetir o POST.
6. **Audit.** Grant, revoke, leitura, mensagem interativa, ação e anexo entram
   em `audit.audit_events`. Sem corpo de mensagem nem segredo.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Reusar só o escopo de canal do bot como leitura | A spec exige grant de janela separado da membership |
| Executar o `ack` como assembly carregado | É o runtime que B-066 e B-111 proíbem |
| Segunda flag | O catálogo já reserva `Features:Plugins:Enabled` |

## Rollback

1. `Features:Plugins:Enabled=false`. Interação, histórico e anexo de plugin
   param. Mensagens já gravadas e `messages.send` permanecem.
2. Revogar o grant ou desinstalar o plugin bloqueia leitura e ação novas.
3. A migration `Down` remove as quatro tabelas de grant, oferta, recibo e
   quota, e a coluna `LastUsedAt`. Não apaga mensagens nem anexos.

## Consequências

- **+** O admin vê capability, janela, último uso, quota e revoga na hora
- **+** Um token não descobre canal fora do grant
- **−** SDK e registry continuam em B-136/B-137
