# Catálogo de Evolução de Contratos, Dados e Flags

Mapa pré-implementação das superfícies futuras. Shapes finais entram em
`contratos.md` no PR da feature; este catálogo impede nomes conflitantes e
follow-ups invisíveis.

## Fontes da verdade

| Assunto | Fonte |
|---------|-------|
| Decisão de produto | `roadmap/decisoes-pendentes.md` |
| Contrato implementado | `architecture/contratos.md` |
| Modelo persistido | migrations + `modelo-dominio.md` |
| Evento implementado | Contracts + outbox + `contratos.md` |
| Configuração | `.env.example` (infra) + `operations/configuracao-env.md` (catálogo; B-105). Integração no admin: **B-187 Done** |
| Flag | catálogo deste documento + configuração efetiva |
| Permissão | RolePermissionCatalog + glossário/contratos |

## Convenções

### API

- Base `/api/v1`.
- Recursos no plural.
- IDs opacos.
- Paginação por cursor.
- Erros com `error`, `message`, correlation id e status adequado.
- Tenant nunca vem do body.
- Mutação retryable aceita `Idempotency-Key`.

### Eventos

Nome lógico:

```text
<dominio>.<agregado>.<ação>.v<major>
```

Envelope contém `eventId`, `eventType`, `occurredAt`, `tenantId`, `aggregateId`,
`correlationId`, `schemaVersion` e payload.

- additive por padrão;
- campo removido só após depreciação;
- consumidor idempotente;
- PII mínima;
- outbox para evento durável.

### Flags

Nome:

```text
Features:<Capability>:Enabled
```

Toda flag registra:

- owner/B-ID;
- escopo process/tenant/workspace/user;
- default;
- source efetiva;
- dependências;
- comportamento off;
- migration de remoção;
- métrica de adoção sem PII.

R3 nasce `false`. Flag não substitui authZ.

## Registro inicial de flags futuras

| Flag lógica | Escopo | Default | B-ID | Comportamento off |
|--------------|--------|---------|------|-------------------|
| `Features:SemanticSearch:Enabled` | workspace | false | B-121 | FTS continua |
| `Features:Automation:Enabled` | tenant | false | B-125 | triggers não executam |
| `Features:Plugins:Enabled` | tenant | false | B-066 | integrações locais existentes preservadas |
| `Features:Registry:Enabled` | instance | false | B-137 | catálogo local apenas |
| `Features:Bridges:Enabled` | tenant | false | B-138 | sem tráfego externo |
| `Features:Federation:Enabled` | instance/tenant | false | B-065 | instância isolada |
| `Features:OfflineWrite:Enabled` | instance/client | false | B-143 | leitura online/PWA |
| `Features:E2EE:Enabled` | workspace | false | B-064 | canais server-readable |
| `Features:Live:Enabled` | tenant | false | B-147 | chat assíncrono |
| `Features:Recording:Enabled` | tenant | false | B-148 | live sem gravação |
| `Features:MeetingAi:Enabled` | workspace | false | B-149 | sem transcrição/notas |
| `Features:CanvasRealtime:Enabled` | workspace | false | B-152 | páginas server-authoritative |
| `Features:Import:Enabled` | instance | false | B-153 | onboarding/template manual |
| `Features:ChatBackup:Enabled` | instance | false | B-172 | export pontual B-046 + scripts B-031 |
| `Features:SupportBundle:Enabled` | instance | false | B-154 | health/runbooks permanecem |

Nomes de binding podem mudar no ADR, mas semântica/default não muda
silenciosamente.

## Matriz de evolução

| Capacidade | Entidades/projeções | API/eventos esperados | Permissões | Docs no PR |
|------------|---------------------|-----------------------|-----------|------------|
| Anúncios | announcement, acknowledgement | publish/ack + eventos | publish/acknowledge | contratos, audit |
| Inbox | projeção de atenção | inbox cursor/list | message.read | contratos, modelo |
| Decisões/tarefas | decision, action item, task | CRUD/link + eventos | scoped create/manage | contratos, retenção |
| Knowledge | page, collection, revision | CRUD/search/link | knowledge.* | modelo, threat |
| RAG | chunk/embedding/job | index/query/delete | ai.search | ADR, threat, ops |
| Automação | definition, execution, step | CRUD/run/cancel | automation.* | ADR, runbook |
| Plugins | plugin, token, grant | install/grant/revoke | integration.* | contratos, security |
| Registry | package/catalog/revocation | resolve/install | instance admin | ADR, provenance |
| Bridge | trust/route/delivery | connect/sync | bridge.* | ADR, threat |
| SCIM | token/group/mapping | SCIM 2.0 | scim.manage | ADR, ops |
| Legal hold | case/hold/custodian | apply/release/export | legalhold.* | ADR, audit |
| DLP | classification/policy/finding | scan/review | dlp.* | threat, privacy |
| Clients | device/session/cursor | register/revoke/sync | session own/admin | support matrix |
| Federação | peer/trust/envelope | negotiate/send/revoke | federation.* | ADR, runbook |
| E2EE | device key/channel epoch | key/device metadata | e2ee.* | crypto ADR |
| Live | session/participant | start/join/end | live.* | ADR, capacity |
| Canvas | document/update/snapshot | edit/sync/export | knowledge.* | ADR, retention |
| Import | import/adapter/batch/checkpoint | validate/plan/execute/publish | workspace.import | threat, migration |
| Diagnóstico | check/bundle/repair job | preflight/test/bundle | support.* | security, runbook |

## Registro de dados

Toda entidade nova documenta:

- propósito;
- schema/tabela;
- chave e unique constraints;
- TenantId/WorkspaceId;
- owner e ACL;
- lifecycle;
- edit/delete/purge;
- legal hold;
- export;
- audit;
- projeções derivadas;
- RLS;
- índices;
- volume/capacidade;
- migration e rollback.

## Compatibilidade

Antes do merge:

1. classificar additive/breaking;
2. definir janela de coexistência;
3. atualizar API, Worker e clientes necessários;
4. dual-read/write/publish quando preciso;
5. adicionar contract test;
6. atualizar release notes;
7. definir remoção de compatibilidade;
8. provar rollback ou roll-forward.

## Catálogos que não devem divergir

- permissões: enum/catálogo, contratos, glossário e UI;
- eventos: Contracts, outbox, webhook, schema registry e SDK;
- flags: configuração, admin, docs e métricas;
- variáveis: catálogo operacional (`configuracao-env.md`), Compose (defaults),
  appsettings e `.env.example` (infra; integração no admin = B-187);
- roles: domínio, Keycloak mapping, API e admin;
- MIME/limites: Files, frontend, proxy e docs.
