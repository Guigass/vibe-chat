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

## Flags R3 entregues

| Flag lógica | Escopo | Default | B-ID | Comportamento off |
|--------------|--------|---------|------|-------------------|
| `Directory:GroupDm:Enabled` | instance | false | B-101 | create/add/leave/rename → 404; history existente permanece para quem ainda é membro |
| `Directory:Invites:Enabled` | instance | false | B-040 | create/list/revoke/accept → 404; guests já aceitos permanecem até revogar |
| `Integrations:Bots:Enabled` | instance | false | B-109 | create/list/rotate/revoke/send → 404; mensagens já gravadas permanecem |
| `Integrations:DeveloperPortal:Enabled` | instance | false | B-135 | portal → 404 `DeveloperPortalDisabled`; credenciais e mensagens já gravadas permanecem |
| `Integrations:PluginSdk:Enabled` | instance | false | B-136 | SDK → 404 `PluginSdkDisabled`; chat, send e portal seguem |
| `Integrations:PluginRegistry:Enabled` | instance | false | B-137 | registry → 404 `PluginRegistryDisabled`; install local e SDK seguem |
| `Messaging:History:Enabled` | instance | false | B-114 | history/move → 404; edição não grava versão; versões já gravadas permanecem |
| `SemanticSearch:Enabled` | instance + workspace | false | B-121 | FTS continua; opt-in do workspace apaga a projeção ao desligar |
| `Notifications:Digests:Enabled` | instance | false | B-122 | settings/catch-up → 404 `DigestDisabled`; worker não varre; mensagens e inbox seguem |
| `Forms:Enabled` | instance | false | B-124 | formulários → 404 `FormDisabled`; chat, mensagens e tarefas seguem |
| `Automation:Enabled` | instance | false | B-125 | automação → 404 `AutomationDisabled`; triggers não executam; chat segue |
| `Incidents:Enabled` | instance | false | B-126 | incidentes → 404 `IncidentDisabled`; chat segue |
| `Connectors:Enabled` | instance | false | B-127 | conectores → 404 `ConnectorDisabled`; chat e outbox de mensagens seguem |
| `Identity:Sso:Enabled` | instance | false | B-164 | lista SSO → 404 `SsoDisabled`; claims de broker ignorados; login Keycloak segue |
| `Identity:Scim:Enabled` | instance | false | B-128 | `/scim/v2` e admin SCIM → 404 `ScimDisabled`; chat e login seguem |
| `Identity:Scim:KeycloakAdapter` | instance | false | B-128 | revogação local segue; falha do IdP não desfaz o deprovision |
| `Integrations:EventSchemas:Enabled` | instance | false | B-139 | webhook segue com o body atual; `/admin/event-schemas` → 404 `EventSchemasDisabled` |
| `Features:Plugins:Enabled` | instance | false | B-066, B-111 | capabilities além de `messages.send`, slash, callback, interação, histórico e anexo → 404 `PluginPlatformDisabled`; envio B-109/B-110 segue |
| `Integrations:Bridges:Enabled` | instance (binding de `Features:Bridges:Enabled`) | false | B-138 | rotas de bridge → 404 `BridgesDisabled`; chat segue; mensagens já copiadas permanecem |
| `Integrations:Federation:Enabled` | instance (binding de `Features:Federation:Enabled`) | false | B-065 | rotas de federação → 404 `FederationDisabled`; chat segue; cópias já gravadas permanecem |
| `Desktop:Enabled` | instance (binding de `Features:Desktop:Enabled`) | false | B-141 | rotas `/desktop/*` → 404 `DesktopDisabled`; PWA e chat seguem; aparelhos já registrados permanecem |
| `Mobile:Enabled` | instance (binding de `Features:Mobile:Enabled`) | false | B-063 | rotas `/mobile/*` → 404 `MobileDisabled`; PWA, chat e desktop seguem; aparelhos já registrados permanecem |
| `Features:OfflineWrite:Enabled` | instance/client | false | B-143 | `GET /api/v1/sync` → 404 `OfflineWriteDisabled`; leitura online e PWA seguem; sem chave utilizável a rota também fica fechada |

## Registro inicial de flags futuras

| Flag lógica | Escopo | Default | B-ID | Comportamento off |
|--------------|--------|---------|------|-------------------|
| `Features:Automation:Enabled` | instance (binding `Automation:Enabled`) | false | B-125 | entregue; ver linha ativa |
| `Features:Registry:Enabled` | instance (binding `Integrations:PluginRegistry:Enabled`) | false | B-137 | entregue; ver linha ativa |
| `Features:Bridges:Enabled` | instance (binding `Integrations:Bridges:Enabled`) | false | B-138 | entregue; ver linha ativa |
| `Features:Federation:Enabled` | instance (binding `Integrations:Federation:Enabled`) | false | B-065 | entregue; ver linha ativa |
| `Features:OfflineWrite:Enabled` | instance/client | false | B-143 | entregue; ver linha ativa |
| `Features:StorageLifecycle:Enabled` | instance | false | B-145 | API `/storage` → 404 `StorageLifecycleDisabled`; upload não consulta o ledger |
| `Features:E2EE:Enabled` | workspace | false | B-064 | canais server-readable |
| `Features:Live:Enabled` | tenant | false | B-147 | chat assíncrono |
| `Features:Recording:Enabled` | tenant | false | B-148 | live sem gravação |
| `Features:MeetingAi:Enabled` | workspace | false | B-149 | sem transcrição/notas |
| `Features:CanvasRealtime:Enabled` | workspace | false | B-152 | páginas server-authoritative |
| `Features:Import:Enabled` | instance | false | B-153 | onboarding/template manual |
| `Features:ChatBackup:Enabled` | instance | false | B-172 | API `/chat-backup` → 404 `ChatBackupDisabled`; export B-046 e scripts B-031 seguem |
| `Ha:Enabled` | instance | false | B-144 | API `/meta/runtime` e `/admin/ha/drain` → 404 `HaDisabled`; Standard segue um nó |
| `Features:SupportBundle:Enabled` | instance | false | B-154 | health/runbooks permanecem |
| `Features:AvailabilityCalendar:Enabled` | instance | false | B-116 | `GET /me/availability/calendar` → 404; status e disponibilidade seguem ativos |
| `Features:LegalHold:Enabled` | instance | false | B-129 | API de compliance → 404 `LegalHoldDisabled`; hold já ativo continua bloqueando o purge |
| `Features:Dlp:Enabled` | instance | false | B-130 | API `/dlp/*` → 404 `DlpDisabled`; gate não altera efeitos externos |
| `Features:Siem:Enabled` | instance | false | B-132 | API `/siem/*` → 404 `SiemDisabled`; worker não envia audit |
| `Features:PolicyPacks:Enabled` | instance | false | B-133 | API `/policy-packs/*` → 404 `PolicyPackDisabled`; convite não consulta pack |
| `Features:DelegatedAdmin:Enabled` | instance | false | B-134 | API `/delegated-admin/*` e `/quotas/*` → 404 `DelegatedAdminDisabled`; chat não consulta quota |

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
