# ADR-022: Web Push com VAPID da instância

## Status: Accepted

## Contexto

W10-1 / B-095 exige notificação no navegador quando a aba não está no canal.
D-13 manda Web Push padrão (VAPID) servido pela própria instância — sem FCM/APNs
proprietário. A spec é R3: ADR, threat model, flag e rollback no mesmo PR.

## Decisão

1. **Assíncrono via outbox** de `MessageCreated` — nunca no hot path de
   `SendMessage` (mesmo padrão de B-048 / B-091).
2. **VAPID da instância** — pública no GET; privada em envelope AES-GCM no
   DB quando `RuntimeSettings:DatabaseOverridesEnabled` (B-187 / ADR-020).
   Fallback: default de código (push no-op). Keyring só no env.
3. **Assinatura por dispositivo** em `notifications.push_subscriptions`
   (`TenantId`+`UserId`+`Endpoint` único); RLS + query filter.
4. **Destinatários no momento do envio:** membership atual ∩ (DM 1:1 **ou**
   menção direta) ∩ `PushEnabled` ≠ false ∩ autor excluído. Cursor persistente
   de B-094 suprime se `lastReadSeq >= sequence`. Preferências ricas / DND /
   “todas as mensagens” por canal ficam em B-097.
5. **Payload mínimo** (D-13): remetente, canal, prévia truncada, ids para o
   clique. Sem body completo. Formato ngsw para o service worker abrir `/app`.
6. **Kill switch** `Push:Enabled` (default `false`). SoT no DB de instância
   quando overrides on (B-187); senão default de código. Sem chaves VAPID
   válidas o sender é no-op. Falha de push **não** reprocessa o outbox.
7. **410/404** do push service remove a assinatura. Biblioteca OSS MIT
   `Lib.Net.Http.WebPush` só na infra.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| FCM / APNs | Viola D-13 e o perímetro self-host |
| Envio síncrono no `SendMessage` | Viola ADR-010; timeout derruba UX de envio |
| Lista de destinatários gravada na assinatura | Quem saiu do canal continuaria recebendo |
| Preferências DND nesta wave | Escopo de B-097; default = menções + DM |

## Rollback

1. `Push:Enabled=false` — para novos envios; o cliente trata `enabled:false`
   sem erro.
2. Rotacionar o par VAPID (runbook) invalida assinaturas; o usuário opta de
   novo.
3. Reverter migration só em lab; em dados reais preferir flag off.

## Consequências

- **+** Notificação fora da aba sem SaaS de push; isolamento por tenant
- **+** Chat continua se o push service falhar
- **−** Prévia curta pode aparecer em tela bloqueada — mitigado por truncar
- **−** Worker faz egress HTTPS aos push services (FCM/Mozilla/Apple Web Push)
