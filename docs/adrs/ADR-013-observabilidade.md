# ADR-013: Observabilidade

## Status: Accepted

## Contexto

Chat em tempo real falha de formas silenciosas (outbox lag, reconnect storms, vazamentos de auth). Self-hosters precisam de métricas, logs e traces abertos, sem APM proprietário obrigatório.

## Decisão

Adotar stack aberta com **OpenTelemetry** como instrumentação:

| Sinal | Backend |
|-------|---------|
| Métricas | Prometheus |
| Dashboards | Grafana |
| Logs | Loki |
| Traces | Tempo |

- Instrumentar API, Worker e (quando útil) browser via OTel
- Propagar `traceparent` / `correlation_id` através de outbox
- RED/USE dashboards mínimos: request rate, erros, latência, outbox lag, conexões SignalR
- Sem PII desnecessária em logs (tokens, corpos de mensagem em nível default)

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Apenas logs em arquivo | Insuficiente para latência e fan-out |
| Datadog/New Relic obrigatório | Conflita com self-host OSS |
| ELK completo na fase 1 | Pesado; Loki atende logs |
| Sem traces | Debug de outbox→realtime fica cego |

## Consequências

- **+** Diagnóstico padronizado; portable entre ambientes
- **−** Compose mais “cheio” (mas opcionalmente perfilável)
- **−** Cardinalidade de labels precisa de cuidado (não labelar `message_id` em métricas)
