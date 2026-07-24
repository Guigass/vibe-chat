# ADR-010: Outbox

## Status: Accepted

## Contexto

Após persistir uma mensagem, precisamos notificar clientes (SignalR), indexar busca, etc. Publicar efeitos colaterais *depois* do commit sem padrão leva a dual-write: mensagem salva sem evento, ou evento sem mensagem.

## Decisão

Adotar o **Transactional Outbox Pattern**:

- Na mesma transação da mutação, gravar `outbox_event`
- `apps/worker` faz polling/claim (SKIP LOCKED), processa e marca `processed_at`
- Consumidores: Realtime, Search, Notifications, Audit, AI (quando aplicável)
- Retries com backoff; dead-letter após N tentativas
- Payloads incluem `tenant_id` e `correlation_id`

Complementos obrigatórios no Messaging:

- **Idempotency keys** no comando de envio
- **Sequence numbers** por conversation

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Publish SignalR inline só no request | Perde fan-out se processo cair pós-commit; multi-instância irregular |
| CDC (Debezium) | Forte, mas infra pesada para fase 1 |
| Bus externo imediato (Kafka) | Ver ADR-015 — injustificado agora |
| 2PC distribuído | Complexidade excessiva |

## Consequências

- **+** Consistência message↔evento; recuperação simples
- **+** Desacopla API do fan-out lento
- **−** Lag do outbox precisa de métricas (`outbox_lag_seconds`)
- **−** Handlers devem ser idempotentes
