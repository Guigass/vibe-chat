# Contratos Compartilhados entre Módulos — VibeChat

## Objetivo

Definir as interfaces, DTOs e eventos que permitem colaboração entre módulos **sem** acoplamento às implementações. Pacote canônico: `VibeChat.Contracts`.

## Princípios

1. Contratos são estáveis e versionados com cuidado (mudanças breaking exigem ADR ou migração)
2. Sem dependência de EF Core, SignalR ou SDKs de cloud nos contratos de domínio
3. Eventos de integração são **imutáveis** e serializáveis (JSON)
4. Queries entre módulos via interfaces estreitas (CQRS leve)

---

## Contexto de tenancy

```csharp
// Conceito — não é código de produção obrigatório neste doc
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsAuthenticated { get; }
}
```

`TenantId` e `UserId` vêm exclusivamente do token/contexto autenticado.

---

## Directory → outros módulos

```csharp
public interface IMembershipQuery
{
    Task<bool> CanAccessChannelAsync(Guid tenantId, Guid userId, Guid channelId, CancellationToken ct);
    Task<bool> CanPostAsync(Guid tenantId, Guid userId, Guid channelId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListChannelMemberIdsAsync(Guid tenantId, Guid channelId, CancellationToken ct);
}
```

---

## Messaging — comandos e DTOs

### SendMessage

| Campo | Tipo | Notas |
|-------|------|-------|
| ConversationId | Guid | Channel root ou thread |
| Body | string | Texto (markdown subset futuro) |
| ContentType | string | `text/plain` inicial |
| IdempotencyKey | string | Obrigatório no cliente |
| AttachmentIds | Guid[] | Opcional |
| ClientMessageId | Guid? | Eco para UI otimista |

### MessageDto

| Campo | Tipo |
|-------|------|
| Id | Guid |
| TenantId | Guid |
| ConversationId | Guid |
| ChannelId | Guid | Canal pai (mesmo para replies de thread) |
| Seq | long |
| AuthorUserId | Guid |
| Body | string |
| CreatedAt | DateTimeOffset |
| EditedAt | DateTimeOffset? |
| DeletedAt | DateTimeOffset? |
| ThreadId | Guid? | Presente em pai (após abrir thread) e replies |
| ReplyToMessageId | Guid? | |
| ReplyCount | int | Contagem de replies (timeline do canal) |
| Attachments | AttachmentDto[] | Metadados prontos (sem URL) |
| Reactions | ReactionSummaryDto[] | `{ emoji, count, me }` agregado |

### EditMessage

| Campo | Tipo | Notas |
|-------|------|-------|
| Body | string | Obrigatório; só autor com `message.edit.own` |

### Soft-delete Message

- `DELETE /api/v1/channels/{channelId}/messages/{messageId}`
- Autor com `message.delete.own` **ou** papel com `message.delete.any`
- Soft-delete (`DeletedAt`); body oculto nas leituras (ADR-018)
- Também cobre replies de thread do canal (authZ por membership do canal pai)

### Reactions

| Endpoint | Notas |
|----------|-------|
| `PUT /api/v1/channels/{channelId}/messages/{messageId}/reactions` | Toggle (`{ emoji }`); membership + `message.react`; allowlist `👍 ❤️ 😂 🎉 👀 ✅`; unique `(tenant, message, user, emoji)` |

`MessageDto.reactions`: `{ emoji, count, me }[]` (agregado no history/thread). Outbox `ReactionChangedEvent` → hub `ReactionChanged` no grupo do canal pai (`messageId`, `emoji`, `userId`, `added`, `reactions`).

