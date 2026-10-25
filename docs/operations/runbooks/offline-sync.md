# Runbook — Sync offline (B-143)

Flag `Features:OfflineWrite:Enabled` (**off** por default). Sem chave utilizável
a API responde 404 `OfflineWriteDisabled`. O chat online e a PWA seguem.

A chave `Features:OfflineWrite:CursorKey` só assina o cursor. Não é token, PEM
nem segredo de mensagem. Vazio ou `CHANGE_ME` mantém a feature fechada.

## Ligar

1. Definir `Features:OfflineWrite:CursorKey` com pelo menos 16 caracteres.
   Não reutilizar senha de banco, SMTP ou API.
2. `Features:OfflineWrite:Enabled=true` e reiniciar a API.
3. No cliente, a pessoa liga “Dados offline neste dispositivo”. Sem isso não
   há fila nem cache.

## Desligar

1. `Features:OfflineWrite:Enabled=false` e reiniciar a API.
2. `GET /api/v1/sync` passa a responder 404. Envios online não mudam.
3. O cliente que ainda tiver fila local não recebe ack novo. A pessoa pode
   apagar os dados offline no próprio aparelho.

## Falha

- `ProtocolUpgradeRequired`: o cliente para a fila. Não reenviar com outra
  chave de idempotência. Atualizar o bundle.
- `CursorExpired` ou `FullResyncRequired`: o cliente troca o cache pelo
  snapshot e preserva comando local ainda não confirmado.
- `MessageVersionConflict`: a edição não grava. A fila mostra o conflito.
- `ScopeRevoked` ou logout: o IndexedDB local é apagado no próximo contato.
- Cursor de outro tenant não devolve o corpo daquele tenant. A membership é
  revalidada em toda leitura.

## Rollback

Não há migration. Reverter é desligar a flag. O histórico em
`GET /api/v1/channels/{id}/messages` permanece.
