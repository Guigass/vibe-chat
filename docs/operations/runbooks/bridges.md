# Runbook — Bridges (B-138)

Flag `Integrations:Bridges:Enabled` (**off** por default). Com a flag desligada
a API responde 404 `BridgesDisabled` e não abre tráfego externo. O chat segue.
Mensagens já copiadas permanecem no canal.

## Falha

| Sintoma | O que olhar | Ação |
|---------|-------------|------|
| Admin não cria ponte | Flag, `InstanceId`, `PinnedHosts` e papel admin | Sem pin ou com URL `http`/`https` a criação responde `BridgeDestinationUnpinned` ou `BridgeEgressBlocked`. Não há fetch |
| Evento repetido | `integrations.bridge_events` | O mesmo `eventId` devolve a mensagem existente e não cria outra |
| Eco da própria instância | `originInstanceId` igual a `InstanceId`, ou id `local:` | Outcome `loop`. Nenhuma mensagem nova |
| Cópia bloqueada | audit `integration.bridge.copy.blocked` | DLP `federation` não prosseguiu. O corpo não sai no pull |
| Ponte desligada ainda recebe | `status` e bot `Enabled` | `disconnect` desativa o bot. Inbound e outbound respondem `BridgeTrafficBlocked` |
| Delete remoto “sumiu” | `deleteRemoteGuaranteed` | Permanece false. Sem capability `deletes`, a mensagem local fica e o audit `integration.bridge.delete.limited` registra a limitação |

## Rollback

1. `Integrations:Bridges:Enabled=false` e reiniciar API e worker.
2. Ou `POST /api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/disconnect`.
3. O chat não muda. Mensagens já gravadas permanecem.
4. Migration `Down` só se for preciso remover `integrations.bridges`, `bridge_identities` e `bridge_events`.

## Capacidade

Uma ponte por canal. Pull de até 50 mensagens por chamada. Envelope
`vibechat.bridge.envelope.v1`. A API não abre conexão de saída para a rede
remota; o processo externo usa o token do bot, mostrado uma vez na criação.
