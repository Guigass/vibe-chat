# ADR-006: Redis

## Status: Accepted

## Contexto

Presença, typing indicators, cache de leituras quentes, rate limiting e backplane SignalR são dados efêmeros ou de coordenação — não devem sobrecarregar o PostgreSQL.

## Decisão

Usar **Redis** para:

| Uso | Padrão |
|-----|--------|
| Presence | Keys com TTL + heartbeat |
| Typing | Keys curtas com TTL (ex.: 3s) |
| Cache | Cache-aside de projeções baratas |
| Rate-limit | Contadores/sliding window |
| SignalR backplane | Redis backplane oficial |

PostgreSQL permanece source of truth. Redis é **descartável**: perda de dados Redis não corrompe mensagens.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Somente PostgreSQL | Contenção e latência ruins para typing/presence |
| Memcached | Sem estruturas e pub/sub adequados ao backplane |
| NATS para presence | Introduz bus externo cedo demais (ADR-015) |

## Consequências

- **+** Latência baixa para presença/typing; escala SignalR
- **+** Rate-limit distribuído simples
- **−** Mais um componente no Compose
- **−** Evitar stored-state crítico no Redis (não persistir mensagens lá)
