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

`TenantId` e `UserId` vêm exclusivamente do token/contexto autenticado.
`ICurrentUser` carrega **identidade** do principal (`sub`, email, display name).
Claims JWT de role (`ClaimTypes.Role` / `ICurrentUser.Roles`) **não** autorizam
ações de produto — ver B-176 e a seção abaixo.
Tipos de principal, sessão, device e delegação seguem
[`modelo-identidade-principals.md`](modelo-identidade-principals.md).

---

## Membership e autorização entre módulos

Não existe `IMembershipQuery` monolítico. AuthZ combina:

1. **Membership** — leitores de domínio por bounded context
2. **Permissões** — `IPermissionChecker` + `RolePermissionCatalog` em BuildingBlocks

**Fonte de verdade de papéis (B-176):** autorização de produto vem de
`tenancy.workspace_members.role` + `RolePermissionCatalog`. O JWT (Keycloak)
prova identidade (`sub`, email); realm roles do IdP são opcionais para SSO futuro
e **não** substituem membership. `PermissionChecker.GetRolesAsync` /
`HasPermissionAsync` leem só o DB. `GET /api/v1/me` e `GET /api/v1/workspaces`
expõem o papel da membership (não claims JWT). Sync Keycloak → membership é
B-128 (SCIM), fora do escopo atual.

### Perfil do caller — `GET`/`PUT /api/v1/me` (B-100)

Preferência pessoal, não de tenant. A mesma instância atende pessoas em idiomas
diferentes. Mensagens de erro da API continuam em inglês no `error`/`error.code`;
o cliente traduz pelo código (não usa `Accept-Language` no servidor).

| Método | Contrato |
|--------|----------|
| `GET /api/v1/me` | `{ userId, subject, email, displayName, roles, locale }` — `locale` é um de `pt-BR` \| `en` \| `es` \| `fr` \| `de` \| `it` \| `ja` \| `zh-CN` \| `ko` \| `ru` ou `null` se ainda não persistido |
| `PUT /api/v1/me` | Body `{ locale }` — só um locale suportado; outro valor → **400** `InvalidLocale`. Atualiza só o caller |

Logs do servidor permanecem em inglês e não interpolam texto traduzido.
`GET /api/v1/workspaces/{id}/commands.description` é locale-sensitive (mesmo shape; valor no idioma do caller).
Campo `message` em erros, quando existir, fica em inglês fixo — o cliente traduz por `error`.

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
membership. Endpoints tipicamente checam membership + permissão. Em Minimal APIs (B-174),
mutações e superfícies sensíveis declaram `RequirePermission(permission)` (metadata
`RequirePermissionAttribute`); o filtro de grupo `/api/v1` resolve tenant via
`ResolveWorkspaceAsync` / `ResolveChannelAsync` / membership admin e chama
`HasPermissionAsync` — **nunca** a partir de `tenantId` do body. Checagens condicionais
(EditOwn/DeleteOwn vs DeleteAny, autor vs admin) permanecem no handler.
Matriz endpoint × gate: [`docs/security/authz-matriz.md`](../security/authz-matriz.md) (B-175).

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
| Attachments | AttachmentDto[] | Metadados prontos (sem URL): `id`, `fileName`, `contentType`, `sizeBytes`, `status`, `kind`, `durationMs?`, `waveform?`, `thumbnailStatus?` (`Pending`\|`Ready`\|`Failed`), `width?`, `height?`, `pageCount?` (B-090) |
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
| Body | string | Obrigatório; autor com `message.edit.own` dentro da política B-107, ou moderação com `message.edit.any` quando `messaging.edit.allowModeratorOverride`; máx. **8000** code units UTF-16 |

Erro `MessageBodyTooLong` (400):

```json
{
  "error": "MessageBodyTooLong",
  "message": "Message exceeds the 8000-character limit.",
  "maxLength": 8000
}
```

Validação de tamanho ocorre em `POST .../messages`, `POST .../threads/{threadId}/messages` e `PUT .../messages/{messageId}` **antes** da transação.

Política de edição (B-107 / ADR-025), por tenant, default = comportamento anterior (edição do autor ligada, sem janela, override de moderação **desligado**):

| Código | HTTP | Quando |
|--------|------|--------|
| `EditDisabled` | 403 | `messaging.edit.enabled=false` e o ator é o autor |
| `EditRoleDenied` | 403 | papel do autor fora de `messaging.edit.roles` |
| `EditWindowExpired` | 422 | `now` > `createdAt` + `windowMinutes` |
| (Forbid vazio) | 403 | não autor e override desligado, ou sem a permissão |

`GET /api/v1/channels/{channelId}/messaging-policy` devolve só esses campos não secretos a quem tem `message.read` no canal (inclui guest). Alterar a política continua em `PUT /api/v1/admin/settings` (`workspace.admin`). `windowMinutes` nulo = sem limite; `0` ou acima de 525600 → `InvalidMessagingPolicy` (400).

### Soft-delete Message

- `DELETE /api/v1/channels/{channelId}/messages/{messageId}`
- Autor com `message.delete.own` dentro de `messaging.delete.*`, **ou** papel com `message.delete.any` quando `messaging.delete.allowModeratorOverride` (default **ligado**)
- Códigos estáveis: `DeleteDisabled` (403), `DeleteRoleDenied` (403), `DeleteWindowExpired` (422); não autor sem override continua 403 vazio
- Soft-delete (`DeletedAt`); body oculto nas leituras (ADR-018)
- Também cobre replies de thread do canal (authZ por membership do canal pai)
- **Planned (B-169):** com `contentAuditEnabled=true`, o evento `message.delete`
  em `audit.audit_events` inclui snapshot do body (e ids mínimos) no
  `metadataJson`, para sobreviver a purge (B-047). Com `false`, metadata sem
  body. Políticas de quem/quando pode apagar: B-107 (`messaging.delete.*`).

### Reactions

| Endpoint | Notas |
|----------|-------|
| `PUT /api/v1/channels/{channelId}/messages/{messageId}/reactions` | Toggle (`{ emoji }`); membership + `message.react`; emoji Unicode válido (até 8 code points, sem texto); unique `(tenant, message, user, emoji)` |
| `GET /api/v1/channels/{channelId}/messages/{messageId}/reactions/{emoji}/users` | Quem reagiu com o emoji; membership + `message.react`; `{ emoji, users: [{ userId, displayName }], total }` |

`MessageDto.reactions`: `{ emoji, count, me }[]` (agregado no history/thread). Outbox `ReactionChangedEvent` → hub `ReactionChanged` no grupo do canal pai (`messageId`, `emoji`, `userId`, `added`, `topUsers`, `reactions`).

### Mensagens fixadas (B-092)

