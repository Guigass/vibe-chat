# Contratos Compartilhados entre Módulos — VibeChat

## Objetivo

Definir as interfaces, DTOs e eventos que permitem colaboração entre módulos **sem** acoplamento às implementações.

**Onde vivem os contratos (estado atual):** não há assembly `VibeChat.Contracts`. Interfaces técnicas
compartilhadas ficam em `modules/BuildingBlocks` (`VibeChat.BuildingBlocks`); contratos de domínio
ficam no módulo dono (ex.: `IChannelMembershipReader` em Conversations, `IWorkspaceMembershipReader`
em Tenancy, features de IA em `modules/AI`). Implementações de persistência/adapters ficam em
`src/VibeChat.Infrastructure` e o composition root em `apps/api` / `apps/worker`.

## Princípios

1. Contratos são estáveis e versionados com cuidado (mudanças breaking exigem ADR ou migração)
2. Sem dependência de EF Core, SignalR ou SDKs de cloud nos contratos de domínio
3. Eventos de integração são **imutáveis** e serializáveis (JSON)
4. Queries entre módulos via interfaces estreitas (CQRS leve)
5. Estado, outbox, audit e projeções têm finalidades diferentes; ver
   [`estado-eventos-auditoria-projecoes.md`](estado-eventos-auditoria-projecoes.md)

---

## Contexto de tenancy

```csharp
// Em VibeChat.BuildingBlocks — forma real
public interface ITenantContext
{
    TenantId TenantId { get; }
    bool HasTenant { get; }
    void SetTenant(TenantId tenantId);
    UserId UserId { get; }
    bool HasUser { get; }
    void SetUser(UserId userId);
    string? JobRole { get; }
    void SetJobRole(string? jobRole);
}
```

`TenantId` e `UserId` vêm exclusivamente do token/contexto autenticado (`ICurrentUser` carrega identity/roles do principal).
Tipos de principal, sessão, device e delegação seguem
[`modelo-identidade-principals.md`](modelo-identidade-principals.md).

---

## Membership e autorização entre módulos

Não existe `IMembershipQuery` monolítico. AuthZ combina:

1. **Membership** — leitores de domínio por bounded context
2. **Permissões** — `IPermissionChecker` + `RolePermissionCatalog` em BuildingBlocks

```csharp
// modules/Tenancy
public interface IWorkspaceMembershipReader
{
    Task<bool> IsMemberAsync(TenantId tenantId, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Role>> GetRolesAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken);
}

// modules/Conversations
public interface IChannelMembershipReader
{
    Task<bool> CanAccessAsync(TenantId tenantId, ChannelId channelId, UserId userId, CancellationToken cancellationToken);
}

// modules/BuildingBlocks
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(TenantId tenantId, UserId userId, string permission, CancellationToken cancellationToken);
}
```

Implementação concreta: `PermissionChecker` em Infrastructure também satisfaz os leitores de
membership. Endpoints tipicamente checam membership + `HasPermissionAsync` (ex.:
`Permissions.Message.Send`) no composition root — não há `CanPostAsync` como porta separada.

---

## Messaging — comandos e DTOs

### SendMessage

| Campo | Tipo | Notas |
|-------|------|-------|
| ConversationId | Guid | Channel root ou thread |
| Body | string | Markdown restrito (B-081): `**negrito**`, `*itálico*`, `~~riscado~~`, `` `código` ``, bloco ` ``` ` com linguagem opcional, `> citação`, listas `-`/`1.`, URLs `http(s)://…` auto-link no cliente; renderização só no web — persistência/busca/export/auditoria usam o texto original; máx. **8000** code units UTF-16 (`MessageBodyPolicies.MaxLength`); vazio permitido somente com `AttachmentIds` prontos |
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
| ReplyToMessageId | Guid? | Citação inline (B-084); validado no mesmo canal/thread |
| ReplyTo | `{ messageId, authorName, preview, deleted }`? | Prévia resolvida no servidor (até 140 chars); history + Accepted + hub |
| ForwardedFromMessageId | Guid? | Origem do encaminhamento (B-085); cabeçalho histórico |
| ForwardedFromChannelId | Guid? | Canal de origem do encaminhamento |
| ForwardedFrom | `{ messageId, channelId, channelName, authorName, createdAt, isDirect }`? | Cabeçalho resolvido (permanece se a origem for apagada depois); em DM, `channelName` é o display name do peer (nunca o slug `dm:guid:guid`) e `isDirect = true` |
| ReplyCount | int | Contagem de replies (timeline do canal) |
| Attachments | AttachmentDto[] | Metadados prontos (sem URL) |
| Reactions | ReactionSummaryDto[] | `{ emoji, count, me }` agregado |

