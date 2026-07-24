# ADR-005: PostgreSQL

## Status: Accepted

## Contexto

VibeChat precisa de source of truth relacional com transações ACID (mensagem + outbox), constraints de unicidade (seq, idempotency), Full-Text Search inicial e Row Level Security para multi-tenancy.

## Decisão

Usar **PostgreSQL** como banco principal e source of truth:

- Todas as entidades de negócio persistidas no Postgres
- Migrações versionadas (EF Core Migrations ou Flyway/Liquibase — preferência do time de backend, uma só)
- FTS (`tsvector`/`GIN`) para busca inicial (ADR-011)
- RLS por `tenant_id` (ADR-009)

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| MySQL/MariaDB | RLS e FTS menos adequados ao desenho proposto |
| SQL Server | Custo/licensing em self-host OSS; Postgres é padrão da comunidade |
| MongoDB como SoT | Transações e invariantes de seq/idempotência mais frágeis |
| SQLite | Inadequado para multi-usuário concurrent self-host |

## Consequências

- **+** Transação única message+outbox; constraints fortes
- **+** RLS como defesa em profundidade
- **−** Operação exige backup/VACUUM/monitoramento (docs/operations)
- **−** Escala de busca/analytics pode exigir OpenSearch depois (ADR-016)