| Artefato | Contrato |
|----------|----------|
| Tabela | `messaging.pinned_messages` (`TenantId`, `ChannelId`, `MessageId`, `PinnedByUserId`, `PinnedAt`); unique `(TenantId, ChannelId, MessageId)`; limite **20** por canal |
| `POST /api/v1/channels/{channelId}/messages/{messageId}/pin` | Membership + `message.pin`; mensagem do mesmo canal (não thread reply) → senão **400**; idempotente se já fixada; **400** `PinLimitReached` na 21ª |
| `DELETE /api/v1/channels/{channelId}/messages/{messageId}/pin` | Membership + `message.pin`; **204** se removida ou já ausente |
| `GET /api/v1/channels/{channelId}/pins` | Membership + `message.read`; lista `{ messageId, sequence, bodyPreview, authorName, pinnedByUserId, pinnedByName, pinnedAt }[]` + `{ count, limit }` |
| Hub | Outbox `PinChangedEvent` → `PinChanged` (`messageId`, `pinned`, `byUserId`) |
| Audit | `message.pin` / `message.unpin` |
| Sistema | Fixar/desafixar insere mensagem com corpo `<system:pin:{messageId}>` / `<system:unpin:{messageId}>` (transparência no canal) |
| Cascata | Soft-delete remove pin da lista e emite `PinChanged pinned=false` |
| `MessageDto` | `isPinned: bool` no history |

Permissão `message.pin` (default: Member, Moderator, Admin, Bot — não Guest/Auditor).

### Salvos pessoais (B-093)

| Artefato | Contrato |
|----------|----------|
| Tabela | `messaging.saved_messages` (`TenantId`, `UserId`, `MessageId`, `ChannelId`, `Note?`, `CompletedAt?`, `CreatedAt`); unique `(TenantId, UserId, MessageId)` |
| `POST /api/v1/workspaces/{workspaceId}/saved` | Body `{ messageId, note? }`; mensagem legível agora; nota ≤280; idempotente (não duplica) |
| `PATCH /api/v1/workspaces/{workspaceId}/saved/{messageId}` | Body `{ note?, completed? }`; só o dono |
| `DELETE /api/v1/workspaces/{workspaceId}/saved/{messageId}` | **204** se removido ou já ausente |
| `GET /api/v1/workspaces/{workspaceId}/saved?completed=&limit=&cursor=` | Só itens do usuário; revalida membership (omite sem apagar); mensagem soft-deleted → `bodyPreview: "Mensagem removida"`, `messageRemoved: true`; em DM `channelName` é o display name do peer (não o slug `dm:…`); resposta `{ items, nextCursor, pendingCount }` |
| Hub | Nenhum (estado pessoal) |
| AuthZ | Nenhum admin lê salvos de terceiros; cross-tenant → **403** |

### Agendamento e lembretes (B-113)

Horário de parede + fuso IANA vira um instante UTC único. Lacuna ou ambiguidade de DST → **400** (`InvalidLocalTime` / `AmbiguousLocalTime`). Passado ou além de 366 dias → **400**.

| Artefato | Contrato |
|----------|----------|
| Tabelas | `messaging.scheduled_messages` (`TenantId`, `WorkspaceId`, `AuthorId`, `ChannelId`, `ThreadId?`, `ReplyToMessageId?`, `PlannedMessageId`, `Body`, `SendAtUtc`, `TimeZone`, `Status`, `ClientIdempotencyKey`, `SendIdempotencyKey`, `SentMessageId?`, `AttemptCount`, `ClaimedAt?`, `NextAttemptAt?`, `FailureCode?`); `messaging.reminders` (mesmo relógio; `UserId`, `TargetKind` `Time\|Message\|Thread`, `Note?`, `MessageId?`) |
| `POST /api/v1/channels/{channelId}/scheduled-messages` | Body `{ idempotencyKey, body, sendAtLocal, timeZone, replyToMessageId?, threadId? }`; membership + `message.send`; idempotente pela chave do cliente |
| `PATCH /api/v1/workspaces/{workspaceId}/scheduled-messages/{scheduledMessageId}` | Só o autor e só `Pending`; corpo e/ou horário; claim concorrente → **409** `ScheduleAlreadyClaimed` |
| `DELETE …/scheduled-messages/{scheduledMessageId}` | Cancela se `Pending` (**204**); já cancelado ou ausente → **204**; já claimed/sent → **409** |
| `GET /api/v1/workspaces/{workspaceId}/schedule?limit=&cursor=` | Lista pessoal (agendados + lembretes) do caller; `{ items, nextCursor }` |
| `POST /api/v1/workspaces/{workspaceId}/reminders` | Body `{ idempotencyKey, targetKind, remindAtLocal, timeZone, note?, messageId?, threadId? }`; `message.read`; alvo de mensagem/thread revalida leitura agora |
| `PATCH` / `DELETE …/reminders/{reminderId}` | Só o dono; mesma regra de `Pending` |
| Disparo | Worker `ScheduleDispatchDispatcher` (15s, job_role `schedule`): claim `FOR UPDATE SKIP LOCKED`; envio via `SendMessage` com `SendIdempotencyKey` estável `sched:{id}`; outbox `scheduled_message.due` / `reminder.due` |
| AuthZ no disparo | Membership + `message.send` revalidados. Perda de acesso → `MembershipRevoked`, sem mensagem, notificação ao autor sem canal nem corpo |
| Privacidade | Lembrete não vai ao grupo do canal. Hub `ReminderDue` / `ScheduledMessageDue` só no grupo do usuário. Web Push respeita `PushEnabled`, DND e `HidePreview` |
| Audit | `schedule.create/update/cancel/revoke`, `reminder.create/update/cancel/deliver` |

### Seguir thread (B-102)

Seguir é uma assinatura por usuário; não há evento de hub dedicado — o web client
refaz o cálculo local a partir do `MessageCreated` de replies (`threadId`) já em
trânsito. Não lidas são calculadas em leitura (`ConversationSequences.LastSequence
- ThreadSubscription.LastReadSeq`), nunca por agregação sobre `messages`.

| Artefato | Contrato |
|----------|----------|
| Tabela | `messaging.thread_subscriptions` (`TenantId`, `UserId`, `ThreadId`, `ChannelId`, `Source` enum `Manual\|Author\|Reply\|Mention`, `LastReadSeq`, `CreatedAt`); unique `(TenantId, UserId, ThreadId)` |
| `POST /api/v1/threads/{threadId}/subscription` | Membership do canal da thread + `message.read`; upsert `Source=Manual`; `LastReadSeq` = seq atual da thread (sem backfill de não lidas) |
| `DELETE /api/v1/threads/{threadId}/subscription` | **204** se removida ou já ausente |
| `PUT /api/v1/threads/{threadId}/subscription/read-cursor` | Body `{ lastReadSequence, allowRetrograde? }`; **404** sem assinatura |
| `GET /api/v1/workspaces/{workspaceId}/threads/following?cursor=&limit=` | Só threads do usuário; revalida membership por linha (omite sem apagar, como B-093); ordenado por atividade recente; `{ items: [{ threadId, channelId, channelName, channelType, rootPreview, rootDeleted, unreadCount, lastActivityAt }], nextCursor }` |
| `POST /api/v1/threads/{threadId}/messages/{messageId}/share-to-channel` | Membership + `message.send`; reusa o writer de forward (B-085) com o próprio canal da thread como único alvo — referência, não cópia de texto solto; `Message.ThreadId` da origem propagado no payload de forward (`ForwardedFromResponse.threadId`) para o client linkar de volta à thread |
| Auto-follow (mesma transação) | Autor da mensagem raiz → `Source=Author` na criação da thread (`POST .../messages/{messageId}/threads`); quem responde → `Source=Reply`, `LastReadSeq` = seq da própria resposta; quem é mencionado na thread → `Source=Mention`, sem tocar `LastReadSeq` se já existia |
| `notifications.channel_preferences.FollowAllThreads` | Bool; ativado via `PUT /api/v1/notifications/preferences/channels/{channelId}/follow-all-threads` (`{ enabled }`); auto-segue só threads **novas** do canal, não retroativo; `Level` da mesma tabela virou `NotificationLevel?` — linha pode existir só para carregar este flag, sem mute ativo |
| Web Push | `PushDispatcher` trata seguidor de thread como mention (`isMentioned \|\| followingThreadSet.Contains(userId)`) — passa do gate "None a menos que mencionado", mas DND/mute/read-cursor seguem suprimindo normalmente |
| AuthZ | Assinatura exige membership do canal da thread; perder membership esconde a linha da lista (não apaga); cross-tenant → **403** |

