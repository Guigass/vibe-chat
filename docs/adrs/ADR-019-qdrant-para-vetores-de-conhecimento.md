# ADR-019: Qdrant para vetores de conhecimento e bots

## Status: Accepted

## Contexto

D-22 e a Wave 18 exigem RAG autorizado para bots internos com fontes em arquivos
e URLs. O índice vetorial precisa continuar opcional, reconstruível e isolado por
tenant; não pode virar fonte da verdade nem substituir a autorização no
PostgreSQL.

Qdrant foi escolhido explicitamente como o serviço vetorial dessa trilha. A
decisão não significa que ele já esteja presente no runtime: a implementação
entra em B-121/B-157, atrás de feature flag e profile Compose opcional.

## Decisão

Adotar **Qdrant self-hosted** como índice vetorial OSS das capacidades de
conhecimento/RAG:

- PostgreSQL permanece source of truth para fontes, chunks, ACL, versões,
  jobs, tombstones e lifecycle;
- MinIO preserva os arquivos originais quando aplicável;
- Qdrant contém vetores e payload mínimo de roteamento/provenance, nunca
  credenciais nem a autorização canônica;
- indexação, reindexação e remoção rodam assincronamente por outbox/Worker;
- retrieval sempre parte de `TenantContext`, aplica filtro obrigatório no
  Qdrant e revalida ACL/estado atual da fonte no PostgreSQL antes de retornar
  qualquer trecho;
- edit, delete, purge, revogação de ACL e troca de modelo de embedding
  invalidam ou reconstroem a projeção;
- conteúdo E2EE não é indexado;
- falha ou indisponibilidade do Qdrant degrada para busca FTS/estado “RAG
  indisponível”, sem afetar chat, envio de mensagem ou dado canônico.

### Isolamento e topologia

O baseline usa uma coleção por **embedding profile** com partição obrigatória
por `tenant_id` e `workspace_id`, payload indexes e uma única porta de acesso
centralizada no módulo AI. Tenant de grande porte pode receber shard dedicado
sem mudar o contrato do domínio.

Cada point inclui, no mínimo:

- `tenant_id`, `workspace_id`;
- `source_id`, `source_version`, `chunk_id`;
- `knowledge_scope_ids` e `bot_scope_ids`;
- `classification`;
- `embedding_profile` e `projector_version`;
- `content_hash` e estado de lifecycle.

Texto integral continua fora do payload vetorial por default. O hit retorna IDs;
o conteúdo é carregado da projeção governada no PostgreSQL somente após a
revalidação.

Filtro no Qdrant é defesa de redução de superfície, não autorização suficiente.
Teste negativo cross-tenant deve provar tanto o filtro quanto a revalidação.

### Operação e segurança

- Serviço em profile Compose opcional `ai`, com volume nomeado e healthcheck.
- Nenhuma porta pública na referência de produção.
- Self-host exige autenticação, rede privada e TLS quando houver tráfego fora
  do host/rede confiável; secrets só via secret store/env.
- API/Worker não usam credencial administrativa para consultas rotineiras;
  separar acesso de gestão/indexação e leitura quando a topologia suportar.
- Métricas: index lag, stale points, delete propagation lag, query latency,
  ACL-denied hits, rebuild e falhas de autenticação.
- Qdrant é rebuildable; backup não substitui a fonte canônica nem a evidência
  de purge.

## Alternativas consideradas

| Alternativa | Motivo de não escolha |
|-------------|-----------------------|
| `pgvector` no PostgreSQL | Menor superfície operacional, mas mistura carga vetorial e OLTP; permanece fallback técnico se o ADR for futuramente superseded com evidência |
| OpenSearch vetorial | Operação maior e continua condicionado aos gatilhos do ADR-016 |
| Índice embutido/in-process | Isolamento, persistência e operação menos claros para múltiplos workers |
| Serviço vetorial proprietário | Contraria self-hosted first e dependências OSS |

## Consequências

- **+** Motor dedicado para filtros + busca vetorial, self-hosted e reconstruível.
- **+** Qdrant pode escalar/particionar sem alterar o domínio canônico.
- **−** Novo serviço, secret, healthcheck, capacidade e runbook.
- **−** Qdrant não oferece RLS PostgreSQL; a porta central, filtros obrigatórios e
  revalidação são invariantes de segurança.
- **−** Mudança de embedding exige versionamento e reindexação controlada.

## Implementação e rollback

B-121 estabelece a base de retrieval/RAG; B-157 adiciona fontes por bot e a
operação explícita do Qdrant. O PR que introduzir o serviço deve atualizar
Compose, configuração, threat model, catálogo de lifecycle e testes R3.

Rollback: desabilitar as flags, parar o profile `ai`, descartar a projeção
vetorial e reconstruí-la depois. Mensagens, páginas, fontes, arquivos e audit
permanecem intactos.

## Referências

- [Qdrant — multitenancy](https://qdrant.tech/documentation/tutorials/multiple-partitions/)
- [Qdrant — security](https://qdrant.tech/documentation/security/)
- [ADR-012 — integração de IA](ADR-012-integracao-ia.md)
- [ADR-016 — gatilhos para OpenSearch](ADR-016-quando-justificar-opensearch.md)

