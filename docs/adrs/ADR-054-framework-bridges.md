# ADR-054: Framework de bridges por contrato externo

## Status: Accepted

## Contexto

B-136 entrega o SDK sem rede externa. D-21 pede bridges com escopo explícito
antes de federação, identidade remota marcada e cópia de dados visível. B-138
precisa negociar capabilities, impedir loop e replay, e parar o tráfego ao
desligar, sem executar código remoto dentro da API.

## Decisão

1. **Flag.** `Integrations:Bridges:Enabled` nasce false (binding de
   `Features:Bridges:Enabled`). Off responde 404 `BridgesDisabled`. O chat não
   depende desta flag.
2. **Contrato fora do processo.** O protocolo é `vibechat.bridge.envelope.v1`
   (schemaVersion 1), no formato de evento versionado de B-139, entregue pela
   API escopada. A API não abre socket, não segue redirect e não baixa arquivo.
   O processo externo puxa envelopes e empurra eventos autenticado como o bot
   da ponte. `http`/`https` no destino responde `BridgeEgressBlocked`.
3. **Pin.** O host de destino tem de estar em `PinnedHosts`. Não há discovery
   pública. A instância tem `InstanceId`; um evento com essa origem, ou o id
   de um envelope que esta instância já emitiu, é loop e não vira mensagem.
4. **Plumb.** Uma ponte liga um canal (não DM) a uma sala remota opaca. A
   identidade da ponte é um bot com escopo só nesse canal. Identidades remotas
   ficam numa tabela de mapping opaco e nunca substituem um usuário local.
5. **Consentimento.** O estado nasce `pending`. Nada entra ou sai antes de
   `acknowledgeCopy`. Pausar bloqueia os dois lados; retomar volta ao ativo.
   Desligar desativa o bot e responde `BridgeTrafficBlocked`.
6. **Capabilities.** A interseção de `messages`, `edits`, `deletes`,
   `reactions` e `files` precisa incluir `messages`. Delete remoto não é
   garantido (`deleteRemoteGuaranteed` permanece false). Sem a capability
   `deletes`, o evento é auditado como `integration.bridge.delete.limited` e
   a mensagem local fica.
7. **Idempotência.** O mesmo `eventId` não grava outra mensagem. O pull avança
   um checkpoint e não reemite o corpo. DLP `federation` que não prossegue
   omite o corpo e audita `integration.bridge.copy.blocked` sem o texto.
8. **AuthZ.** Rotas de gestão exigem `workspace.admin`. A leitura do canal
   exige membership e `message.read`. O token do bot só age na própria ponte.
   RLS FORCE em `integrations.bridges`, `bridge_identities` e `bridge_events`.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Cliente HTTP da API para a rede remota | O pacote R3 exige o bridge fora do processo e abre SSRF |
| Matrix ou ActivityPub nesta fatia | B-065 é a federação; D-21 manda o bridge limitado primeiro |
| Apagar local prometendo delete remoto | A spec proíbe garantir delete do outro lado |
| Postar como o usuário local | Puppeting silencioso está fora de escopo |

## Rollback

1. `Integrations:Bridges:Enabled=false`. As rotas respondem 404 e o pull/push param.
2. `POST …/disconnect` desativa o bot. Mensagens já copiadas permanecem.
3. `Down` remove só `bridges`, `bridge_identities` e `bridge_events`. Bots,
   mensagens e canais ficam.

## Consequências

- **+** Loop, replay e host sem pin não copiam conteúdo
- **+** A limitação de delete fica na resposta, no canal e na auditoria
- **−** Não há entrega push nem protocolo de terceiros nesta fatia