### Threads

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/messages/{messageId}/threads` | Get-or-create thread ancorada na mensagem pai; membership do canal |
| `GET /api/v1/threads/{threadId}` | Metadados + parent + `replyCount` |
| `GET /api/v1/threads/{threadId}/messages` | Histórico da conversa da thread (`seq` próprio) |
| `POST /api/v1/threads/{threadId}/messages` | Reply; idempotência + seq + outbox; `threadId` no evento hub |

Replies usam `ConversationId = ThreadId` (seq separado do canal). Fan-out SignalR continua no grupo do **canal pai**, com `threadId` / `conversationId` no payload.

### Directory — spaces, channels, members & DMs

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/workspaces/{workspaceId}/spaces` | Lista spaces do workspace (membership obrigatória — D-07); ordenado por `order` |
| `POST /api/v1/workspaces/{workspaceId}/spaces` | Body `{ name, order? }`; exige `channel.create` |
| `GET /api/v1/workspaces/{workspaceId}/channels` | Channels do workspace; `spaceId` opcional no response |
| `POST /api/v1/workspaces/{workspaceId}/channels` | Body `{ name, type, spaceId? }`; exige `channel.create`; `spaceId` deve pertencer ao workspace |
| `GET /api/v1/workspaces/{workspaceId}/members` | Membros do workspace (membership obrigatória — D-07); inclui `role` |
| `GET /api/v1/workspaces/{workspaceId}/roles` | Papéis atribuíveis (`Member`, `Moderator`, `Auditor`, `Admin`); exige `workspace.admin` no workspace |
| `POST /api/v1/workspaces/{workspaceId}/members` | Convite/provisionamento (B-068). Body `{ email, displayName?, role? }` (`role` default `Member`); exige `workspace.admin`; cria perfil stub `pending:{email}` se o usuário ainda não logou; 409 se já membro; rejeita `Guest`/`Bot`/owners; audit `member.invite`; e-mail opcional via outbox se `Email:Enabled`. Sem self-signup — IdP (Keycloak) continua responsável pela autenticação |
| `PUT /api/v1/workspaces/{workspaceId}/members/{userId}/role` | Body `{ role }`; owner/admin (`workspace.admin`); não permite auto-elevação; rejeita `Guest`/`Bot`/`WorkspaceOwner`/`PlatformOwner` (D-07); audit `member.role.change`; e-mail opcional via outbox se `Email:Enabled` |
| `GET /api/v1/workspaces/{workspaceId}/presence` | Status `online`/`away`/`offline` dos membros (Redis TTL) |
| `POST /api/v1/workspaces/{workspaceId}/dms` | Body `{ userId }`; get-or-create DM 1:1 (`ChannelType.Direct`) |

`ChannelResponse` inclui `spaceId?` e, para DMs, `peerUserId` / `peerDisplayName`. Channels `Private`/`Direct`/`Group` só aparecem na listagem para membros do canal. Spaces agrupam channels na UI; DMs ficam fora de spaces.

Papéis reutilizam `Role` + `RolePermissionCatalog` + `IPermissionChecker`. Guest permanece no enum/catálogo, mas **fora do fluxo de membership** (D-07).

---

## Eventos de outbox (integração)

Envelope comum:

| Campo | Descrição |
|-------|-----------|
| EventId | Guid |
| EventType | string (ex.: `messaging.message.created`) |
| OccurredAt | DateTimeOffset |
| TenantId | Guid |
| AggregateId | Guid |
| CorrelationId | string |
| Payload | JSON |

### `messaging.message.created`

```json
{
  "messageId": "…",
  "conversationId": "…",
  "channelId": "…",
  "threadId": null,
  "seq": 42,
  "authorUserId": "…",
  "preview": "texto truncado"
}
```

### `messaging.message.edited`

```json
{
  "messageId": "…",
  "conversationId": "…",
  "channelId": "…",
  "seq": 42,
  "body": "texto atualizado",
  "editedAt": "…"
}
```

### `messaging.message.deleted`

```json
{
  "messageId": "…",
  "conversationId": "…",
  "channelId": "…",
  "seq": 42,
  "deletedAt": "…"
}
```