`ReplyToMessageId` de outro canal → 400 `ReplyToDifferentChannel`. Inexistente → 400 `ReplyToNotFound`. Soft-delete da original: `replyTo.deleted = true`, preview vazio (UI: “Mensagem removida”).

### Histórico paginado (B-089)

`GET /api/v1/channels/{channelId}/messages`

| Param | Notas |
|-------|-------|
| `limit` | 1–100; default 50 |
| `after` | `seq` exclusivo — página para frente |
| `before` | `seq` exclusivo — página para trás |
| `around` | centraliza janela em torno de um `seq` |
| *(nenhum cursor)* | última janela (`limit` mensagens mais recentes) |

`after`, `before` e `around` são mutuamente exclusivos → 400 `InvalidMessagePagination`.

Resposta:

```json
{
  "messages": [ /* MessageDto[] */ ],
  "hasMoreBefore": true,
  "hasMoreAfter": false
}
```

Membership + RLS idênticos ao histórico anterior; `seq` de outro canal nunca vaza conteúdo alheio.

### ForwardMessage (B-085)

`POST /api/v1/workspaces/{workspaceId}/messages/{messageId}/forward`

| Campo | Tipo | Notas |
|-------|------|-------|
| TargetChannelIds | Guid[] | 1–5 destinos; membership obrigatória em cada um |
| Comment | string? | Opcional; se vazio, body da nova mensagem copia o da origem |
| IdempotencyKey | string | Obrigatório; reenvio não duplica o fan-out |

- Membership na origem **e** em cada destino; qualquer destino inválido → **403** e **nenhum** envio parcial.
- Cria uma mensagem nova por destino (`seq` + outbox `MessageCreated` próprios).
- Anexos por **referência**: novas linhas em `files.attachments` com o mesmo `StorageKey`; `ReferenceCount` compartilhado. Índice de `StorageKey` **não** é único.
- Audit `message.forward` com origem e destinos.
- Purge (B-047): se o blob ainda tem irmãos com o mesmo `StorageKey`, remove só a linha da mensagem purgada e ajusta `ReferenceCount`; blob MinIO só seria elegível a delete quando `AttachmentReferencePolicies.CanDeleteBlob` (0 linhas).
### EditMessage

| Campo | Tipo | Notas |
|-------|------|-------|
| Body | string | Obrigatório; só autor com `message.edit.own`; máx. **8000** code units UTF-16 |

Erro `MessageBodyTooLong` (400):

```json
{
  "error": "MessageBodyTooLong",
  "message": "A mensagem excede o limite de 8000 caracteres.",
  "maxLength": 8000
}
```

Validação ocorre em `POST .../messages`, `POST .../threads/{threadId}/messages` e `PUT .../messages/{messageId}` **antes** da transação.

### Soft-delete Message

- `DELETE /api/v1/channels/{channelId}/messages/{messageId}`
- Autor com `message.delete.own` **ou** papel com `message.delete.any`
- Soft-delete (`DeletedAt`); body oculto nas leituras (ADR-018)
- Também cobre replies de thread do canal (authZ por membership do canal pai)

### Reactions

| Endpoint | Notas |
|----------|-------|
| `PUT /api/v1/channels/{channelId}/messages/{messageId}/reactions` | Toggle (`{ emoji }`); membership + `message.react`; emoji Unicode válido (até 8 code points, sem texto); unique `(tenant, message, user, emoji)` |
| `GET /api/v1/channels/{channelId}/messages/{messageId}/reactions/{emoji}/users` | Quem reagiu com o emoji; membership + `message.react`; `{ emoji, users: [{ userId, displayName }], total }` |

`MessageDto.reactions`: `{ emoji, count, me }[]` (agregado no history/thread). Outbox `ReactionChangedEvent` → hub `ReactionChanged` no grupo do canal pai (`messageId`, `emoji`, `userId`, `added`, `topUsers`, `reactions`).

### Menções (B-082)

