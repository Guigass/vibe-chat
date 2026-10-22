# ADR-051: SDK de plugins deriva do contrato HTTP

## Status: Accepted

## Contexto

B-066 e B-111 deixaram o plugin como manifesto, bot e callback HMAC. B-139
versiona os eventos públicos no repositório. B-136 precisa de um SDK oficial
sem criar um segundo contrato e sem publicar pacote em feed externo.

## Decisão

1. **Flag.** `Integrations:PluginSdk:Enabled` nasce false. Off responde 404
   `PluginSdkDisabled`. O chat, o send de B-109 e o portal de B-135 não dependem
   desta flag.
2. **Contrato canônico.** O verificador reusa `sha256=` de B-048. A matriz
   aceita só o SDK `1.0.0` com o contrato HTTP `1` e as versões presentes em
   `PublicEventCatalog`. `messaging.message.created` v1 deixa de ser compatível
   em 2027-04-19. Versão desconhecida não é compatível.
3. **Replay no cliente.** A API não guarda um cache global de delivery id. O
   SDK recebe os ids já vistos e recusa replay, assinatura inválida e entrega
   fora da janela de 5 minutos (skew de 1 minuto).
4. **Sandbox sintético.** O segredo `sandbox-not-a-secret` só vale na rota de
   verificação sintética e não é credencial de tenant. O quickstart não grava
   mensagem, não chama URL externa e ignora `tenantId` no body. O token não
   entra em log: a única formatação de log é o redator que troca `vc_int_` /
   `vc_cb_` / `Bearer`.
5. **Pacotes no repositório.** .NET em `sdks/dotnet/VibeChat.Sdk` e TypeScript
   em `sdks/typescript`, licença Apache-2.0, `published: false`. Publicar num
   feed é ação externa e não faz parte deste item.
6. **AuthZ.** Leitura do catálogo, verificação, contract-test e sandbox exigem
   `integration.read`. Member recebe 403. Workspace de outro tenant sem
   membership recebe 403. Auditoria `developer.sdk.*` não leva corpo nem segredo.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Gerar um cliente OpenAPI paralelo | A spec manda derivar do contrato, não substituí-lo |
| Publicar no NuGet e no npm neste PR | Feed externo é R4; o pacote fica reproduzível no repo |
| Cache de replay dentro da API | O consumidor é quem vê a repetição; a API não vira cofre de delivery id |

## Rollback

1. `Integrations:PluginSdk:Enabled=false`. As quatro rotas respondem 404.
2. Não há migration. Mensagens, bots e callbacks permanecem.
3. Remover os projetos `sdks/` não altera o runtime do chat.

## Consequências

- **+** Bot, slash, consumidor de evento e connector de exemplo nascem sintéticos
- **+** A matriz e os vetores de assinatura têm teste .NET e TypeScript
- **−** Registry assinado continua em B-137