### `messaging.reaction.changed` (`ReactionChangedEvent`)

```json
{
  "messageId": "…",
  "conversationId": "…",
  "channelId": "…",
  "threadId": null,
  "userId": "…",
  "emoji": "👍",
  "added": true,
  "reactions": [{ "emoji": "👍", "count": 1, "userIds": ["…"] }]
}
```

Clientes derivam `me` a partir de `userIds` (o campo `me` só existe nas respostas HTTP).

### `directory.membership.changed`

Usado por Presence/Realtime/Search para invalidar caches e grupos.

### `files.attachment.ready`

Metadados prontos / vírus scan ok (quando existir).

---

## Realtime

```csharp
public interface IRealtimePublisher
{
    Task PublishToConversationAsync(
        Guid tenantId,
        Guid conversationId,
        string eventType,
        object payload,
        CancellationToken ct);
}
```

Nomes de eventos hub (cliente):

| Evento | Quando |
|--------|--------|
| `message.created` | Nova mensagem |
| `message.edited` | Edição |
| `message.deleted` | Soft delete |
| `ReactionChanged` | Toggle de reação (payload com resumo agregado) |
| `Typing` | Typing (TTL curto Redis); hub publica com `Clients.OthersInGroup` (B-071) — autor não recebe o próprio evento |
| `PresenceChanged` | Presence `online`/`away`/`offline` (hub group `t:{tenantId}`) |

Grupos SignalR: canal `t:{tenantId}:c:{channelId}` (mensagens/typing/reações); tenant `t:{tenantId}` (presence).

Hub (além de `JoinChannel` / `LeaveChannel` / `SendTyping`):

| Método | Notas |
|--------|-------|
| `Heartbeat(tenantId)` | Renova presença online (TTL ~45s); authZ via membership no tenant |
| `SetAway(tenantId)` | Marca away; authZ via membership no tenant |

---

## Files

```csharp
public interface IObjectStorage
{
    Task<PresignedUpload> CreateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken ct);
    Task<PresignedDownload> CreateDownloadUrlAsync(string storageKey, string fileName, TimeSpan ttl, CancellationToken ct);
    Task<ObjectStat?> StatObjectAsync(string storageKey, CancellationToken ct);
}
```

### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/attachments` | Body `{ fileName, contentType, sizeBytes }` → URL pré-assinada PUT; exige membership + `file.upload` |
| `POST /api/v1/channels/{channelId}/attachments/{id}/complete` | Confirma objeto no MinIO; status `Ready` |
| `GET /api/v1/channels/{channelId}/attachments/{id}/download` | URL pré-assinada GET; exige membership + `file.download` |

Regras: keys prefixadas por tenant (`tenants/{tenantId}/…`); MIME/tamanho via `Files:*`; body da mensagem pode ser vazio se houver `AttachmentIds` prontos no `SendMessage`.

---

## Search

```csharp
public interface ISearchIndexer
{
    Task IndexMessageAsync(MessageIndexed doc, CancellationToken ct);
    Task RemoveMessageAsync(TenantId tenantId, MessageId messageId, CancellationToken ct);
}

public interface ISearchQuery
{
    Task<SearchResultPage> SearchMessagesAsync(SearchMessagesQuery q, CancellationToken ct);
}
```

Fase 1: implementação PostgreSQL FTS (ADR-011).

### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/search/messages?workspaceId=&q=&channelId=&limit=` | FTS em mensagens (`tsvector`/`GIN`); exige membership no workspace + `search.messages` + `message.read`; filtra por ACL de canal (público via workspace, privado/DM via `channel_members`); nunca retorna mensagens soft-deleted nem fora da membership |

`SearchMessageHit`: `messageId`, `channelId`, `channelName`, `channelType`, `sequence`, `authorUserId`, `authorDisplayName`, `bodyPreview`, `createdAt`, `rank`.

