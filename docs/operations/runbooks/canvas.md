# Canvas colaborativo (B-152 / ADR-068)

A flag `Features:CanvasRealtime:Enabled` nasce **false**. Com ela desligada o
chat e as páginas B-120 seguem no PATCH. Não há serviço novo.

## Falha

1. Confirme `Features__CanvasRealtime__Enabled` no ambiente efetivo.
2. Update recusado (`CanvasUpdateRejected`, `UnsafeKnowledgeBody`,
   `CanvasQuotaExceeded`, `CanvasStateTooLarge`) não grava. O body anterior
   permanece.
3. `CanvasResync` ou `CanvasEpochMismatch` manda o cliente reler o estado.
   Não reenvie o update antigo: o epoch mudou no restore.
4. Presença sumiu depois de restart da API. É esperado: cursor não é durável.
5. Purge não apagou updates. Veja hold ativo no workspace
   (`knowledge.canvas.preserve`).

## Rollback

1. Desligue a flag. Novas rotas `/canvas` respondem 404
   `CanvasRealtimeDisabled`.
2. O texto projetado em `knowledge.pages` continua na leitura e no PATCH.
3. Só rode o `Down` da migration `AddCanvasCollaboration` se o estado CRDT
   puder ser descartado. Páginas e versões não saem com ele.

## Capacidade

Update até 48 KB. Estado até 64 KB. Texto até o teto já existente da página
(16 000). Compactação a cada 20 updates. No máximo 30 snapshots e 200 updates
vivos por epoch. Acima disso a rota responde `CanvasQuotaExceeded`.
