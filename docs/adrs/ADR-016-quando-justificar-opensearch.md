# ADR-016: Quando justificar Elasticsearch / OpenSearch

## Status: Accepted

## Contexto

A busca inicial usa PostgreSQL FTS (ADR-011). Motores dedicados oferecem relevância, facetas e escala, mas exigem cluster, sync e operação. Este ADR define quando a mudança se justifica.

## Decisão

**Manter PostgreSQL FTS** até evidências de produto/ops exigirem motor dedicado (**OpenSearch** preferível a ES proprietário em self-host OSS).

### Critérios de gatilho

1. **SLOs de busca** (p95 latência / relevância) falham em volumes reais após otimização de índices GIN e hardware razoável
2. Requisitos de **linguagem** avançados (sinônimos complexos, multilíngue pesado, typo-tolerance fino) não atendidos pelo FTS
3. Necessidade de **busca unificada** (mensagens + arquivos + people + apps) com facetas e highlights ricos
4. **Carga de escrita/leitura** de search compete com OLTP e degrada o Postgres primário (e read replica dedicada ainda não basta)
5. Produto exige **busca semântica/vetorial** em escala onde pgvector + ops ainda não atendem (avaliar pgvector antes de OpenSearch)

### Princípios se adotar

- Indexação continua via **outbox** (nada de dual-write no request)
- ACL aplicada na indexação **e** na query (nunca confiar só no índice)
- OpenSearch **não** é source of truth
- Plano de reindexação documentado

## Alternativas consideradas

| Alternativa | Motivo |
|-------------|--------|
| ES/OS desde o dia 1 | Ops prematura |
| Meilisearch/Typesense | Opções válidas menores; avaliar se gatilhos forem leves |
| pgvector apenas | Pode cobrir semantic search antes de OS — preferir se suficiente |

## Consequências

- **+** Evita cluster de busca cedo
- **+** Caminho de migração claro via outbox
- **−** Até migrar, features avançadas de search ficam limitadas
- **−** Migração exigirá backfill e validação de ACL