### Enquetes (B-096)

Enquete é uma mensagem (`seq` + outbox `MessageCreated` + idempotência). Discriminator = linha em `messaging.polls` (PK = `MessageId`). O `Body` da mensagem replica a pergunta (preview/FTS). Só canal com membership (não thread, não DM).

| Artefato | Contrato |
|----------|----------|
| Tabelas | `messaging.polls` (`TenantId`, `MessageId`, `ChannelId`, `CreatedByUserId`, `Question` 1–500, `AllowMultiple`, `Anonymous`, `ClosesAt?`, `ClosedAt?`); `messaging.poll_options` (`TenantId`, `Id`, `PollId`, `Text` 1–100, `Position` único por enquete); `messaging.poll_votes` (`TenantId`, `Id`, `PollId`, `OptionId`, `UserId`, `CreatedAt`); unique `(TenantId, PollId, OptionId, UserId)` |
| `POST /api/v1/channels/{channelId}/polls` | Body `{ messageId, idempotencyKey, question, options[2–10], allowMultiple?, anonymous?, closesAt? }`; membership + `message.send`; DM/grupo → **403**; `closesAt` no passado → **400**; **202** + `MessageResponse.poll` |
| `POST /api/v1/polls/{pollId}/votes` | Body `{ optionIds: uuid[] }`; membership + `message.send`; único: 1 opção (substitui); múltiplo: conjunto completo; fechada → **409**; outra tenant / invisível → **403** |
| `DELETE /api/v1/polls/{pollId}/votes` | Retira todos os votos do caller; fechada → **409**; invisível → **403** |
| `POST /api/v1/polls/{pollId}/close` | Autor ou `workspace.admin`; idempotente se já fechada; invisível → **403** |
| History / admin | `MessageDto.poll` / `AdminConversationMessageResponse.poll` agregado (contagens, `%`, `canVote`, `closedAt`); anônima **sem** `userId`/`voters` |
| Hub | Outbox `PollChangedEvent` → `PollChanged` (`messageId`, `channelId`, `poll`) |
| Worker | `PollCloseDispatcher` (30s, job_role `polls`): `ClosedAt IS NULL AND ClosesAt <= now()`; atraso ≤60s; audit `poll.close` com `actor=system` |
| Audit | `poll.create`, `poll.vote`, `poll.unvote`, `poll.close` |
| Slash | `/enquete` no catálogo só com `message.send` |

### Menções (B-082)

| Artefato | Contrato |
|----------|----------|
| Corpo | Tokens estáveis `<@userId>`, `<@here>`, `<@channel>` |
| Tabela | `messaging.message_mentions` (`TenantId`, `MessageId`, `ChannelId`, `MentionedUserId?`, `Kind`: `User`/`Here`/`Channel`) |
| Escrita | Mesma transação de `SendMessage` |
| Autocomplete | `GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/members?query=` — membership do canal; até 8 resultados. Sem `query`, o mesmo path devolve o roster paginado (B-186) |
| Unread (por canal) | `GET /api/v1/channels/{channelId}/unread-count` → `{ unreadCount, mentionCount }` |
| Unread (batch) | `GET /api/v1/workspaces/{workspaceId}/channels/unread` → `[{ channelId, unreadCount, mentionCount, lastReadSeq }]` |
| Read cursor | `PUT /api/v1/channels/{channelId}/read-cursor` → `{ lastReadSequence, allowRetrograde? }`; monotônico salvo; retrocesso só com `allowRetrograde: true` (marcar como não lida) |
| Hub | `ReadCursorChanged` — sincroniza badges multi-dispositivo do mesmo usuário |
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
| `GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/members` | Sem `query`: roster `{ items, nextCursor, total, canManage }` (`limit` 1–200, default 50, `cursor` opaco). Público/anúncio = membros do workspace (`canManage=false`). Privado = `channel_members` ativos; `canManage` se criador ou `channel.manage`. Com `?query=`: autocomplete até 8 (array) |
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/members` | Body `{ userId }`. Só `Private`. Criador ou `channel.manage`. Alvo no mesmo workspace. 409 `AlreadyChannelMember` / `LastChannelManager` não se aplica no add. 400 `ChannelMembershipNotPrivate`. Fora do workspace → 403. Audit `channel.member.add`. Mensagem `<system:member-add:userId>` + outbox `MessageCreated`. `JoinedSeq=0` |
| `DELETE /api/v1/workspaces/{workspaceId}/channels/{channelId}/members/{userId}` | Remove outro membro do privado. Não remove o último gestor (criador ainda membro ou `channel.manage`). 409 `LastChannelManager`. Audit `channel.member.remove`. `LeftAt` + evict SignalR |
| `DELETE /api/v1/workspaces/{workspaceId}/channels/{channelId}/members/me` | Sair do privado. Mesma regra do último gestor. Audit `channel.member.leave` |
| `POST /api/v1/workspaces/{workspaceId}/channels` | Body `{ name, type, spaceId? }`; exige `channel.create`; `spaceId` deve pertencer ao workspace |
| `PUT /api/v1/workspaces/{workspaceId}/channels/{channelId}/topic` | Body `{ topic }` (máx. 250; vazio limpa); membership + `channel.create`; rejeita `Direct` (B-087 `/topico`) |
| `GET /api/v1/workspaces/{workspaceId}/commands` | Descoberta de slash commands disponíveis ao ator (B-087); membership; filtrado por permissão; `description` segue o `locale` do caller (`me.locale`; null → `pt-BR`); `name` e `usage` são estáveis — ver tabela abaixo |
| `GET /api/v1/workspaces/{workspaceId}/members` | Membros do workspace (membership obrigatória — D-07); inclui `role` |
| `GET /api/v1/workspaces/{workspaceId}/contact-groups` | Departamentos do workspace + grupos pessoais do caller (B-166). Grupo de contatos **não** autoriza canal, DM ou papel |
| `POST /api/v1/workspaces/{workspaceId}/contact-groups` | Body `{ name, kind, order? }`. `kind`: `department` (só `workspace.admin`) ou `personal` (dono = caller). Nome ≤ 80, único por (`workspace`, `kind`, dono efetivo), sem diferenciar maiúsculas. `Idempotency-Key` opcional |
| `PATCH /api/v1/workspaces/{workspaceId}/contact-groups/{groupId}` | `{ name?, order? }`. Departamento: admin. Pessoal alheio: **404** |
| `DELETE /api/v1/workspaces/{workspaceId}/contact-groups/{groupId}` | Mesma authZ. **204**. Atribuições caem em cascade |
| `PUT /api/v1/workspaces/{workspaceId}/contact-groups/{groupId}/members` | Body `{ userIds: Guid[] }` substitui o conjunto. Só membros do mesmo workspace. `Idempotency-Key` opcional |
| `GET /api/v1/workspaces/{workspaceId}/contacts?grouped=true` | Seções `{ groupId?, name?, kind?, members[] }`: departamentos, grupos pessoais do caller, depois sem grupo (`groupId` nulo) se houver alguém fora de todos. `grouped=false` → **400** `InvalidQuery` |
| `GET /api/v1/workspaces/{workspaceId}/roles` | Papéis atribuíveis (`Member`, `Moderator`, `Auditor`, `Admin`); exige `workspace.admin` no workspace |
| `POST /api/v1/workspaces/{workspaceId}/members` | Convite/provisionamento (B-068). Body `{ email, displayName?, role? }` (`role` default `Member`); exige `workspace.admin`; cria perfil stub `pending:{email}` se o usuário ainda não logou; 409 se já membro; rejeita `Guest`/`Bot`/owners; audit `member.invite`; e-mail opcional via outbox se `Email:Enabled`. Sem self-signup — IdP (Keycloak) continua responsável pela autenticação |
| `PUT /api/v1/workspaces/{workspaceId}/members/{userId}/role` | Body `{ role }`; owner/admin (`workspace.admin`); não permite auto-elevação; rejeita `Guest`/`Bot`/`WorkspaceOwner`/`PlatformOwner` (D-07); audit `member.role.change`; e-mail opcional via outbox se `Email:Enabled` |
| `GET /api/v1/workspaces/{workspaceId}/presence` | Status `online`/`away`/`offline` dos membros (Redis TTL) |
| `POST /api/v1/workspaces/{workspaceId}/dms` | Body `{ userId }`; get-or-create DM 1:1 (`ChannelType.Direct`) |
| `POST /api/v1/workspaces/{workspaceId}/group-dms` | Body `{ userIds[], name? }`; get-or-create DM em grupo (`ChannelType.GroupDm`, 3–9); idempotente pelo conjunto ordenado (`ParticipantSetKey`). Flag `Directory:GroupDm:Enabled` (default false) → 404 se off |
| `POST /api/v1/channels/{channelId}/participants` | Body `{ userIds[] }`; só participante atual. Em `GroupDm` adiciona in-place (`JoinedSeq` = último seq). Em `Direct` cria **nova** GroupDm (a 1:1 permanece) |
| `DELETE /api/v1/channels/{channelId}/participants/me` | Sai da GroupDm; `LeftAt`/`LeftSeq`; 403 no history e no hub dali em diante |
| `PATCH /api/v1/channels/{channelId}` | Body `{ name? }`; renomeia Title da GroupDm (só participante) |

`ChannelResponse` inclui `spaceId?`, `topic?`, `hasGuests?` e, para DMs, `peerUserId` / `peerDisplayName`. GroupDm inclui `participantCount?`, `participantNames?`, `participantUserIds?`; `name` é o Title ou os nomes dos outros participantes. Channels `Private`/`Direct`/`Group`/`GroupDm` só aparecem na listagem para membros do canal. Guest (B-040) vê **somente** o canal do convite. Spaces agrupam channels na UI; DMs (1:1 e grupo) ficam fora de spaces. History de GroupDm filtra `seq > JoinedSeq`.

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/invites` | Body `{ email?, expiresInDays? }`; `workspace.admin`; devolve `{ id, url, expiresAt }` **uma vez**. Flag `Directory:Invites:Enabled` (default false) → 404 se off. Recusa DM/GroupDm. |
| `GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/invites` | Lista convites **sem token** + guests ativos; `workspace.admin` |
| `DELETE /api/v1/invites/{inviteId}` | Revoga; se já aceito, `LeftAt` no `ChannelMember` e evict do hub |
| `POST /api/v1/invites/{token}/accept` | Autenticado; cria `ChannelMember` sem `WorkspaceMember`; token gasto/expirado/revogado/ausente → **410** `InviteUnavailable` (indistinguível) |