Indexação: coluna `messaging.messages.search_vector` (trigger + reindex via outbox `MessageCreated`/`Edited`/`Deleted`).

---

## Administration / Audit

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/admin/dashboard` | Métricas operacionais (auth obrigatória) |
| `GET /api/v1/admin/audit-events?limit=&action=` | Lista eventos de `audit.audit_events` do tenant do actor; exige `admin.dashboard`; `limit` 1–200 (default 50); nunca retorna eventos de outro tenant |
| `GET /api/v1/admin/conversations?workspaceId=&limit=` | Lista canais/DMs do tenant para auditoria (B-067); exige `admin.dashboard`; **não** exige `channel_members`; `limit` 1–200 (default 100) |
| `GET /api/v1/admin/conversations/{channelId}/messages?after=&limit=` | Histórico admin do canal/DM (root); body **visível** mesmo com soft-delete; inclui `deletedBy` / anexos; exige `admin.dashboard`; canal fora do tenant → 403 |
| `GET /api/v1/admin/threads/{threadId}/messages?after=&limit=` | Histórico admin de replies da thread; mesma authZ e semântica de body |
| `GET /api/v1/admin/settings?workspaceId=` | Settings sensíveis mascarados (B-069); exige `workspace.admin` (Auditor com só `admin.dashboard` → 403); `workspaceId` opcional (default: primeiro workspace do actor) |
| `PUT /api/v1/admin/settings` | Atualiza flags não-secretas; mesma authZ; rejeita body com `ai.apiKey` / `email.smtpPassword` (`SecretsNotWritable`); audit `settings.change` |
| `GET /api/v1/admin/workspaces/{workspaceId}/export` | Export compliance do workspace (B-046); ZIP `application/zip` com JSON (`manifest`, `workspace`, `members`, `spaces`, `channels`, `threads`, `messages`, `attachments` metadata); corpos soft-deleted incluídos (paridade B-067); **sem** binários MinIO; exige `workspace.admin` (Auditor → 403); audit `workspace.export`; workspace fora do tenant/membership → 403 |

`AuditEventResponse`: `id`, `action`, `entityType`, `entityId`, `actorUserId`, `occurredAt`, `metadataJson`.

`AdminConversationResponse`: `id`, `workspaceId`, `name`, `type`, `spaceId`, `peerUserId`, `peerDisplayName`.

`AdminConversationMessageResponse`: `id`, `channelId`, `conversationId`, `sequence`, `authorId`, `authorName`, `body` (sempre o valor persistido), `createdAt`, `editedAt`, `deletedAt`, `deletedBy`, `deletedByName`, `threadId`, `replyToMessageId`, `replyCount`, `attachments`.

Ações mínimas: `admin.login`, `channel.create`, `space.create`, `message.send`, `message.delete`, `attachment.upload`, `member.role.change`, `member.invite`, `settings.change`, `workspace.export`, `message.purge`.

### Export de workspace (B-046)

- Formato: `vibechat.workspace.export.v1` (`manifest.json.format`)
- AuthZ alinhada a settings sensíveis (`workspace.admin`); não usar só `admin.dashboard`
- Mensagens: body persistido inclusive soft-delete (`deletedAt` / `deletedBy`)
- Anexos: metadados apenas (`fileName`, `contentType`, `sizeBytes`, `checksumSha256`, `status`) — sem `storageKey` nem bytes
- Tenant do actor; nunca aceitar `tenantId` do body

### Settings sensíveis (B-069)

`SensitiveSettingsResponse`:

| Campo | Notas |
|-------|-------|
| `workspaceId` | Workspace alvo (AI workspace settings) |
| `ai.processEnabled` / `processSource` | Flag de processo (`Ai:Enabled`) — SoT env; somente leitura |
| `ai.workspaceEnabled` / `provider` | `ai.settings` do workspace — gravável |
| `ai.apiKeyConfigured` / `apiKeyMask` | Máscara `••••last4` ou `configured: false`; **nunca** valor em claro |
| `email.*` | Enabled/host/port/user/from/startTls (override tenant em `notifications.email_settings` ou env); senha só `smtpPasswordConfigured` + `smtpPasswordMask` |
| `webhooks.status` | `unconfigured` \| `disabled` \| `active` (B-048) |
| `webhooks.enabled` / `url` / `urlConfigured` | Endpoint HTTP do tenant; URL gravável |
| `webhooks.secretConfigured` / `secretMask` | HMAC secret mascarado (`••••last4`); **nunca** em claro |
| `webhooks.secretsWritable` | `true` — secret pode ser rotacionado via PUT (omitir/vazio = manter) |
| `webhooks.message` | Texto de status para UI admin |
| `retention.processEnabled` / `processSource` | Kill switch de processo (`MessageRetention:Enabled`) — SoT env; somente leitura |
| `retention.enabled` / `retentionDays` | Política do tenant em `messaging.message_retention_settings` — gravável |
| `retention.defaultRetentionDays` | Default operacional (sugerido 90; ADR-018 / D-03) |
| `retention.message` | Texto de status para UI admin |

Regras:

- Secrets de OpenRouter / SMTP password **só** via env / secret store (ADR-012)
- Secret de webhook é a exceção (B-048): gravável via admin API; resposta só máscara
- Membro comum e Auditor (sem `workspace.admin`) → `403` em GET/PUT
- Tenant do actor; nunca aceitar `tenantId` do body
- Retenção (B-047): `retentionDays` entre 1 e 3650; purge hard-delete só com **processo** `MessageRetention:Enabled=true` **e** `retention.enabled=true` no tenant; job no worker; audit `message.purge`

### Retenção / purge (B-047)

- Soft-delete permanece o default de exclusão (ADR-018); APIs de leitura redigem body
- Hard-delete: worker `MessageRetentionPurgeDispatcher` remove mensagens com `DeletedAt` anterior ao cutoff (`now - retentionDays`)
- Cascata mínima: remove `reactions` da mensagem; anexa `attachments.MessageId = null` (metadados preservados; sem delete MinIO neste slice)
- `ConversationSequence` / `seq` não são reescritos
- Off por default (processo + tenant)

### Webhooks outbound (B-048)

- Tabela `integrations.webhook_endpoints` (1 endpoint por tenant): `Enabled`, `Url`, `Secret`
- Delivery best-effort no `OutboxProcessor` **após** realtime, apenas `MessageCreated`
- `POST` do payload JSON do outbox; headers:
  - `X-VibeChat-Event: MessageCreated`
  - `X-VibeChat-Delivery-Id: <outboxId>`
  - `X-VibeChat-Signature: sha256=<hmac-hex>` (HMAC-SHA256 do body com o secret)
- URL: `https` (ou `http://localhost` / `127.0.0.1` em lab); timeout ~5s; falha não reprocessa outbox
- RLS + query filter por `TenantId`

