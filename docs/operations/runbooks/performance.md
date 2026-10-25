# Performance — hot paths e orçamento de latência (B-170)

Olhar isto quando a API, o eco, a busca ou o outbox passam do limite do
perfil Standard. Os limites estão em [`capacity.md`](../capacity.md). O
modelo está em [`performance-escalabilidade.md`](../../architecture/performance-escalabilidade.md).

Não é SLA. Uma amostra curta de lab não fecha ADR-015, ADR-016 nem ADR-017.

## O que olhar

| SLI | Limite Standard | Onde |
|-----|-----------------|------|
| Latência da API p95 | 500 ms | HTTP / trace |
| Eco da mensagem p95 | 1 s | envio + fan-out |
| Lag do outbox p95 | 5 s | idade do pendente e `processed_at - occurred_at` |
| Lag da busca p95 | 10 s | tempo até a mensagem aparecer na busca |
| Conexões SignalR | gatilho em 2000/nó | `vibechat.realtime.connections` |

Burn = observado / limite. Acima de 1, o orçamento de latência está queimando.

## O que fazer

1. Confirmar worker de pé e Redis acessível. Realtime pode degradar; o
   histórico continua no Postgres.
2. Outbox queimando com um worker: olhar lote, erro e dead-letter. A
   alavanca é o worker, não um bus. Segunda réplica só com `SKIP LOCKED`.
   Bus externo só se o lag p95 passar de 30 s por 15 minutos com pelo
   menos dois workers (ADR-015).
3. Busca queimando: índice GIN e plano no primário. OpenSearch só no
   gatilho da ADR-016 (p95 de query > 500 ms com ≥ 5 milhões de mensagens,
   ou CPU de busca > 40% por 15 minutos).
4. Conexões ou réplicas queimando: crescer o nó Compose. Kubernetes só no
   gatilho da ADR-017.
5. Vizinho barulhento: a RLS continua. Não desligar RLS para “ganhar”
   performance. Quota é outra flag.

## Como repetir a amostra

```bash
# lab: Small e Standard, taxa limitada, 20s por workload
task load:performance

# janela longa, ainda no lab, sem apontar para produção
MODE=full PROFILE=standard WORKLOAD=send task load:performance
```

O resultado versionado leva commit, ambiente, hardware e margem. Sem
e-mail, sem corpo de mensagem, sem `isSla: true`.

## Rollback

Não há flag nem migration neste item. Reverter o documento e o catálogo
não muda o caminho de mensagem.