Slash commands (B-087) — o cliente traduz o comando para as APIs existentes; a lista vem do servidor:

| Comando | Usage | Permissão / condição | Endpoint alvo |
|---------|-------|----------------------|---------------|
| `dm` | `/dm @pessoa` | membership | `POST …/dms` |
| `topico` | `/topico <texto>` | `channel.create` (+ canal não-DM) | `PUT …/channels/{id}/topic` |
| `convidar` | `/convidar <email>` | `workspace.admin` | `POST …/members` |
| `resumir` | `/resumir` | `ai.summarize` | `POST …/ai/summarize` |
| `apagar` | `/apagar` | `message.delete.own` | `DELETE …/messages/{id}` |
| `ajuda` | `/ajuda` | membership | UI local + esta lista |

`description` localiza por `UserLocales.Resolve(caller.locale)` (`pt-BR` default). `name` e `usage` não mudam.

Papéis reutilizam `Role` + `RolePermissionCatalog` + `IPermissionChecker`. Guest (B-040 / ADR-024) **não** entra em `workspace_members`: é `ChannelMember` no canal do convite, com `message.send` / `message.react` / `file.upload`. Demais superfícies de workspace respondem 403.

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

Metadados prontos / vírus scan ok (quando existir). No `complete`, anexos
`image/*` e `application/pdf` entram com `thumbnailStatus=Pending`; o worker
gera a miniatura no processamento deste outbox (nunca no hot path do envio).

### `AttachmentThumbnailReady` (SignalR)

Fan-out após geração (sucesso ou falha): `{ tenantId, channelId, attachmentId,
thumbnailStatus, width?, height?, pageCount?, thumbnailKey? }`.

### Link preview (B-091 / ADR-021)

Tabelas (RLS por `TenantId`):

| Tabela | Papel |
|--------|--------|
| `messaging.link_previews` | Cache por tenant+`UrlHash` — `Url`, `Title`, `Description`, `SiteName`, `ImageKey?`, `Status` (`Pending`\|`Ready`\|`Failed`\|`Blocked`), `FetchedAt`, `ExpiresAt` (TTL 7d) |
| `messaging.message_link_previews` | Junction mensagem ↔ preview; `RemovedAt` soft-remove |
| `messaging.link_preview_settings` | Toggle admin por tenant (`Enabled`) |

Flags: `LinkPreview:Enabled` (default `true`) e `LinkPreview:TimeoutMs` (default `8000`).

Job: no `OutboxProcessor` após realtime de `MessageCreated` — **nunca** no hot path de envio. Fetcher com guarda SSRF (scheme allowlist, IP privado/metadata após DNS e cada redirect, máx. 3 redirects, HTML ≤ 1,5 MB, imagem ≤ 512 KB).

| Endpoint | Notas |
|----------|--------|
| History/thread `MessageResponse.linkPreview?` | Só status `Ready` e junction não removida |
| `DELETE /api/v1/channels/{channelId}/messages/{messageId}/link-preview` | Autor ou `workspace.admin` |
| `GET /api/v1/channels/{channelId}/messages/{messageId}/link-preview/image` | Presign MinIO da miniatura própria |

### `LinkPreviewReady` (SignalR)