### Auditoria de conversa (B-067)

Distinta do feed `audit_events` (B-042). Viewer compliance: admin/Auditor com `admin.dashboard` lê histórico completo **dentro do tenant**, inclusive DMs onde não é membro e corpos soft-deleted (ADR-018). Membro comum → 403. Canal/thread de outro tenant → 403. Histórico normal (`GET /channels/.../messages`) continua redigindo body deletado e exigindo membership.

Provisionamento (B-068): no primeiro login OIDC, `EnsureProfile` vincula stub `pending:{email}` ao `sub` real — a membership já provisionada pelo admin passa a valer sem self-signup.

## Notifications / Email (B-043)

```csharp
public interface IEmailSender
{
    string Name { get; }
    bool IsEnabled { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
```

- Implementações: `NullEmailSender` (lab), `SmtpEmailSender` (SMTP genérico; Mailpit em dev — D-10; no-op se efetivamente desligado)
- Off by default; senha só via env (`EMAIL__*` / `SMTP_*`); host/user/from/enabled podem ter override por tenant (`notifications.email_settings`, B-069)
- Caso inicial: e-mail ao alterar papel de membro (`MemberRoleChangedEmailEvent` no outbox — fora do hot path de `SendMessage`)

## AI

```csharp
public interface IAiCompletionProvider
{
    string Name { get; }
    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct);
}
```

