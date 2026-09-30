# ADR-026: Webhooks outbound com vários endpoints

## Status: Accepted

## Contexto

B-108 / W10-12 estende o webhook de B-048 (um endpoint por tenant, só
`MessageCreated`). A spec é R3 porque o fan-out sai da rede do tenant com
payload de mensagem e um secret HMAC. O pacote
`docs/architecture/pacotes-decisao-r3.md` não tem seção própria para este item
(ele cobre W11–W17). Vale a regra comum: opt-in, ADR, threat model, timeout,
falha que não derruba o chat e rollback.

Não há decisão D-* nova. ADR-015 continua: sem bus externo. ADR-020 continua:
secret em envelope AES-GCM, AAD no tenant.

## Decisão

1. **Vários endpoints por tenant**, chave `Id`, limite de aplicação **5**.
   A linha única de B-048 ganha `Id` e permanece válida (`Name = default`,
   assinatura só `MessageCreated`).
2. **Assinatura opt-in** por endpoint: `MessageCreated`, `MessageEdited`,
   `MessageDeleted`, `ReactionChanged`. Default = só `MessageCreated`.
   `WebhookTest` não é assinável; sai só no ping do admin.
3. **Filtro de canal** opcional (`uuid[]`). Vazio = todos os canais a que o
   fan-out já se aplica. Id fora do tenant → `400 InvalidChannelFilter`.
4. **Ping** `POST /admin/webhooks/{id}/test` envia JSON sintético
   `WebhookTest`. Não grava mensagem. Atualiza `LastDeliveryAt`,
   `LastStatusCode` e `LastError`.
5. **Entrega** continua no `OutboxProcessor` depois do realtime, timeout 5s,
   sem seguir redirect. Falha HTTP não relança: o outbox da mensagem segue e
   é marcado processado. Sem fila nova e sem retry com DLQ.
6. **Secret** continua mascarado. Rotação por id em
   `POST /admin/webhooks/{id}/rotate`. O rotate legado
   `POST /admin/settings/credentials/webhook/rotate` vale só com 0 ou 1
   endpoint (0 cria a linha `default`; mais de um → `409`).
   O AAD do envelope **permanece o tenant**, para o secret já gravado em
   B-048 continuar descriptografando.
7. **Sem flag global nova.** Zero endpoints = nenhum fan-out (o default de
   B-048). Eventos além de `MessageCreated` ficam desligados até o admin
   marcar. Um kill switch de processo desligaria entregas já configuradas;
   isso não é controle de authZ e quebraria o contrato existente.
8. **`MemberInvited` / `MemberRoleChanged` ficam de fora.** O outbox que
   existe é e-mail (`To`, `Subject`, `BodyText`), não um evento de diretório.
   Reenviar esse JSON para uma URL arbitrária vazaria PII de e-mail. A spec
   condiciona esses nomes a “outbox equivalente”.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Flag de processo que desliga todo webhook | Regressão de B-048; o opt-in por endpoint já é o rollback |
| Trocar o AAD para o id do endpoint | Invalida secrets já gravados |
| Assinar o payload do e-mail de convite | PII fora do canal que o admin escolheu |
| Fila/DLQ dedicada | Fora desta fatia (ADR-015) |

## Rollback

1. Desligar `Enabled` ou apagar endpoints extras. Sem linha, não há entrega.
2. A migration `Down` recusa se algum tenant tiver mais de uma linha. Com no
   máximo uma linha, remove as colunas novas e devolve a PK para `TenantId`.
   Em dados reais, preferir o passo 1.

## Consequências

- **+** Dois destinos e eventos de edição/exclusão/reação sem novo serviço
- **+** O admin vê o último HTTP sem o secret em claro
- **−** Entrega é no mínimo uma vez se um passo posterior do mesmo outbox
  falhar e a mensagem for reprocessada (igual a B-048)
- **−** Revisão profunda de segurança fica para a rodada humana final
  (perfil econômico pré-release)