`{ tenantId, channelId, messageId, linkPreviewId, url, title?, description?, siteName?, hasImage, status }` — fan-out quando o cartão fica `Ready`.

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
| `MessageCreated` | Nova mensagem — payload inclui `messageId`, `clientMessageId` (mesmo UUID do `messageId` do comando), `channelId`, `conversationId`, `threadId?`, `parentMessageId?`, `replyToMessageId?`, `replyTo?`, `forwardedFromMessageId?`, `forwardedFromChannelId?`, `forwardedFrom?`, `sequence`, `authorId`, `body`, menções/anexos; em enquete, `poll` agregado |
| `PollChanged` | Voto, desvoto ou encerramento de enquete (B-096) — `messageId`, `channelId`, `poll` (mesmo shape do history) |
| `MessageEdited` | Edição |
| `MessageDeleted` | Soft delete |
| `ReactionChanged` | Toggle de reação (payload com resumo agregado) |
| `AttachmentThumbnailReady` | Miniatura de anexo pronta/falha (B-090) |
| `LinkPreviewReady` | Cartão Open Graph pronto (B-091) |
| `Typing` | Typing (TTL curto Redis); hub publica com `Clients.OthersInGroup` (B-071) — autor não recebe o próprio evento |
| `PresenceChanged` | Presence `online`/`away`/`offline` (hub group `t:{tenantId}`) |

Cliente deduplica ack HTTP + fan-out hub por `messageId` / `clientMessageId` (case-insensitive) — ver `protocolo-sync-realtime.md`.

Grupos SignalR: canal `t:{tenantId}:c:{channelId}` (mensagens/typing/reações); tenant `t:{tenantId}` (presence).

Hub (além de `JoinChannel` / `LeaveChannel` / `SendTyping`):

| Método | Notas |
|--------|-------|
| `JoinChannel(tenantId, channelId)` | Membership do canal |
| `SendTyping(tenantId, channelId, displayName)` | Membership + `message.send` (B-175); Auditor sem send → `HubException` |
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
    Task<Stream?> GetObjectAsync(string storageKey, CancellationToken ct);
    Task PutObjectAsync(string storageKey, Stream content, string contentType, CancellationToken ct);
}
```

`Attachment.ReferenceCount` (default 1): quantas linhas compartilham o `StorageKey` após encaminhar (B-085).

Campos de miniatura (B-090) em `files.attachments`: `ThumbnailKey`, `Width`,
`Height`, `ThumbnailStatus` (`Pending`\|`Ready`\|`Failed`|null), `PageCount`
(PDF). Miniatura WebP lado maior 640 px em
`tenants/{tenantId}/channels/{channelId}/{attachmentId}/thumb.webp`. Forward
clona os campos de thumbnail junto com `StorageKey`.

### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/attachments` | Body `{ fileName, contentType, sizeBytes, kind?, durationMs?, waveform?, width?, height? }` → URL pré-assinada PUT; exige membership + `file.upload`; `kind=Audio` exige `durationMs` e aceita `waveform` (0–100, ≤100 pts); `kind=Video` exige `durationMs`, aceita `width`/`height` |
| `POST /api/v1/channels/{channelId}/attachments/{id}/complete` | Confirma objeto no MinIO; status `Ready`; image/PDF → `ThumbnailStatus=Pending` + outbox `files.attachment.ready` |
| `GET /api/v1/channels/{channelId}/attachments/{id}/download` | URL pré-assinada GET do original; exige membership + `file.download` |
| `GET /api/v1/channels/{channelId}/attachments/{id}/thumbnail` | URL pré-assinada GET da miniatura WebP; mesma authZ do download; `404` se não `Ready`; cross-tenant → 403 |
| `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/messages/{messageId}/attachments/{attachmentId}/transcribe` | membership + `ai.transcribe`; `{ text, language, provider }` efêmero (não persiste); `503 AiDisabled` se IA off |

Regras: keys prefixadas por tenant (`tenants/{tenantId}/…`); MIME/tamanho via resolver efetivo (`Files:*` env como teto + `files.settings` por tenant quando ADR-020 habilitado); body da mensagem pode ser vazio se houver `AttachmentIds` prontos no `SendMessage`.

Limites (config `Files:*` / override tenant, default entre parênteses):

| Chave | Default | Validação |
|-------|---------|-----------|
| `MaxSizeBytes` | `10485760` (10 MiB) | Por arquivo no `initiate`; tenant ≤ teto de código |
| `MaxAttachmentsPerMessage` | `10` | Contagem de `attachmentIds` no `SendMessage`; excedente → `400` `{ error: "TooManyAttachments", max }` |
| `Files:Video:MaxSizeBytes` | `26214400` (25 MiB) | Por vídeo no `initiate`; teto de código |
| `Files:Video:MaxDurationMs` | `60000` | `durationMs` obrigatório para `kind=Video`; excedente → `400` |
| `DefaultAllowedVideoContentTypes` | `video/mp4`, `video/webm` | Allowlist separada (espelha áudio) |

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

Fase 1: implementação PostgreSQL FTS (ADR-011). Filtros (B-098) só restringem; a ACL de membership continua no servidor.

### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/search/messages?workspaceId=&q=&channelId=&authorId=&from=&to=&hasAttachment=&hasLink=&attachmentKind=&sort=&cursor=&limit=` | FTS em mensagens (`tsvector`/`GIN`, config `public.portuguese_unaccent`); exige membership no workspace + `search.messages` + `message.read`; filtra por ACL de canal (público via workspace, privado/DM via `channel_members`); nunca retorna mensagens soft-deleted nem fora da membership. `authorId`/`channelId` de outro tenant ou sem visibilidade → **403**. `q` pode ficar vazio quando há filtro estruturado. `q` aceita sintaxe websearch: frase entre aspas, `OR` e `-termo`, com prefixo `:*` nos termos soltos e acento irrelevante (B-188). `from`/`to` ISO-8601 (data só: início/fim do dia UTC). `attachmentKind`: `image`\|`audio`\|`document`. `sort`: `relevance` (default, recência + boost de frase exata) \| `date`. `limit` máx. 50. |

`SearchMessageHit`: `messageId`, `channelId`, `channelName`, `channelType`, `sequence`, `authorUserId`, `authorDisplayName`, `bodyPreview`, `createdAt`, `rank`, `kind` (`message`). Com termo, `bodyPreview` é `ts_headline` (marcas U+0001/U+0002 em volta do trecho).

`SearchMessagesResponse`: `total` (contagem do recorte de mensagens) e `cursor` (próxima página de mensagens; ausente na última). Na primeira página, com termo positivo, também `channels` (`kind=channel`), `people` (`kind=person`) e `attachments` (`kind=attachment`, só anexo `Ready` ligado a mensagem visível). Mesma ACL de membership. Filtros B-098 continuam só restringindo. `cursor` não pagina esses três grupos.

Sintaxe no campo (cliente): `de:@alice em:#geral antes:2026-07-01 depois:2026-06-01 tem:anexo|link|imagem|audio|documento`. Operadores viram query params; texto restante é `q` (aspas, `OR` e `-termo` ficam no termo).

Indexação: coluna `messaging.messages.search_vector` (trigger + reindex via outbox `MessageCreated`/`Edited`/`Deleted`) com `public.portuguese_unaccent`; índice GIN `ix_messages_search_vector`; índice composto `ix_messages_tenant_channel_created` (`TenantId`, `ConversationId`, `CreatedAt`) para recorte por data/canal. Nome de canal, display name e filename de anexo são consultados na hora, sem índice `pg_trgm`.