| Artefato | Contrato |
|----------|----------|
| Corpo | Tokens estáveis `<@userId>`, `<@here>`, `<@channel>` |
| Tabela | `messaging.message_mentions` (`TenantId`, `MessageId`, `ChannelId`, `MentionedUserId?`, `Kind`: `User`/`Here`/`Channel`) |
| Escrita | Mesma transação de `SendMessage` |
| Autocomplete | `GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/members?query=` — membership do canal; até 8 resultados |
| Unread | `GET /api/v1/channels/{channelId}/unread-count` → `{ unreadCount, mentionCount }` |
| Permissão | `@canal` exige `channel.mention_all` (default: quem pode postar) |
| Hub `MessageCreated` | `mentionedUserIds: uuid[]`, `mentionKinds: string[]`; cada cliente deriva `mentionsMe` localmente; `clientMessageId` ecoa o `messageId` aceito do cliente (reconcilia UI otimista) |

### Threads

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/messages/{messageId}/threads` | Get-or-create thread ancorada na mensagem pai; membership do canal |
| `GET /api/v1/threads/{threadId}` | Metadados + parent + `replyCount` |
| `GET /api/v1/threads/{threadId}/messages` | Histórico da conversa da thread (`seq` próprio) |
| `POST /api/v1/threads/{threadId}/messages` | Reply; idempotência + seq + outbox; `threadId` no evento hub |

Replies usam `ConversationId = ThreadId` (seq separado do canal). Fan-out SignalR continua no grupo do **canal pai**, com `threadId` / `conversationId` / `parentMessageId` (âncora da thread) no payload.

### Directory — spaces, channels, members & DMs

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/workspaces/{workspaceId}/spaces` | Lista spaces do workspace (membership obrigatória — D-07); ordenado por `order` |
| `POST /api/v1/workspaces/{workspaceId}/spaces` | Body `{ name, order? }`; exige `channel.create` |
| `GET /api/v1/workspaces/{workspaceId}/channels` | Channels do workspace; `spaceId` e `topic` opcionais no response |
| `POST /api/v1/workspaces/{workspaceId}/channels` | Body `{ name, type, spaceId? }`; exige `channel.create`; `spaceId` deve pertencer ao workspace |
| `PUT /api/v1/workspaces/{workspaceId}/channels/{channelId}/topic` | Body `{ topic }` (máx. 250; vazio limpa); membership + `channel.create`; rejeita `Direct` (B-087 `/topico`) |
| `GET /api/v1/workspaces/{workspaceId}/commands` | Descoberta de slash commands disponíveis ao ator (B-087); membership; filtrado por permissão — ver tabela abaixo |
| `GET /api/v1/workspaces/{workspaceId}/members` | Membros do workspace (membership obrigatória — D-07); inclui `role` |
| `GET /api/v1/workspaces/{workspaceId}/roles` | Papéis atribuíveis (`Member`, `Moderator`, `Auditor`, `Admin`); exige `workspace.admin` no workspace |
| `POST /api/v1/workspaces/{workspaceId}/members` | Convite/provisionamento (B-068). Body `{ email, displayName?, role? }` (`role` default `Member`); exige `workspace.admin`; cria perfil stub `pending:{email}` se o usuário ainda não logou; 409 se já membro; rejeita `Guest`/`Bot`/owners; audit `member.invite`; e-mail opcional via outbox se `Email:Enabled`. Sem self-signup — IdP (Keycloak) continua responsável pela autenticação |
| `PUT /api/v1/workspaces/{workspaceId}/members/{userId}/role` | Body `{ role }`; owner/admin (`workspace.admin`); não permite auto-elevação; rejeita `Guest`/`Bot`/`WorkspaceOwner`/`PlatformOwner` (D-07); audit `member.role.change`; e-mail opcional via outbox se `Email:Enabled` |
| `GET /api/v1/workspaces/{workspaceId}/presence` | Status `online`/`away`/`offline` dos membros (Redis TTL) |
| `POST /api/v1/workspaces/{workspaceId}/dms` | Body `{ userId }`; get-or-create DM 1:1 (`ChannelType.Direct`) |

`ChannelResponse` inclui `spaceId?`, `topic?` e, para DMs, `peerUserId` / `peerDisplayName`. Channels `Private`/`Direct`/`Group` só aparecem na listagem para membros do canal. Spaces agrupam channels na UI; DMs ficam fora de spaces.

Slash commands (B-087) — o cliente traduz o comando para as APIs existentes; a lista vem do servidor:

