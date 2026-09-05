# Matriz de Aderência aos ADRs

Snapshot documental de 2026-07-27. A matriz ajuda a revisar se decisões aceitas
continuam refletidas na estrutura e configuração do repositório. Não substitui
testes nem uma revisão de segurança.

| ADR | Decisão | Evidência observada | Aderência |
|-----|---------|---------------------|-----------|
| [001](../adrs/ADR-001-monolito-modular.md) | Monólito modular | `apps/api`, `apps/worker`, `modules/*`, testes de arquitetura | Alinhado; ampliar varredura de dependências entre todos os módulos |
| [002](../adrs/ADR-002-angular.md) | Angular 22 + CDK + UI própria | Angular/CDK + `@spartan-ng/brain` (MIT); sem `primeng` (#82 / B-104) | **Alinhado** (D-15 / D-27 / B-104) |
| [003](../adrs/ADR-003-aspnet-core.md) | ASP.NET Core / .NET 10 | projetos API/Worker e target framework dos `*.csproj` | Alinhado |
| [004](../adrs/ADR-004-signalr.md) | SignalR para realtime | módulo Realtime, hub na API, cliente `@microsoft/signalr` | Alinhado |
| [005](../adrs/ADR-005-postgresql.md) | PostgreSQL como SoT | Compose, EF Core migrations, history no banco | Alinhado |
| [006](../adrs/ADR-006-redis.md) | Redis efêmero/backplane | Compose, presence/typing/rate-limit e backplane | Alinhado |
| [007](../adrs/ADR-007-keycloak.md) | Keycloak OIDC | serviço Compose, realm versionado, JWT/OIDC web | Alinhado; DevAuth limitado ao ambiente Development |
| [008](../adrs/ADR-008-minio-s3.md) | MinIO/S3-compatible | serviços MinIO/createbucket, módulo Files, presigned URLs | Alinhado |
| [009](../adrs/ADR-009-multi-tenancy-rls.md) | Shared schema + RLS | Roles migrator/app, `03-rls.sql` FORCE+WITH CHECK, `RlsSession`, testes runtime | **Alinhado** (`SEC-RLS-RUNTIME`) |
| [010](../adrs/ADR-010-outbox.md) | Transactional outbox | tabela/migration, writer, processor e worker | Alinhado |
| [011](../adrs/ADR-011-busca-postgresql.md) | FTS no PostgreSQL | migration search vector, módulo Search, endpoint/testes | Alinhado |
| [012](../adrs/ADR-012-integracao-ia.md) | IA opcional e fora do hot path | módulo AI, Mock/OpenRouter, `Ai:Enabled=false` por default | Alinhado; emenda ADR-020/B-187: key + kill switch podem ir ao DB |
| [013](../adrs/ADR-013-observabilidade.md) | OpenTelemetry + stack OSS | collector, Prometheus, Grafana, Loki, Tempo e dashboard | Alinhado |
| [014](../adrs/ADR-014-estrategia-testes.md) | Pirâmide e testes de segurança | unit, integration, architecture, security, E2E e load | Alinhado; Dependabot (B-076 / #84) + `dependencias.md` |
| [015](../adrs/ADR-015-quando-justificar-bus-mensagens.md) | Sem bus externo antes dos gatilhos | Nenhum NATS/Kafka/RabbitMQ no Compose/projetos | Alinhado |
| [016](../adrs/ADR-016-quando-justificar-opensearch.md) | Sem OpenSearch antes dos gatilhos | Busca permanece PostgreSQL; sem serviço externo | Alinhado |
| [017](../adrs/ADR-017-quando-justificar-kubernetes.md) | Compose na fase 1 | `compose.yaml`, profiles e runbooks; sem Helm/K8s | Alinhado |
| [018](../adrs/ADR-018-retencao-mensagens.md) | Soft-delete + purge configurável | settings, migration, worker purge e kill switch | Operacional: `MessageRetention__*` injetado no worker (B-105) |
| [020](../adrs/ADR-020-runtime-settings-credenciais-criptografadas.md) | Settings runtime + AES-GCM | envelopes tipados, flag + keyring lab, rotate/reencrypt/VAPID, Files/RateLimit teto de código | **Alinhado** (B-187): `.env` só infra, produto no DB |
| [021](../adrs/ADR-021-link-preview-ssrf.md) | Link preview + SSRF | outbox B-091, `LinkPreviewFetcher`, cache por tenant, ADR+threat model | **Alinhado** (W9-4) |
| [022](../adrs/ADR-022-web-push-vapid.md) | Web Push VAPID | outbox B-095, `Push:Enabled=false`, payload mínimo, RLS | **Alinhado** (W10-1) |
| [023](../adrs/ADR-023-group-dm.md) | DM em grupo | `ChannelType.GroupDm`, `JoinedSeq`, flag `Directory:GroupDm:Enabled=false` | **Alinhado** (W10-7) |

## Gaps transversais derivados

| Gap | ADRs afetados | Dono no roadmap |
|-----|---------------|------------------|
| Tornar configuração declarada efetiva nos containers | ADR-012, ADR-018 e D-10 | **Done** — B-105 / W7-7; produto no admin = **B-187 Done** |
| Ampliar teste de dependências para todos os assemblies | ADR-001, ADR-014 | Registro futuro `GAP-*` |
| CSP no caminho oficial | defesa em profundidade associada a ADR-007/014 | B-077 / W7-4 |
| Validar body antes da persistência | ADR-005/010/014 | B-078 / W7-5 |

## Regra de manutenção

Atualizar esta matriz quando:

- um ADR for aceito, emendado ou superseded;
- um item da coluna “Parcial” for fechado;
- surgir nova evidência que contradiga uma decisão;
- uma wave alterar runtime, dependências externas ou fronteiras de módulo.

Se a implementação divergir de um ADR aceito, não marcar apenas “Done”: corrigir
a implementação ou registrar formalmente a nova decisão.