- Implementações: `NullAiProvider` (default quando `Ai:Enabled=false`), `MockAiProvider`, `OpenRouterAiProvider` (opt-in + API key)
- Request deve carregar `TenantId` e evidência de autorização do contexto
- Provider externo **off by default** (D-06); nunca no hot path de `SendMessage`

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/ai/summarize` | membership + `ai.summarize` | Resumo das últimas ~20 msgs do canal; exige `Ai:Enabled` + `AiSettings` do workspace; `503` + `{ error: AiDisabled }` se off; `502` + `ProviderError` se provider externo falhar; nunca envia PII a terceiros sem flag+key |
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/ai/suggest-reply` | membership + `ai.suggest_reply` | Sugestão de resposta (efêmera) com base nas últimas ~20 msgs; mesmas flags/`AiSettings` que summarize; `503`/`502` iguais; fora do hot path de `SendMessage` (B-045 / D-06) |

`AiSummaryResponse`: `{ summary }`  
`AiSuggestReplyResponse`: `{ suggestion }`  
`AiSummaryErrorResponse`: `{ error, message }`

---

## Presence

```csharp
public interface IPresenceService
{
    Task SetOnlineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken ct);
    Task SetAwayAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken ct);
    Task HeartbeatAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken ct);
    Task SetOfflineAsync(TenantId tenantId, UserId userId, string connectionId, CancellationToken ct);
    Task<int> CountOnlineAsync(TenantId tenantId, CancellationToken ct);
    Task<IReadOnlyDictionary<UserId, PresenceStatus>> GetStatusesAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken ct);
}
```

Redis (prefixo tenant-first, ver `multi-tenant.md`):

| Uso | Key |
|-----|-----|
| Presence status | `t:{tenantId}:presence:status:{userId}` (TTL) |
| Presence connections | `t:{tenantId}:presence:conn:{userId}` |
| Presence users set | `t:{tenantId}:presence:users` |
| Typing hash | `t:{tenantId}:typing:{channelId}` |

Typing permanece em `ITypingService`.

---

## Rate limit (Platform)

```csharp
public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct);
}
```

Fase 1: Redis fixed-window (`INCR` + `EXPIRE`). Keys: `t:{tenantId}:rl:send:{userId}`, `t:{tenantId}:rl:hub:{userId}`. Aplicado em `POST .../messages` (429) e hub `JoinChannel`/`SendTyping` (`HubException`). Config: `RateLimit:SendPerMinute`, `RateLimit:HubPerMinute`. Sem Redis configurado: fail-open.

---

## Platform — Outbox

```csharp
public interface IOutboxWriter
{
    Task EnqueueAsync(OutboxEnvelope envelope, CancellationToken ct);
}

public interface IOutboxProcessor
{
    Task ProcessBatchAsync(CancellationToken ct);
}
```

`IOutboxWriter` é chamado **dentro** da unidade de trabalho do módulo de origem.

---

## Versionamento

- Eventos novos: preferir additive (campos opcionais)
- Renomear `EventType` só com dual-publish temporário
- Contratos C#: mudanças breaking exigem atualização coordenada Api + Worker na mesma release (monólito facilita)

## Anti-padrões

- Passar entidades EF entre módulos
- Compartilhar `DbContext` “god” sem fronteiras
- Publicar eventos SignalR diretamente de um módulo de domínio sem passar por `IRealtimePublisher` / outbox
- Ler `HttpContext` dentro de módulos de domínio (usar `ITenantContext`)