---

## Administration / Audit

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/admin/dashboard` | Métricas operacionais; exige `admin.dashboard` (Member → 403; B-175) |
| `GET /api/v1/admin/health-summary` | Status dos health checks (postgres/redis/minio); exige `admin.dashboard` (B-175); distinto de `GET /health` anônimo |
| `GET /api/v1/admin/version` | Versão da API; exige `admin.dashboard` (B-175) |
| `GET /api/v1/admin/audit-events?limit=&action=` | Lista eventos de `audit.audit_events` do tenant do actor; exige `admin.dashboard`; `limit` 1–200 (default 50); nunca retorna eventos de outro tenant |
| `GET /api/v1/admin/conversations?workspaceId=&limit=` | Lista canais/DMs do tenant para auditoria (B-067); exige `admin.dashboard`; **não** exige `channel_members`; `limit` 1–200 (default 100) |
| `GET /api/v1/admin/conversations/{channelId}/messages?after=&limit=` | Histórico admin do canal/DM (root); body **visível** mesmo com soft-delete; inclui `deletedBy` / anexos; exige `admin.dashboard`; canal fora do tenant → 403 |
| `GET /api/v1/admin/threads/{threadId}/messages?after=&limit=` | Histórico admin de replies da thread; mesma authZ e semântica de body |
| `GET /api/v1/admin/settings?workspaceId=` | Settings sensíveis mascarados (B-069 / ADR-020); exige `workspace.admin` (Auditor com só `admin.dashboard` → 403); `workspaceId` opcional (default: primeiro workspace do actor) |
| `PUT /api/v1/admin/settings` | Atualiza flags não-secretas (AI/email/webhooks/retention/linkPreview/files/rateLimit/`openRouterBaseUrl`/`*.processEnabled`); mesma authZ; rejeita secrets no body (`SecretsNotWritable`); audit `settings.change` |
| `POST /api/v1/admin/settings/credentials/openrouter/rotate` | Rotaciona API key OpenRouter (envelope AES-GCM em `ai.settings`); body `{ workspaceId?, value }`; resposta `{ configured, mask, keyVersion, rotatedAt }`; `503` se keyring indisponível |
| `POST /api/v1/admin/settings/credentials/smtp/rotate` | Rotaciona senha SMTP do tenant (envelope em `notifications.email_settings`); mesma forma de resposta |
| `POST /api/v1/admin/settings/credentials/webhook/rotate` | Rotaciona o signing secret quando o tenant tem 0 ou 1 endpoint (0 cria `default`). Mais de um → `409 WebhookEndpointAmbiguous` |
| `POST /api/v1/admin/webhooks` | Cria endpoint (`name`, `url`, `enabled`, `subscribedEvents`, `channelFilter`, `secret` opcional). `workspace.admin`. Limite 5 → `409 WebhookEndpointLimit`. Canal fora do tenant → `400 InvalidChannelFilter`. Resposta sem secret |
| `PUT /api/v1/admin/webhooks/{endpointId}` | Atualiza nome, URL, enabled, eventos e filtro. Secret no body → `400 SecretsNotWritable` |
| `DELETE /api/v1/admin/webhooks/{endpointId}` | Remove o endpoint do tenant |
| `POST /api/v1/admin/webhooks/{endpointId}/rotate` | Rotaciona o HMAC daquele endpoint; resposta só máscara/versão |
| `POST /api/v1/admin/webhooks/{endpointId}/test` | Envia `WebhookTest` assinado e devolve `{ ok, statusCode, lastError }` |
| `POST /api/v1/admin/settings/credentials/vapid/rotate` | Rotaciona VAPID da instância (envelope em `administration.process_settings`); body `{ workspaceId?, publicKey, privateKey, subject? }`; resposta máscara/versão; `503` se keyring indisponível (B-187) |
| `POST /api/v1/admin/settings/encryption/reencrypt` | Regrava envelopes do workspace/tenant/instância para `ActiveKeyVersion`; migra plaintext legado de webhook; audit `settings.encryption.reencrypt` |
| `GET /api/v1/admin/workspaces/{workspaceId}/export` | Export compliance do workspace (B-046); ZIP `application/zip` com JSON (`manifest`, `workspace`, `members`, `contact-groups`, `spaces`, `channels`, `threads`, `messages`, `attachments` metadata); corpos soft-deleted incluídos (paridade B-067); **sem** binários MinIO; exige `workspace.admin` (Auditor → 403); audit `workspace.export`; workspace fora do tenant/membership → 403 |

`AuditEventResponse`: `id`, `action`, `entityType`, `entityId`, `actorUserId`, `occurredAt`, `metadataJson`.

`AdminConversationResponse`: `id`, `workspaceId`, `name`, `type`, `spaceId`, `peerUserId`, `peerDisplayName`.

`AdminConversationMessageResponse`: `id`, `channelId`, `conversationId`, `sequence`, `authorId`, `authorName`, `body` (sempre o valor persistido), `createdAt`, `editedAt`, `deletedAt`, `deletedBy`, `deletedByName`, `threadId`, `replyToMessageId`, `replyCount`, `attachments`, `poll?` (B-096; anônima sem votantes).

Ações mínimas: `admin.login`, `channel.create`, `space.create`, `message.send`, `message.delete`, `attachment.upload`, `member.role.change`, `member.invite`, `contact_group.create`, `contact_group.delete`, `contact_group.members.replace` (só departamento), `settings.change`, `settings.credential.rotate`, `settings.encryption.reencrypt`, `workspace.export`, `message.purge`, `poll.create`, `poll.vote`, `poll.unvote`, `poll.close`.

**Planned (B-169) — metadata de `message.delete`:** quando `contentAuditEnabled=true`,
`metadataJson` inclui `channelId`, `threadId?`, `sequence`, `authorId`, `body`
(conteúdo no soft-delete). Com `false`, mesmos ids/`sequence` sem `body`.
Hoje o runtime grava só `channelId` / `threadId` / `sequence` (sem body) —
comportamento acima é contrato alvo da flag, não o estado atual do código.
Outros eventos de audit (ex. `settings.content_audit.change`) continuam sem
body de mensagem.

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
| `ai.processEnabled` / `processSource` | Flag de processo (`Ai:Enabled`) — B-187: gravável; SoT DB quando overrides on |
| `ai.workspaceEnabled` / `provider` | `ai.settings` do workspace — gravável via PUT |
| `ai.openRouterBaseUrl` | BaseUrl OpenRouter da instância (B-187); https + IP público |
| `ai.apiKeyConfigured` / `apiKeyMask` / `apiKeyKeyVersion` / `apiKeyRotatedAt` / `apiKeySource` | Máscara `••••last4`; **nunca** valor em claro; rotação via endpoint dedicado |
| `email.*` | Enabled/host/port/user/from/startTls (override tenant); senha só máscara/versão/fonte |
| `email.processEnabled` / `processSource` | Kill switch de processo (`Email:Enabled`) — B-187: gravável; SoT DB quando overrides on |
| `webhooks.status` | `unconfigured` \| `disabled` \| `active`. Com exatamente 1 endpoint, reflete essa linha (B-048). Com várias, `active` se alguma entrega |
| `webhooks.enabled` / `url` / `urlConfigured` | Resumo do único endpoint. Com várias, `url` vem vazio — usar `endpoints` |
| `webhooks.secretConfigured` / `secretMask` / `secretKeyVersion` / `secretRotatedAt` / `secretSource` | HMAC mascarado. Com várias, máscara do resumo fica nula (`secretSource = multiple`) |
| `webhooks.endpoints[]` | Lista sem secret: `id`, `name`, `enabled`, `url`, `subscribedEvents`, `channelFilter`, `secretMask`, `lastStatusCode`, `lastError`, `lastDeliveryAt` |
| `webhooks.maxEndpoints` | Limite (5) |
| `webhooks.message` | Texto de status para UI admin |
| `retention.processEnabled` / `processSource` | Kill switch de processo (`MessageRetention:Enabled`) — B-187: gravável; SoT DB quando overrides on |
| `retention.enabled` / `retentionDays` | Política do tenant em `messaging.message_retention_settings` — gravável |
| `retention.defaultRetentionDays` / `batchSize` / `intervalMinutes` | Knobs do job (instância, B-187) |
| `retention.message` | Texto de status para UI admin |
| `files.*` | Limites por tenant (`files.settings`); teto = código/`AttachmentPolicies`; gravável |
| `rateLimit.sendPerMinute` / `hubPerMinute` | Limites por tenant; efetivo = `min(DB, teto de código)`; gravável |
| `linkPreview.processEnabled` / `processSource` / `timeoutMs` | Processo + timeout (B-187); toggle tenant já gravável |
| `push.processEnabled` / `processSource` | Kill switch de processo (`process_settings.PushEnabled`) — B-187 |
| `push.vapidPublicKey` / `vapidConfigured` / `vapidMask` / `vapidSource` | Pública visível; privada só máscara; rotate dedicado |
| `encryption.activeKeyVersion` / `credentialsUsingActiveKey` / `databaseOverridesEnabled` | Metadata do keyring (sem nomes de variáveis com valor) |

Regras:

- Credenciais externas (OpenRouter, SMTP password, webhook HMAC) em envelope AES-GCM no DB quando `RuntimeSettings:DatabaseOverridesEnabled` (ADR-020); chave mestra só no env
- Fallback env para AI/SMTP enquanto não houver envelope; envelope inválido falha fechado
- PUT geral **nunca** aceita secrets (`SecretsNotWritable`); usar `POST .../credentials/*/rotate`
- Membro comum e Auditor (sem `workspace.admin`) → `403` em GET/PUT/rotate/reencrypt
- Tenant do actor; nunca aceitar `tenantId` do body
- Retenção (B-047): `retentionDays` entre 1 e 3650; purge hard-delete só com **processo** `MessageRetention:Enabled=true` **e** `retention.enabled=true` no tenant; job no worker; audit `message.purge`. B-187: o gate de processo pode vir do DB (`administration.process_settings`) quando overrides on
- Kill switches de processo (B-187): `Ai` / `Email` / `MessageRetention` / `Push` / `LinkPreview` na singleton de instância; PUT geral; default seguro; flag off → default de código
- Files/RateLimit: admin pode restringir, não ultrapassar teto de **código** (B-187; env deixa de ser teto de produto)

### Retenção / purge (B-047)

- Soft-delete permanece o default de exclusão (ADR-018); APIs de leitura redigem body
- Hard-delete: worker `MessageRetentionPurgeDispatcher` remove mensagens com `DeletedAt` anterior ao cutoff (`now - retentionDays`)
- Cascata mínima: remove `reactions` da mensagem; anexos com `StorageKey` exclusivo fazem `attachments.MessageId = null` (metadados preservados) e apagam no MinIO o `StorageKey` **e** o `ThumbnailKey` quando presentes (B-090); anexos compartilhados (B-085) removem só a linha da mensagem purgada e ajustam `ReferenceCount` nos irmãos — sem delete MinIO enquanto houver referência
- `ConversationSequence` / `seq` não são reescritos
- Off por default (processo + tenant)

### Webhooks outbound (B-048 / B-108 / ADR-026)

- Tabela `integrations.webhook_endpoints` (até 5 por tenant, PK `Id`): `Name`, `Enabled`, `Url`, `SubscribedEvents` (`text[]`), `ChannelFilter` (`uuid[]`), `LastDeliveryAt`, `LastStatusCode`, `LastError`, envelope AES-GCM do signing secret (+ coluna `Secret` legado em dual-read)
- Eventos assináveis: `MessageCreated` (default), `MessageEdited`, `MessageDeleted`, `ReactionChanged`. `WebhookTest` só no ping
- Delivery best-effort no `OutboxProcessor` **após** realtime. Filtro vazio = todos os canais. Falha HTTP grava last status e **não** relança (o outbox da mensagem segue)
- `POST` do payload JSON do outbox (ping: JSON sintético, sem mensagem); headers:
  - `X-VibeChat-Event: <tipo>`
  - `X-VibeChat-Delivery-Id: <outboxId ou id do ping>`
  - `X-VibeChat-Signature: sha256=<hmac-hex>` (HMAC-SHA256 do body com o secret)
- URL: `https` (ou `http://localhost` / `127.0.0.1` em lab); timeout ~5s; sem redirect
- RLS + query filter por `TenantId`. AAD do envelope continua o id do tenant (compatível com B-048)

### Bots de integração (B-109 / ADR-027)

Flag `Integrations:Bots:Enabled` default **false** (Development/lab pode ligar). Off → 404 `IntegrationDisabled` em admin e em send.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/workspaces/{workspaceId}/bots` | `workspace.admin` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bots` | `workspace.admin`; body `{ name, channelIds[], allowDms }`; resposta inclui `token` **uma vez** |
| PUT | `/api/v1/admin/workspaces/{workspaceId}/bots/{botId}` | `workspace.admin`; body `{ name, channelIds[], allowDms, enabled }`; sem secret |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bots/{botId}/rotate` | `workspace.admin`; novo `token` uma vez; o anterior deixa de valer |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bots/{botId}/revoke` | `workspace.admin`; 401 imediato no token atual |
| POST | `/api/v1/integrations/v1/channels/{channelId}/messages` | `Authorization: Bearer vc_int_…` ou `X-VibeChat-Integration-Token` |
| POST | `/api/v1/integrations/v1/dms` | mesmo token; exige `allowDms` |

Send body: `{ body, idempotencyKey, threadId? }`. DM acrescenta `userId`. Resposta `202` `{ messageId, channelId, sequence, createdAt, idempotent }`. A chave é prefixada com o id do bot no store de idempotência. O pipeline é o `SendMessage` de B-004 (`seq` + outbox). O payload `MessageCreated` inclui `authorIsBot`.

Escopo de canal é lista explícita. Fora da lista, DM/GroupDm ou outro tenant → 403. Token desconhecido ou revogado → 401. Bot `enabled=false` → 403 `BotDisabled`. GET admin devolve `tokenLast4` / `tokenConfigured`, nunca o segredo. Rate-limit próprio `t:{tenantId}:rl:integration:{botId}`.

Tabelas `integrations.bots`, `integrations.bot_tokens` (só hash SHA-256), `integrations.bot_channel_scopes`. RLS FORCE. Lookup do token antes do tenant usa `app.integration_token_hash` (SET LOCAL), no mesmo espírito do convite B-040. Perfil `Role.Bot` no workspace; subject `bot:{id}` não autentica como humano.

### Plugins locais (B-110 / ADR-028)

Mesma flag `Integrations:Bots:Enabled`. Off → 404 `IntegrationDisabled`. O plugin é configuração: manifesto JSON + bot 1:1. Não executa código de terceiro.

Manifesto v1 estrito (`vibechat.plugin.manifest.v1`): `id`, `name`, `version` (`N.N.N`), `capabilities`. Campo extra → 400 `InvalidPluginManifest`. Capability diferente de `messages.send` → 400 `UnknownPluginCapability`. Corpo acima de 4 KiB → 400 `PluginManifestTooLarge`. Built-in do binário: `incoming-messages`.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/workspaces/{workspaceId}/plugins` | `workspace.admin` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugins` | `workspace.admin`; body com exatamente um de `builtinId` ou `manifest`, mais `channelIds[]` e `allowDms`; resposta inclui `token` **uma vez** |
| PATCH | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}` | `workspace.admin`; body `{ enabled }`; desligar responde 403 nos sends novos e preserva histórico |
| DELETE | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}` | `workspace.admin`; revoga o token na mesma transação; 204 |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}/rotate` | `workspace.admin`; novo `token` uma vez |

Slug duplicado ou acima de 20 plugins no workspace → 409 (`PluginAlreadyInstalled` / `PluginLimitReached`). Id de outro workspace → 404 `PluginNotFound`. Member → 403. Tabela `integrations.plugins` com RLS FORCE e FK para `integrations.bots`. Uninstall não apaga mensagens.

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

### Web Push (B-095 / ADR-022 / D-13)

```csharp
public interface IPushSender
{
    string Name { get; }
    bool IsEnabled { get; }
    Task<PushSendResult> SendAsync(PushDeliveryRequest request, CancellationToken ct);
}
```

- Implementações: `NullPushSender` (default), `WebPushSender` (`Lib.Net.Http.WebPush` + VAPID) quando `Push:Enabled=true` e chaves válidas
- Off by default; chaves no DB quando overrides on (B-187); GET devolve `{ enabled, publicKey? }` sem erro se off
- Tabela `notifications.push_subscriptions` (RLS): `TenantId`, `UserId`, `Endpoint` (único por usuário), `P256dh`, `Auth`, `UserAgent?`, `CreatedAt`, `LastSeenAt`, `FailedAt`
- Delivery best-effort no `OutboxProcessor` **após** realtime, apenas `MessageCreated`; 410/404 remove a linha; falha não reprocessa outbox
- Destinatários: membership **atual** ∩ nível efetivo (ver B-097) ∩ `preferences.PushEnabled` ≠ false ∩ não-autor; `read_cursors` suprime se já lido

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `GET /api/v1/notifications/push/public-key` | `message.read` | `{ enabled, publicKey? }` |
| `GET /api/v1/notifications/push/subscriptions` | `message.read` | Só o actor |
| `POST /api/v1/notifications/push/subscriptions` | `message.read` | Upsert por endpoint do actor |
| `DELETE /api/v1/notifications/push/subscriptions/{id}` | `message.read` | 404 se não for do actor |

### Preferências de notificação e DND (B-097)

`notifications.preferences` (estende a linha por `(TenantId, UserId)` de B-095):
`Level` (`All`\|`MentionsAndDms`\|`None`, default `MentionsAndDms`), `HidePreview`,
`DndEnabled`, `DndStart`/`DndEnd` (`time`), `DndDays` (bitmask domingo=1…sábado=64;
`0` = todos os dias), `TimeZone` (IANA), `DigestEnabled`, `PriorityContactUserIds`
(`uuid[]`).

`notifications.channel_preferences` (RLS, único por `ChannelId`+`UserId`):
`Level?`, `MutedUntil?`, `FollowAllThreads` (B-102). `Level` é nullable desde B-102:
`null` = "sem override de mute" — a linha pode existir só para carregar
`FollowAllThreads=true`. Linha ausente = "usar o padrão" em ambos os eixos.
`MutedUntil` expirado é ignorado na leitura (dispatcher e API) — sem job de limpeza,
o silêncio "volta sozinho". Limpar o mute (`DELETE .../channels/{channelId}`) não
apaga a linha quando `FollowAllThreads=true` — só zera `Level`/`MutedUntil`.

Nível efetivo por destinatário = override de canal não expirado, senão `Level`
global. `None` nunca notifica; DM notifica em `All`/`MentionsAndDms`; canal comum só
com `All` ou menção. DND (`PushDispatchPolicies.IsWithinDnd`) é recalculado a cada
envio contra o fuso IANA armazenado — nunca offset fixo — e suprime o push a menos
que o autor da DM esteja em `PriorityContactUserIds`. `HidePreview` troca o corpo da
notificação por vazio por destinatário (o payload passa a variar por assinatura, não
mais um único payload por mensagem).

`DigestEnabled` é persistido e exposto na UI, mas o envio do resumo por e-mail do
que foi perdido durante o DND **não** está implementado nesta entrega — ver nota em
[B-097](../product/specs/B-097-preferencias-notificacao-dnd.md).

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `GET /api/v1/notifications/preferences` | `message.read` | Preferência global do actor + `channelOverrides[]` |
| `PUT /api/v1/notifications/preferences` | `message.read` | Upsert; valida `TimeZone` quando `DndEnabled=true`; `PriorityContactUserIds` filtrado a membros do tenant |
| `PUT /api/v1/notifications/preferences/channels/{channelId}` | `message.read` | `{ level, duration? }`; canal de outro tenant → 403 |
| `DELETE /api/v1/notifications/preferences/channels/{channelId}` | `message.read` | Remove override (volta ao padrão); canal de outro tenant → 403 |

Estritamente por `(tenant, user)` — ninguém, nem admin, lê ou escreve a preferência
de outro usuário (sem trilha de auditoria para preferência pessoal, por desenho).

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

Fase 1: Redis fixed-window (`INCR` + `EXPIRE`). Keys: `t:{tenantId}:rl:send:{userId}`, `t:{tenantId}:rl:hub:{userId}`, `t:{tenantId}:rl:integration:{botId}` (B-109, mesmo teto de send, balde separado). Aplicado em `POST .../messages` (429) e hub `JoinChannel`/`SendTyping`/`Heartbeat`/`SetAway` (`HubException`). Config efetiva (ADR-020): com row DB → `min(tenant DB, teto de código)`; sem row → `RateLimit:*` appsettings clamp ao teto de código; flag `DatabaseOverridesEnabled` off → defaults de código. Sem Redis configurado: fail-open.

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
CTA explícito do usuário. `GET /api/v1/admin/version` exige `admin.dashboard` e
descreve a API — não é dependência do caminho de update do PWA.

Nginx de referência (`apps/web/nginx.conf`): `index.html`, `ngsw.json` e
`version.json` usam `Cache-Control: no-cache, no-store, must-revalidate`;
bundles com hash de conteúdo podem ser imutáveis.

## Anti-padrões

- Passar entidades EF entre módulos
- Compartilhar `DbContext` “god” sem fronteiras
- Publicar eventos SignalR diretamente de um módulo de domínio sem passar por `IRealtimePublisher` / outbox
- Ler `HttpContext` dentro de módulos de domínio (usar `ITenantContext`)
