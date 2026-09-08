# Architecture Decision Records

Índice canônico das decisões arquiteturais do VibeChat. Todos os ADRs atuais
estão `Accepted`; emendas e substituições parciais são registradas dentro do ADR
original.

| ADR | Decisão | Status |
|-----|---------|--------|
| [001](ADR-001-monolito-modular.md) | Monólito modular | Accepted |
| [002](ADR-002-angular.md) | Angular 22 + CDK + UI própria | Accepted; adoção PrimeNG superseded |
| [003](ADR-003-aspnet-core.md) | ASP.NET Core / .NET 10 | Accepted |
| [004](ADR-004-signalr.md) | SignalR para realtime | Accepted |
| [005](ADR-005-postgresql.md) | PostgreSQL como source of truth | Accepted |
| [006](ADR-006-redis.md) | Redis para estado efêmero/backplane | Accepted |
| [007](ADR-007-keycloak.md) | Keycloak OIDC | Accepted |
| [008](ADR-008-minio-s3.md) | MinIO/S3-compatible | Accepted |
| [009](ADR-009-multi-tenancy-rls.md) | Shared schema + RLS | Accepted |
| [010](ADR-010-outbox.md) | Transactional outbox | Accepted |
| [011](ADR-011-busca-postgresql.md) | Busca inicial no PostgreSQL | Accepted |
| [012](ADR-012-integracao-ia.md) | IA opcional atrás de portas | Accepted |
| [013](ADR-013-observabilidade.md) | OpenTelemetry e stack OSS | Accepted |
| [014](ADR-014-estrategia-testes.md) | Estratégia de testes | Accepted |
| [015](ADR-015-quando-justificar-bus-mensagens.md) | Gatilhos para bus externo | Accepted |
| [016](ADR-016-quando-justificar-opensearch.md) | Gatilhos para OpenSearch | Accepted |
| [017](ADR-017-quando-justificar-kubernetes.md) | Gatilhos para Kubernetes | Accepted |
| [018](ADR-018-retencao-mensagens.md) | Retenção e exclusão | Accepted |
| [019](ADR-019-qdrant-para-vetores-de-conhecimento.md) | Qdrant para vetores de conhecimento e bots | Accepted; implementação planejada em B-121/B-157 |
| [020](ADR-020-runtime-settings-credenciais-criptografadas.md) | Settings runtime e credenciais criptografadas | Accepted |
| [021](ADR-021-link-preview-ssrf.md) | Link preview com guarda SSRF | Accepted |
| [022](ADR-022-web-push-vapid.md) | Web Push com VAPID da instância | Accepted |
| [023](ADR-023-group-dm.md) | DM em grupo reutiliza Channel | Accepted |

## Quando criar um ADR

Criar ou emendar ADR antes de:

- adicionar serviço ou fronteira de deploy;
- trocar banco, IdP, transporte realtime ou storage;
- introduzir bus externo, OpenSearch ou Kubernetes;
- mudar modelo multi-tenant, outbox ou consistência;
- adotar dependência estrutural/proprietária;
- mudar uma decisão que afete múltiplos módulos.

Mudança de endpoint, evento ou DTO que não altera arquitetura pertence primeiro
a `architecture/contratos.md` e à spec da feature. Decisão legal, de marca ou de
produto permanece em `roadmap/decisoes-pendentes.md`.

## Ciclo de vida

- `Proposed`: em análise; não autoriza implementação.
- `Accepted`: vigente.
- `Deprecated`: ainda existe, mas não deve orientar trabalho novo.
- `Superseded`: substituído; deve apontar para o sucessor ou emenda.

ADRs não são apagados quando perdem vigência; preservam o contexto histórico.

