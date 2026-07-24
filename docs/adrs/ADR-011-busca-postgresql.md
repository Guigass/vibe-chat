# ADR-011: Busca inicial no PostgreSQL

## Status: Accepted

## Contexto

Usuários precisam encontrar mensagens e canais. Elasticsearch/OpenSearch adicionam cluster, sync e custo operacional. Na fase 1, o volume esperado cabe em FTS do PostgreSQL.

## Decisão

Implementar busca inicial com **PostgreSQL Full-Text Search**:

- `tsvector` em mensagens (e campos relevantes de channels)
- Índice GIN
- Indexação via outbox (`messaging.message.created/edited`)
- Ranking simples + filtros por membership (nunca retornar fora da ACL)
- API `SearchMessages` no módulo Search

Migrar para OpenSearch apenas quando critérios do ADR-016 forem atingidos.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| OpenSearch/ES desde o início | Ops + dual write cedo demais |
| Busca `LIKE %term%` | Performance e relevância ruins |
| Meilisearch | Outro daemon; possível futuro, não fase 1 |
| Busca só client-side | Inviável com histórico grande |

## Consequências

- **+** Zero infra nova; transações e ACL no mesmo DB
- **+** Suficiente para MVP e deploys médios
- **−** Relevância e escala limitadas vs motor dedicado
- **−** Idiomas/stemming precisam configuração (`portuguese` etc.)
