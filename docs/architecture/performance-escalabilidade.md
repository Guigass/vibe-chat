# Performance e escalabilidade

Revisão B-170 da topologia vigente (Compose, monólito modular, outbox, FTS).
Não é SLA e não autoriza bus, OpenSearch nem Kubernetes. Os números que
fecham essa porta continuam os de [ADR-048](../adrs/ADR-048-capacity-slos.md),
confirmados aqui sem alteração. Amostra de lab:
[`tests/load/performance-baseline.json`](../../tests/load/performance-baseline.json).

## Hot paths

| Id | Dono | Métrica | Baseline B-146 |
|----|------|---------|----------------|
| `send-message` | Messaging | `vibechat.messages.sent` | persistência p95 400 ms |
| `history-sync` | Messaging | HTTP history | sync p95 800 ms |
| `signalr-fanout` | Realtime | `vibechat.realtime.connections` | entrega realtime p95 1 s |
| `outbox-worker` | Worker | `building_blocks.outbox_messages` | lag p95 5 s |
| `fts-search` | Search | HTTP search | lag de busca p95 10 s |
| `upload-presign` | Files | HTTP upload | anexo ~1 MiB p95 5 s |
| `admin-export` | Administration | HTTP export | latência de API p95 500 ms |

`send-message` grava mensagem, `seq` e outbox na mesma transação. O fan-out
sai no worker, nunca no pedido. IA, miniatura e preview ficam fora desse
caminho. History pagina por `seq` com os índices `(ConversationId, Sequence)`
e `(TenantId, ConversationId, CreatedAt)`. O worker lê no máximo 20 linhas
pendentes (`ProcessedAt`, `OccurredAt`). Upload devolve URL pré-assinada; o
byte não passa por `SendMessage`.

## Como a instalação cresce

| Peça | Limite hoje | Alavanca |
|------|-------------|----------|
| API | Réplicas sem estado atrás do proxy | Outra réplica. SignalR usa o backplane Redis |
| Worker | Uma réplica, lote de 20 | Segunda réplica só com `SKIP LOCKED`. Bus só na ADR-015 |
| Postgres | Um primário com `FORCE RLS` | Nó maior. Réplica de leitura antes de OpenSearch |
| Redis | Presença, typing, rate limit, backplane | Chave continua com prefixo de tenant. Queda degrada o realtime, não o histórico |
| MinIO | Objeto e URL pré-assinada | Ciclo de vida e CDN ficam em B-145 |
| Vizinho barulhento | RLS em tabela de tenant | Workload `isolation` espera 403/404. Quota tem flag própria |
| Contrapressão | HTTP 429 e teto do lote | Não se abre mão de `seq` nem de RLS para aliviar carga |

O perfil de planejamento Standard (B-146) continua 500 usuários, 100 de
concorrência, 500 conexões por nó e 20 msg/s. Large e HA não são o alvo
desta revisão. B-144 só entra com evidência própria.

## Gatilhos confirmados

Janela de 15 minutos. Amostra mais curta não fecha gatilho.

| ADR | Fecha quando |
|-----|----------------|
| ADR-015 | lag p95 do outbox > 30 s e pelo menos 2 workers |
| ADR-016 | query de busca p95 > 500 ms com ≥ 5 milhões de mensagens, ou CPU de busca no primário > 40% |
| ADR-017 | réplicas de API > 3, ou conexões por nó > 2000 e o restart rolante já não cabe no Compose |

Enquanto isso, o caminho é Compose + outbox + FTS.

## Gargalos adiados

Não houve otimização de código nesta revisão. Cada item abaixo já tem
alavanca dentro da arquitetura atual e só muda com medição.

| Id | Caminho | Motivo |
|----|---------|--------|
| `outbox-skip-locked` | `outbox-worker` | Um worker. O portão do processo evita lote duplicado nele. `SKIP LOCKED` entra com o segundo worker, se o lag medido pedir |
| `outbox-batch` | `outbox-worker` | Lote 20, handlers em sequência. Aumentar sem lag medido alonga a transação |
| `search-on-primary` | `fts-search` | A ADR-016 exige volume ou CPU que esta amostra não tem |
| `compose-connections` | `signalr-fanout` | A ADR-017 exige réplicas ou 2000 conexões. O lab HTTP não abre esses sockets |

A campanha k6 mede HTTP. Lag do outbox sai do SQL (`processed_at - occurred_at`
e idade do pendente). Conexões SignalR, CPU de busca e lag de indexação
ficam sem amostra (`null`): o painel Grafana já observa
`vibechat.realtime.connections` e o histograma de dispatch. Não são zero.

## Quando o orçamento queima

SLI de [capacity.md](../operations/capacity.md). Burn de latência =
observado / limite. Acima de 1, o orçamento queima. Procedimento:
[runbook](../operations/runbooks/performance.md).

## Regressão

- `task load:smoke`
- `task load:performance` no lab (não é gate de PR)
- `task test:security` — o workload `isolation` espera 403/404
- `FORCE RLS` intacto
- chave Redis com prefixo de tenant

Ambiente, commit, hardware e margem acompanham o JSON. O número envelhece
e não vira promessa comercial.
