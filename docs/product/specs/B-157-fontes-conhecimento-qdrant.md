# B-157 — Fontes de conhecimento para bots e Qdrant

> Wave W18-3 · Trilha A/B/C/D/E/AI · Deps: B-120, B-121, B-131, B-155 · Decisões: D-17, D-22 · ADRs: ADR-019 · Risco R3
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Bots internos precisam responder a partir de arquivos e URLs da empresa. Sem
pipeline governado, vetores podem cruzar tenants, sobreviver à exclusão ou
recuperar conteúdo que o solicitante já não pode ler.

## Escopo

- `KnowledgeSource` por arquivo, página B-120 ou URL HTTPS.
- Upload via Files, scan/quarentena B-131, classificação, ACL, hash e versão.
- Fetch de URL com SSRF/egress protection, redirect/DNS/IP revalidation,
  allowlist, limites de tipo/tamanho/tempo; snapshot único por default.
- Parse, normalização, chunking, embedding e indexação assíncrona idempotente.
- PostgreSQL como SoT de fonte/chunk/job/tombstone; MinIO para original; Qdrant
  como vetor/projeção reconstruível conforme ADR-019.
- Knowledge collections e grants explícitos por bot.
- Reindex, pause/resume, status, contagem/checksum, lag e dead-letter.
- Edit/delete/purge/ACL revoke/model change propagados com SLO e evidência.
- Retrieval com filtro tenant/workspace/bot e revalidação de ACL da fonte.
- Citações navegáveis e estado “sem evidência suficiente”.

## Fora de escopo

- Crawl irrestrito de site, browser com login ou execução de JavaScript remoto.
- Treinar modelo com conteúdo do tenant.
- Indexar E2EE.
- Qdrant como source of truth, ACL canônica ou export de custódia.

## Contratos

Entidades `ai.knowledge_sources`, `ai.knowledge_source_versions`,
`ai.knowledge_chunks`, `ai.knowledge_ingestion_jobs`,
`ai.bot_knowledge_grants`. Eventos `knowledge.source.accepted.v1`,
`knowledge.source.changed.v1`, `knowledge.source.removed.v1` e
`knowledge.index.completed.v1`. Endpoints admin de fontes/collections/jobs e
endpoint interno de retrieval; tenant nunca vem do body.

Flags `Features:BotKnowledge:Enabled=false` e dependência opcional Qdrant.
Compose/profile/config/runbook entram no PR de implementação.

## UX

Admin vê fonte, classificação, escopo, versão, `Queued/Scanning/Parsing/
Embedding/Ready/Failed/Stale/Deleting`, chunks, última sincronização e erro
acionável. Pode testar pergunta e inspecionar citações autorizadas. Usuário
final sempre vê fonte/versão ou ausência de evidência.

## Multi-tenant e authZ

`ai.knowledge.manage` administra; retrieval exige interseção usuário + bot +
fonte. Qdrant aplica filtro obrigatório, mas PostgreSQL revalida ACL/estado.
URL/arquivo não pode referenciar recurso de outro tenant por ID.

## Aceite

- [ ] Arquivo e URL válidos chegam a `Ready` com checksum/contagem reproduzível.
- [ ] Pergunta recupera apenas fontes concedidas e cita a versão correta.
- [ ] Revogar membership/grant impede retrieval imediatamente.
- [ ] Delete/purge remove chunks/vetores dentro do SLO e produz evidência.
- [ ] Qdrant indisponível não derruba chat/FTS.
- [ ] URL para loopback/private/link-local/metadata é bloqueada após redirects.
- [ ] Teste cross-tenant adversarial não retorna hit, chunk nem existência.

## Testes

Testcontainers para Postgres/MinIO/Qdrant; fake embedding provider; corpus
golden; SSRF/DNS rebinding/redirect; malware/quarentena; ACL revoke/delete/
rebuild; security cross-tenant; load de ingestão/query; E2E upload→index→citação.

## Riscos

Embeddings retêm semântica, Qdrant não tem RLS PostgreSQL, URL abre SSRF e parser
processa conteúdo hostil. Mitigar com projeção mínima, porta central,
revalidação, delete ledger, sandbox/limites, egress policy e R3 off por default.