| Comando | Usage | Permissão / condição | Endpoint alvo |
|---------|-------|----------------------|---------------|
| `dm` | `/dm @pessoa` | membership | `POST …/dms` |
| `topico` | `/topico <texto>` | `channel.create` (+ canal não-DM) | `PUT …/channels/{id}/topic` |
| `convidar` | `/convidar <email>` | `workspace.admin` | `POST …/members` |
| `resumir` | `/resumir` | `ai.summarize` | `POST …/ai/summarize` |
| `apagar` | `/apagar` | `message.delete.own` | `DELETE …/messages/{id}` |
| `ajuda` | `/ajuda` | membership | UI local + esta lista |

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
  "topUsers": ["Alice", "Bob"],
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

Semântica de ack, dedupe, gap-fill e reconnect:
[`protocolo-sync-realtime.md`](protocolo-sync-realtime.md). Topologia
multi-instância: [`signalr-ha.md`](signalr-ha.md).

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
| `MessageCreated` | Nova mensagem — payload inclui `messageId`, `clientMessageId` (mesmo UUID do `messageId` do comando), `channelId`, `conversationId`, `threadId?`, `parentMessageId?`, `replyToMessageId?`, `replyTo?`, `forwardedFromMessageId?`, `forwardedFromChannelId?`, `forwardedFrom?`, `sequence`, `authorId`, `body`, menções/anexos |
| `MessageEdited` | Edição |
| `MessageDeleted` | Soft delete |
| `ReactionChanged` | Toggle de reação (payload com resumo agregado) |
| `Typing` | Typing (TTL curto Redis); hub publica com `Clients.OthersInGroup` (B-071) — autor não recebe o próprio evento |
| `PresenceChanged` | Presence `online`/`away`/`offline` (hub group `t:{tenantId}`) |

Cliente deduplica ack HTTP + fan-out hub por `messageId` / `clientMessageId` (case-insensitive) — ver `protocolo-sync-realtime.md`.

Grupos SignalR: canal `t:{tenantId}:c:{channelId}` (mensagens/typing/reações); tenant `t:{tenantId}` (presence).

Hub (além de `JoinChannel` / `LeaveChannel` / `SendTyping`):

| Método | Notas |
|--------|-------|
| `Heartbeat(tenantId)` | Renova presença online (TTL ~45s); authZ via membership no tenant |
| `SetAway(tenantId)` | Marca away; authZ via membership no tenant |

---

## Files

State machine, verificação, scan, derivados e lifecycle:
[`pipeline-anexos.md`](pipeline-anexos.md).

```csharp
public interface IObjectStorage
{
    Task<PresignedUpload> CreateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken ct);
    Task<PresignedDownload> CreateDownloadUrlAsync(string storageKey, string fileName, TimeSpan ttl, CancellationToken ct);
    Task<ObjectStat?> StatObjectAsync(string storageKey, CancellationToken ct);
    Task DeleteObjectAsync(string storageKey, CancellationToken ct);
}
```

