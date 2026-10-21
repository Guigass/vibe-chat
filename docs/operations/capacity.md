# Capacidade, benchmarks e SLOs (B-146)

Objetivos da instalação self-host. **Não são SLA comercial.** O baseline
publicado é um envelope de planejamento (`planning-envelope`, 2026-10-21,
margem `not-measured`), não uma campanha medida.

Flag `Features:CapacitySlo:Enabled` default **false**. Com a flag ligada, só
`workspace.admin` lê `GET /api/v1/workspaces/{id}/capacity`. O painel fica em
`/admin/capacity`.

## Hardware de referência

Um nó Compose: 4 vCPU, 8 GiB, SSD local, Postgres 16, Redis 7, MinIO, uma
réplica de API e uma de worker. É a classe do lab, não um resultado de carga.

## Worksheet

| Perfil | Usuários | Concorrência | Conexões/nó | msg/s | Maior canal | Fan-out | Storage | Retenção | Tenants |
|--------|----------|--------------|-------------|-------|-------------|---------|---------|----------|---------|
| small | 50 | 10 | 50 | 2 | 50 | 50 | 20 GiB | 90 dias | 1 |
| standard | 500 | 100 | 500 | 20 | 200 | 200 | 200 GiB | 365 dias | 5 |
| large | 5000 | 1000 | 2000 | 100 | 1000 | 1000 | 2000 GiB | 365 dias | 25 |

## Recuperação (D-28)

| Perfil | Disponibilidade | RPO | RTO | Vigente |
|--------|-----------------|-----|-----|---------|
| basic | sem objetivo, melhor esforço | ≤ 24 h | ≤ 4 h | sim, não recomendado para operação crítica |
| standard | 99,9% (D-25) | ≤ 1 h | ≤ 4 h | sim |
| ha | 99,95% | ≤ 5 min | ≤ 30 min | não, só depois de B-144 |

## SLIs do perfil Standard

| SLI | Limite |
|-----|--------|
| Disponibilidade da API | 99,9% |
| Latência da API p95 | 500 ms |
| Eco da mensagem p95 | 1 s |
| Lag do outbox p95 | 5 s |
| Lag da busca p95 | 10 s |
| Persistência p95 | 400 ms |
| Entrega realtime p95 | 1 s |
| Reconnect p95 | 3 s |
| Sync p95 | 800 ms |
| Revogação | 30 s |
| Anexo (~1 MiB) p95 | 5 s |
| Restore verificado | drill passou |

Sem amostra medida, o burn rate fica `unmeasured`. Com amostra, razão usa
erro / (1 − objetivo) e latência usa observado / limite. Acima de 1, o
orçamento está queimando.

## Gatilhos (15 min sustentados)

| ADR | Número |
|-----|--------|
| ADR-015 bus | outbox p95 > 30 s e pelo menos 2 workers |
| ADR-016 busca | query p95 > 500 ms com ≥ 5 milhões de mensagens, ou CPU de busca no primário > 40% |
| ADR-017 Kubernetes | réplicas de API > 3, ou conexões/nó > 2000 com restart rolante que o Compose não cobre |

Enquanto o gatilho não fecha, o caminho continua Compose + outbox + FTS.

## Gargalo

| Sinal | Olhar antes de trocar arquitetura |
|-------|-----------------------------------|
| Outbox queimando | Réplica de worker, lote e SKIP LOCKED. Bus só no gatilho da ADR-015 |
| Busca queimando | Índice GIN e réplica de leitura. OpenSearch só no gatilho da ADR-016 |
| Conexões ou réplicas queimando | Escala vertical do nó Compose. Kubernetes só no gatilho da ADR-017 |
| Restore ausente | O backup não conta até o drill passar (D-28) |

## Como rodar

```bash
# smoke: perfil small, 1 VU, 15s
task load:capacity

# benchmark completo no lab (não é gate de PR)
MODE=full PROFILE=standard WORKLOAD=send task load:capacity
```

Workloads: `send`, `history`, `search`, `upload`, `reconnect`, `admin`,
`isolation`. `isolation` espera 403/404 no canal estrangeiro (cross-tenant).
`TENANT_B_USER` liga o cenário `noisy-neighbor`.

O workflow `Capacity benchmark` roda toda segunda e só inspeciona o script
(`k6 inspect`). Não aponta carga para produção. O resultado medido, quando
existir, usa `vibechat.capacity-result.v1` com commit, ambiente, data e margem
de erro, sem e-mail e sem `isSla: true`.

Catálogo compartilhado com o k6: `tests/load/capacity-profiles.json`.
