# ADR-023: DM em grupo reutiliza Channel

## Status: Accepted

## Contexto

W10-7 / B-101 pede conversa com 3–9 pessoas sem criar canal de workspace.
A spec é R3: flag off por default, ADR, threat model e rollback no mesmo PR.
Messaging já exige `seq`, idempotência e outbox — um agregado paralelo
duplicaria o hot path.

## Decisão

1. **Reusar `Channel` com `Type = GroupDm`** — sem entidade nova de conversa.
2. **Membership é a lista de participantes.** `JoinedSeq` esconde o histórico
   anterior à entrada; `LeftAt`/`LeftSeq` encerram o acesso (history e hub).
3. **Get-or-create** por `ParticipantSetKey` (GUIDs ordenados) no workspace.
   Adicionar alguém a uma DM 1:1 cria **outra** conversa.
4. **Mensagens de sistema** entram pelo `IMessageWriter` (seq + outbox).
5. **Kill switch** `Directory:GroupDm:Enabled` default `false`. Lab/Development
   e TestHost ligam a flag.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Agregado `GroupConversation` | Duplicaria seq/outbox/hub |
| Promover 1:1 in-place | A DM original precisa permanecer |
| Ver histórico completo ao entrar | Vazamento; a spec exige janela por seq |

## Rollback

1. `Directory:GroupDm:Enabled=false` — criação/add/leave/rename respondem 404.
   History existente continua para quem ainda é membro.
2. Reverter a migration só em lab; em dados reais preferir flag off.

## Consequências

- **+** Mesmo caminho de messaging, RLS e hub das DMs 1:1
- **+** Teste de segurança dedicado na janela de entrada
- **−** Conjunto idêntico reabre a mesma conversa (idempotência intencional)
