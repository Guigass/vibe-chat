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
| Seq | long |
| AuthorUserId | Guid |
| Body | string |
| CreatedAt | DateTimeOffset |
| EditedAt | DateTimeOffset? |
| DeletedAt | DateTimeOffset? |

### EditMessage

| Campo | Tipo | Notas |
|-------|------|-------|
| Body | string | Obrigatório; só autor com `message.edit.own` |

### Soft-delete Message

- `DELETE /api/v1/channels/{channelId}/messages/{messageId}`
- Autor com `message.delete.own` **ou** papel com `message.delete.any`
- Soft-delete (`DeletedAt`); body oculto nas leituras (ADR-018)

### Directory — members & DMs

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/workspaces/{workspaceId}/members` | Membros do workspace (membership obrigatória — D-07) |
| `POST /api/v1/workspaces/{workspaceId}/dms` | Body `{ userId }`; get-or-create DM 1:1 (`ChannelType.Direct`) |

`ChannelResponse` pode incluir `peerUserId` / `peerDisplayName` para DMs. Channels `Private`/`Direct`/`Group` só aparecem na listagem para membros do canal.

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
| `typing.started` | Typing |
| `presence.changed` | Presence |

---

## Files

```csharp
public interface IFileStorage
{
    Task<PresignedUpload> CreateUploadAsync(…);
    Task<PresignedDownload> CreateDownloadAsync(…);
}
```

---

## Search

```csharp
public interface ISearchIndexer
{
    Task IndexMessageAsync(MessageIndexed doc, CancellationToken ct);
}

public interface ISearchQuery
{
    Task<SearchResultPage> SearchMessagesAsync(SearchMessagesQuery q, CancellationToken ct);
}
```

Fase 1: implementação PostgreSQL FTS (ADR-011).

---

## AI

```csharp
public interface IAiAssistant
{
    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct);
}
```

- Implementações: `NoOpAiAssistant`, `OpenRouterAiAssistant`
- Request deve carregar `TenantId` e evidência de autorização do contexto

---

## Presence

```csharp
public interface IPresenceService
{
    Task HeartbeatAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task SetTypingAsync(Guid tenantId, Guid conversationId, Guid userId, CancellationToken ct);
}
```

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