`Attachment.ReferenceCount` (default 1): quantas linhas compartilham o `StorageKey` após encaminhar (B-085).
### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/attachments` | Body `{ fileName, contentType, sizeBytes, kind?, durationMs?, waveform? }` → URL pré-assinada PUT; exige membership + `file.upload`; `kind=Audio` exige `durationMs` e aceita `waveform` (0–100, ≤100 pts) |
| `POST /api/v1/channels/{channelId}/attachments/{id}/complete` | Confirma objeto no MinIO; status `Ready` |
| `GET /api/v1/channels/{channelId}/attachments/{id}/download` | URL pré-assinada GET; exige membership + `file.download` |
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/messages/{messageId}/attachments/{attachmentId}/transcribe` | membership + `ai.transcribe`; `{ text, language, provider }` efêmero (não persiste); `503 AiDisabled` se IA off |

Regras: keys prefixadas por tenant (`tenants/{tenantId}/…`); MIME/tamanho via resolver efetivo (`Files:*` env como teto + `files.settings` por tenant quando ADR-020 habilitado); body da mensagem pode ser vazio se houver `AttachmentIds` prontos no `SendMessage`.

Limites (config `Files:*` / override tenant, default entre parênteses):

| Chave | Default | Validação |
|-------|---------|-----------|
| `MaxSizeBytes` | `10485760` (10 MiB) | Por arquivo no `initiate`; tenant ≤ teto env |
| `MaxAttachmentsPerMessage` | `10` | Contagem de `attachmentIds` no `SendMessage`; excedente → `400` `{ error: "TooManyAttachments", max }` |

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
| `GET /api/v1/admin/settings?workspaceId=` | Settings sensíveis mascarados (B-069 / ADR-020); exige `workspace.admin` (Auditor com só `admin.dashboard` → 403); `workspaceId` opcional (default: primeiro workspace do actor) |
| `PUT /api/v1/admin/settings` | Atualiza flags não-secretas (AI/email/webhooks/retention/files/rateLimit); mesma authZ; rejeita secrets no body (`SecretsNotWritable`); audit `settings.change` |
| `POST /api/v1/admin/settings/credentials/openrouter/rotate` | Rotaciona API key OpenRouter (envelope AES-GCM em `ai.settings`); body `{ workspaceId?, value }`; resposta `{ configured, mask, keyVersion, rotatedAt }`; `503` se keyring indisponível |
| `POST /api/v1/admin/settings/credentials/smtp/rotate` | Rotaciona senha SMTP do tenant (envelope em `notifications.email_settings`); mesma forma de resposta |
| `POST /api/v1/admin/settings/credentials/webhook/rotate` | Rotaciona signing secret do webhook (envelope em `integrations.webhook_endpoints`); mesma forma |
| `POST /api/v1/admin/settings/encryption/reencrypt` | Regrava envelopes do workspace/tenant para `ActiveKeyVersion`; migra plaintext legado de webhook; audit `settings.encryption.reencrypt` |
| `GET /api/v1/admin/workspaces/{workspaceId}/export` | Export compliance do workspace (B-046); ZIP `application/zip` com JSON (`manifest`, `workspace`, `members`, `spaces`, `channels`, `threads`, `messages`, `attachments` metadata); corpos soft-deleted incluídos (paridade B-067); **sem** binários MinIO; exige `workspace.admin` (Auditor → 403); audit `workspace.export`; workspace fora do tenant/membership → 403 |

`AuditEventResponse`: `id`, `action`, `entityType`, `entityId`, `actorUserId`, `occurredAt`, `metadataJson`.

`AdminConversationResponse`: `id`, `workspaceId`, `name`, `type`, `spaceId`, `peerUserId`, `peerDisplayName`.

`AdminConversationMessageResponse`: `id`, `channelId`, `conversationId`, `sequence`, `authorId`, `authorName`, `body` (sempre o valor persistido), `createdAt`, `editedAt`, `deletedAt`, `deletedBy`, `deletedByName`, `threadId`, `replyToMessageId`, `replyCount`, `attachments`.

Ações mínimas: `admin.login`, `channel.create`, `space.create`, `message.send`, `message.delete`, `attachment.upload`, `member.role.change`, `member.invite`, `settings.change`, `settings.credential.rotate`, `settings.encryption.reencrypt`, `workspace.export`, `message.purge`.

### Export de workspace (B-046)

- Formato: `vibechat.workspace.export.v1` (`manifest.json.format`)
- AuthZ alinhada a settings sensíveis (`workspace.admin`); não usar só `admin.dashboard`
- Mensagens: body persistido inclusive soft-delete (`deletedAt` / `deletedBy`)
- Anexos: metadados apenas (`fileName`, `contentType`, `sizeBytes`, `checksumSha256`, `status`) — sem `storageKey` nem bytes
- Tenant do actor; nunca aceitar `tenantId` do body

### Settings sensíveis (B-069 / ADR-020)

`SensitiveSettingsResponse`:

| Campo | Notas |
|-------|-------|
| `workspaceId` | Workspace alvo (AI workspace settings) |
| `ai.processEnabled` / `processSource` | Flag de processo (`Ai:Enabled`) — SoT env; somente leitura |
| `ai.workspaceEnabled` / `provider` | `ai.settings` do workspace — gravável via PUT |
| `ai.apiKeyConfigured` / `apiKeyMask` / `apiKeyKeyVersion` / `apiKeyRotatedAt` / `apiKeySource` | Máscara `••••last4`; **nunca** valor em claro; rotação via endpoint dedicado |
| `email.*` | Enabled/host/port/user/from/startTls (override tenant); senha só máscara/versão/fonte |
| `webhooks.status` | `unconfigured` \| `disabled` \| `active` (B-048) |
| `webhooks.enabled` / `url` / `urlConfigured` | Endpoint HTTP do tenant; URL gravável via PUT |
| `webhooks.secretConfigured` / `secretMask` / `secretKeyVersion` / `secretRotatedAt` / `secretSource` | HMAC secret mascarado; rotação via endpoint dedicado |
| `webhooks.message` | Texto de status para UI admin |
| `retention.processEnabled` / `processSource` | Kill switch de processo (`MessageRetention:Enabled`) — SoT env; somente leitura |
| `retention.enabled` / `retentionDays` | Política do tenant em `messaging.message_retention_settings` — gravável |
| `retention.defaultRetentionDays` | Default operacional (sugerido 90; ADR-018 / D-03) |
| `retention.message` | Texto de status para UI admin |
| `files.*` | Limites por tenant (`files.settings`); teto = env/`AttachmentPolicies`; gravável |
| `rateLimit.sendPerMinute` / `hubPerMinute` | Limites por tenant; efetivo = `min(DB, env)`; gravável |
| `encryption.activeKeyVersion` / `credentialsUsingActiveKey` / `databaseOverridesEnabled` | Metadata do keyring (sem nomes de variáveis com valor) |

Regras:

- Credenciais externas (OpenRouter, SMTP password, webhook HMAC) em envelope AES-GCM no DB quando `RuntimeSettings:DatabaseOverridesEnabled` (ADR-020); chave mestra só no env
- Fallback env para AI/SMTP enquanto não houver envelope; envelope inválido falha fechado
- PUT geral **nunca** aceita secrets (`SecretsNotWritable`); usar `POST .../credentials/*/rotate`
- Membro comum e Auditor (sem `workspace.admin`) → `403` em GET/PUT/rotate/reencrypt
- Tenant do actor; nunca aceitar `tenantId` do body
- Retenção (B-047): `retentionDays` entre 1 e 3650; purge hard-delete só com **processo** `MessageRetention:Enabled=true` **e** `retention.enabled=true` no tenant; job no worker; audit `message.purge`
- Files/RateLimit: admin pode restringir, não ultrapassar teto do env

### Retenção / purge (B-047)

- Soft-delete permanece o default de exclusão (ADR-018); APIs de leitura redigem body
- Hard-delete: worker `MessageRetentionPurgeDispatcher` remove mensagens com `DeletedAt` anterior ao cutoff (`now - retentionDays`)
- Cascata mínima: remove `reactions` da mensagem; anexos com `StorageKey` exclusivo fazem `attachments.MessageId = null` (metadados preservados); anexos compartilhados (B-085) removem só a linha da mensagem purgada e ajustam `ReferenceCount` nos irmãos — sem delete MinIO enquanto houver referência
- `ConversationSequence` / `seq` não são reescritos
- Off por default (processo + tenant)

### Webhooks outbound (B-048)

- Tabela `integrations.webhook_endpoints` (1 endpoint por tenant): `Enabled`, `Url`, envelope AES-GCM do signing secret (+ coluna `Secret` legado em dual-read até migration contract)
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
- Off by default; host/user/from/enabled override por tenant (`notifications.email_settings`); senha via env **ou** envelope AES-GCM no DB (ADR-020 / B-069)
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

Fase 1: Redis fixed-window (`INCR` + `EXPIRE`). Keys: `t:{tenantId}:rl:send:{userId}`, `t:{tenantId}:rl:hub:{userId}`. Aplicado em `POST .../messages` (429) e hub `JoinChannel`/`SendTyping`/`Heartbeat`/`SetAway` (`HubException`). Config efetiva: `min(tenant DB, RateLimit:* env)` (ADR-020). Sem Redis configurado: fail-open.

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

### Cliente web — `GET /version.json` (B-165)

Artefato estático público servido pelo container/nginx do web (não pela API).
Sem autenticação, sem `tenant_id`, sem secret.

| Campo | Tipo | Notas |
|-------|------|-------|
| `name` | string | Fixo `VibeChat.Web` |
| `version` | string | SemVer do pacote web (ex.: `0.1.0`) |
| `buildId` | string | Identificador curto do build (git SHA truncado ou equivalente CI) |

O cliente embute o mesmo `version`/`buildId` no bootstrap e compara com
`/version.json` (boot, focus/visibility, intervalo longo) e com
`SwUpdate.versionUpdates` quando o service worker está ativo. Reload só após
CTA explícito do usuário. `GET /api/v1/admin/version` permanece admin e descreve
a API — não é dependência do caminho de update do PWA.

Nginx de referência (`apps/web/nginx.conf`): `index.html`, `ngsw.json` e
`version.json` usam `Cache-Control: no-cache, no-store, must-revalidate`;
bundles com hash de conteúdo podem ser imutáveis.

## Anti-padrões

- Passar entidades EF entre módulos
- Compartilhar `DbContext` “god” sem fronteiras
- Publicar eventos SignalR diretamente de um módulo de domínio sem passar por `IRealtimePublisher` / outbox
- Ler `HttpContext` dentro de módulos de domínio (usar `ITenantContext`)
