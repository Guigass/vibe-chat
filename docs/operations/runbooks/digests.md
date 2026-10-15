# Digests e catch-up (B-122)

## Quando usar

O usuário não recebe o resumo programado, o catch-up responde 404, ou a
entrega externa (e-mail/push) ficou em `partial`.

## Flag

`Notifications:Digests:Enabled` default **false** na API e no worker.

- Off: `GET/PUT /api/v1/notifications/digests/settings` e o catch-up respondem
  `DigestDisabled` (404). O worker não varre. Mensagens e a inbox seguem.
- On: agenda em `notifications.digest_schedules`; geração idempotente em
  `digest_runs`.

## Falha

1. Confirmar health da API e do worker.
2. Ver `AiStatus` e `Status` do run. `failed` na IA não apaga os itens.
3. `externalDeferred` com DND ativo é esperado até a janela acabar
   (`NextRetryAt`, no máximo 3 tentativas).
4. E-mail/push desligados não enviam corpo. A página `/app/catch-up` é a
   entrega. O texto externo, quando sai, é só contagem.
5. Audit `digest.generated` não contém body. Métrica
   `vibechat.digests.generated`.

## Rollback

1. `Notifications:Digests:Enabled=false` e reiniciar API e worker.
2. Não apagar mensagens nem cursores.
3. Se o schema precisar sair: `Down` da migration `AddDigests` remove só as
   três tabelas de digest.

## Capacidade

20 agendas por ciclo de 15 s. 30 itens por seção. Sem fan-out para o canal.
