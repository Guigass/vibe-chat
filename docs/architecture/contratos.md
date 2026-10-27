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
| `GET /api/v1/me` | `{ userId, subject, email, displayName, roles, locale, chatWallpaperId, accentColorId }` — `locale` é um de `pt-BR` \| `en` \| `es` \| `fr` \| `de` \| `it` \| `ja` \| `zh-CN` \| `ko` \| `ru` ou `null` se ainda não persistido. `chatWallpaperId` e `accentColorId` são ids do catálogo ou `null` (padrão do produto); id desconhecido no banco volta como `null` |
| `PUT /api/v1/me` | Body `{ locale }` — só um locale suportado; outro valor → **400** `InvalidLocale`. Atualiza só o caller. Não altera wallpaper/accent |
| `PUT /api/v1/users/{userId}/appearance` | Body `{ chatWallpaperId, accentColorId }`. Só o caller (`userId` diferente → **403**, inclusive admin). Sem tenant do caller → **403**. `null` ou vazio restaura o padrão. Id fora do catálogo → **400** `InvalidWallpaper` ou `InvalidAccent` e nada é gravado. Linha em `identity.visual_preferences` (RLS por tenant) |

### Ficha pública do membro (B-167)

`DisplayName` continua em `identity.user_profiles` (identidade global, sem `tenant_id` — o login acontece antes do tenant). Cargo, sobre, destaque e avatar ficam em `identity.member_profiles`, uma linha por `(TenantId, UserId)`, com `FORCE ROW LEVEL SECURITY`. Não é status temporário (B-116) nem preferência visual (B-185).

| Método | Contrato |
|--------|----------|
| `PUT /api/v1/me/profile` | Body `{ displayName, jobTitle?, about?, highlightMessage? }`. Só o caller com membership de workspace; sem membership → **403**. `displayName` obrigatório (1–80). Opcionais vazios limpam o campo. Limites: cargo 80, sobre 500, destaque 160. Controle ou excesso → **400** (`DisplayNameRequired`, `DisplayNameTooLong`, `DisplayNameInvalid`, `JobTitleTooLong`, `AboutTooLong`, `HighlightTooLong`, `ProfileTextInvalid`). `Idempotency-Key` opcional; mesma chave e outro corpo → **409** `IdempotencyConflict`. Audit `profile.update` sem o texto |
| `POST /api/v1/me/profile/avatar` | `multipart` campo `file`. PNG, JPEG, WEBP ou GIF, até 2 MiB, magic bytes conferem o `Content-Type`. Senão **400** (`AvatarEmpty`, `AvatarTooLarge`, `AvatarTypeNotAllowed`). Key MinIO `{tenant}/profiles/{user}/…`. Audit `profile.avatar` com `stage=set` |
| `DELETE /api/v1/me/profile/avatar` | Remove a foto do caller e volta às iniciais. Audit `stage=clear` |
| `GET /api/v1/workspaces/{workspaceId}/members/{userId}/profile` | Membership no workspace. Alvo fora do workspace → **404**. Outro workspace, guest ou sem membership → **403**. Corpo `{ userId, displayName, email, jobTitle, about, highlightMessage, avatarUrl? }`. E-mail já visível na lista de membros |
| `GET /api/v1/workspaces/{workspaceId}/members/{userId}/profile/avatar` | Mesma authZ. Bytes da imagem ou **404**. A lista `GET .../members` inclui `avatarUrl` quando há foto |

Sem hub dedicado: o cliente relê membros. Admin não edita perfil alheio nesta fatia.

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
| Attachments | AttachmentDto[] | Metadados prontos (sem URL): `id`, `fileName`, `contentType`, `sizeBytes`, `status`, `kind`, `durationMs?`, `waveform?`, `thumbnailStatus?` (`Pending`\|`Ready`\|`Failed`), `width?`, `height?`, `pageCount?` (B-090), `scanStatus?` (`NotRequired`\|`PendingScan`\|`Clean`\|`Quarantined`\|`ScanFailed`), `scanReason?` (B-131) |
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

`GET /api/v1/channels/{channelId}/messaging-policy` devolve só esses campos não secretos a quem tem `message.read` no canal (inclui guest), mais `historyEnabled` e `leaveTombstone` (B-114). Com `Messaging:History:Enabled=false`, `historyEnabled` sai `false`. Alterar a política continua em `PUT /api/v1/admin/settings` (`workspace.admin`). `windowMinutes` nulo = sem limite; `0` ou acima de 525600 → `InvalidMessagingPolicy` (400).

### Histórico e movimentação (B-114 / ADR-029)

Flag `Messaging:History:Enabled` default **false**. Off → edição não grava versão e os endpoints abaixo respondem **404** `HistoryDisabled`.

| Artefato | Contrato |
|----------|----------|
| Tabela | `messaging.message_versions` (`TenantId`, `MessageId`, `VersionNumber`, `Body`, `ActorUserId`, `CreatedAt`); unique `(TenantId, MessageId, VersionNumber)`; RLS FORCE |
| Move | `messaging.message_moves` (`TenantId`, `IdempotencyKey`, origem, destino, `DestinationSequence`, `Scope`, `LeaveTombstone`); unique `(TenantId, IdempotencyKey)` e `(TenantId, SourceMessageId)` |
| Colunas | `MovedFromMessageId/ChannelId` no destino; `MovedToMessageId/ChannelId` na origem |
| `GET …/messages/{messageId}/history` | `message.read` + `message.history.read`. `{ versions: [{ version, body, actorUserId, createdAt }], currentBody, editedAt }`. Política oculta → **403** `HistoryHidden` (admin `workspace.admin` lê e audita `message.history.read`) |
| `GET …/messages/{messageId}/move` | `message.read`. `{ destination, origin }` com `{ accessible }` e, só se membro, `channelId`, `messageId`, `sequence`. Sem body |
| `POST …/messages/{messageId}/move` | `message.move` + membership nos dois canais do mesmo workspace. Body `{ idempotencyKey, targetChannelId, scope?: "message"\|"thread", leaveTombstone? }`. Origem vira `<system:moved>` (ou soft-delete se `leaveTombstone=false`). Destino recebe mensagem nova e `seq` novo. Replay da mesma chave devolve o mesmo destino |
| Erros | `SameChannel` 400, `InvalidMoveScope` 400, `NoThread` 400, `MessageHasThread` 409, `AlreadyMoved` 409, `MessageNotMovable` 409, `IdempotencyConflict` 409. Canal de outro tenant → **403** e nenhuma linha |
| Hub | `MessageMoved` no destino (sem push/webhook). Origem: `MessageEdited` do tombstone ou `MessageDeleted` |
| Export | `message-versions.json` no ZIP de workspace. Purge de retenção apaga versões e moves com a mensagem |

### Soft-delete Message

- `DELETE /api/v1/channels/{channelId}/messages/{messageId}`
- Autor com `message.delete.own` dentro de `messaging.delete.*`, **ou** papel com `message.delete.any` quando `messaging.delete.allowModeratorOverride` (default **ligado**)
- Códigos estáveis: `DeleteDisabled` (403), `DeleteRoleDenied` (403), `DeleteWindowExpired` (422); não autor sem override continua 403 vazio
- Soft-delete (`DeletedAt`); body oculto nas leituras (ADR-018)
- Também cobre replies de thread do canal (authZ por membership do canal pai)
- Com `contentAuditEnabled=true` (default, B-169 / ADR-043), o evento `message.delete`
  em `audit.audit_events` inclui `channelId`, `threadId?`, `sequence`, `authorId`,
  `deletedAt` e `body` no `metadataJson`. O snapshot sobrevive ao purge da linha.
  Com `false`, os mesmos ids e timestamps, sem `body`. Políticas de quem/quando
  pode apagar: B-107 (`messaging.delete.*`).

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

### Anúncios e canais somente leitura (B-112)

Modo de canal `Announcement` (`ChannelType`). Não converte canal existente. Confirmação é leitura explícita, **não** assinatura legal.

| Item | Regra |
|------|--------|
| Criar canal | `POST /api/v1/workspaces/{workspaceId}/channels` com `type: Announcement` exige `channel.create` e `announcement.publish`. Senão **403** `AnnouncementPublishForbidden`. Audit `announcement.created` |
| Publicar | `POST /api/v1/channels/{channelId}/messages` no canal Announcement exige `message.send` e `announcement.publish`. Membro → **403**. Body opcional `requiresAcknowledgement`, `acknowledgeBy` (UTC, futuro). Fora do modo → **400** `InvalidAnnouncementChannel`. Prazo sem flag → **400** `AcknowledgementDeadlineRequiresFlag`. Prazo no passado → **400** `InvalidAcknowledgeBy`. A mensagem preserva `Message` e `seq` |
| Tabelas | `messaging.announcements` (`MessageId` PK, `TenantId`, `ChannelId`, `CreatedByUserId`, `RequiresAcknowledgement`, `AcknowledgeBy?`, `ClosedAt?`, `CreatedAt`); `messaging.announcement_acknowledgements` (`Id`, `TenantId`, `MessageId`, `ChannelId`, `UserId`, `AcknowledgedAt`); unique `(TenantId, MessageId, UserId)`; FORCE RLS |
| Confirmar | `POST /api/v1/channels/{channelId}/messages/{messageId}/acknowledgements` — `announcement.acknowledge`; idempotente (repetir não aumenta `acknowledgementCount`); **409** `AnnouncementClosed` ou `AnnouncementAcknowledgementNotRequired`; outro canal → **404** |
| Relatório | `GET .../acknowledgements?limit=&cursor=` — `announcement.publish` ou `workspace.admin`. `{ count, items[{ userId, displayName, acknowledgedAt }], nextCursor }`. Página máx. 50. Não inclui outro tenant nem outro canal |
| Encerrar | `POST .../acknowledgements/close` — `announcement.publish`; idempotente. Audit `announcement.closed` |
| Inbox | `GET /api/v1/workspaces/{workspaceId}/announcements/pending` — `message.read`. Até 50 itens ainda abertos que o caller não confirmou. `channels/unread` inclui `pendingAnnouncementCount` (não é read cursor) |
| Edição | `PUT` da mensagem segue B-107. Se houver anúncio, audit `announcement.edited` |
| Eventos | Outbox `announcement.published` e `announcement.acknowledged` (`tenantId`, `channelId`, `messageId`). Encerramento reenvia `announcement.acknowledged` com `closedAt` para o cliente derrubar a pendência |
| Permissões | `announcement.publish`: Admin/Owner e Moderator. `announcement.acknowledge`: Member, Guest, Auditor, Moderator, Admin/Owner. Bot não publica nem confirma |

`AnnouncementDto` no history: `requiresAcknowledgement`, `acknowledgeBy`, `closedAt`, `acknowledgedByMe`, `acknowledgementCount`, `canAcknowledge`, `canViewReport`. A contagem é agregada; nomes só no relatório.

### Decisões e action items (B-119)

Entidade curada em `messaging.commitments`. O body da mensagem **não** é copiado: a proveniência guarda `sourceMessageId`, `sourceVersion` (seq), hash SHA-256 de um excerpt de até 140 caracteres, método `convert`, ator e snapshot de authZ. Comentários continuam na conversa/thread de origem. Histórico de mudança fica em `messaging.commitment_changes` (ação, ator, instante) e no audit `commitment.*`.

| Item | Regra |
|------|--------|
| Tabela | `messaging.commitments` (`TenantId`, `WorkspaceId`, `ChannelId`, `Kind` `decision\|action_item`, `Title` 1–120, `Description?` ≤500, `OwnerUserId?`, `DueAt?`, `State` `open\|closed`, `SourceMessageId`, `SourceThreadId?`, `SourceVersion`, `ExcerptHash`, `Visibility` `channel\|restricted`, `IdempotencyKey`, `SourceRedacted`); unique `(TenantId, CreatedBy, IdempotencyKey)`; FORCE RLS |
| Histórico | `messaging.commitment_changes`; sem body |
| `POST /api/v1/workspaces/{workspaceId}/commitments` | Body `{ idempotencyKey, messageId, kind, title, description?, ownerUserId?, dueAt?, visibility? }`; `message.read` da origem; assignee precisa ler o canal agora senão **400** `AssigneeNotMember`; replay da mesma chave devolve o item original; mensagem de sistema → **400** `NotConvertible` |
| `GET …/commitments` | Query `channelId`, `ownerId`, `kind`, `state` (`open` default, `closed`, `all`), `cursor`, `limit` (máx. 50). Sem leitura atual do canal, ou visibilidade `restricted` para quem não é criador/owner → o item some (**404** no GET por id) |
| `PATCH` / `POST …/close` / `POST …/reopen` | Criador, owner atual, ou `workspace.admin` só quando `visibility=channel`. Restrito não abre para admin. Close/reopen gravam audit |
| `GET …/history` | `{ items: [{ action, actorUserId, actorName, at }] }` |
| Eventos | Outbox `decision.created\|updated\|closed\|reopened` e `action_item.*`. Payload sem título nem body. `restricted` vai ao grupo do criador/owner; `channel` ao grupo do canal |
| Inbox | Kind `commitment` (prioridade 7, reason `assigned`) para o owner de item `open`, se o nível efetivo notifica menção e o DND não está ativo. `HidePreview` omite o título. Perder o canal remove o item |
| Delete/purge | Soft-delete e purge da origem setam `SourceRedacted`. A resposta não devolve body |

### Tarefas derivadas de mensagens (B-123)

Entidade curada em `messaging.tasks`. O body da mensagem **não** é copiado. Checklist ordenado em `messaging.task_checklist_items`. Responsáveis em `messaging.task_assignees`. Comentários continuam na thread de origem.

| Item | Regra |
|------|--------|
| Tabelas | `messaging.tasks`, `messaging.task_assignees`, `messaging.task_checklist_items`. `TenantId` + FORCE RLS. Unique `(TenantId, CreatedBy, IdempotencyKey)` e `(TenantId, TaskId, UserId)`. `Version` é concurrency token. O worker lê tarefas vencidas com `app.job_role=tasks` |
| `POST /api/v1/workspaces/{workspaceId}/tasks` | `{ idempotencyKey, messageId, title, description?, priority?, assigneeUserIds?, dueAt?, visibility?, checklist? }`. `message.read` da origem. Assignee sem leitura do canal → **400** `AssigneeNotMember`. Replay da chave devolve a tarefa original. Mensagem de sistema → **400** `NotConvertible`. Checklist máx. 20, texto 1–200, ordem do array |
| `GET …/tasks` | Query `view` (`workspace` default, `channel`, `mine`), `channelId` obrigatório em `channel`, `state` (`active` default, `open`, `in_progress`, `completed`, `all`), `cursor`, `limit` (máx. 50). Sem leitura atual do canal, ou `restricted` para quem não é criador/assignee → o item some (**404** no GET por id). `mine` = assignee |
| `PATCH` | `{ version, … }`. Versão diferente → **409** `TaskVersionConflict` com `currentVersion` e sem gravar. Criador, assignee, ou `workspace.admin` só quando `visibility=channel` |
| `POST …/complete` / `POST …/reopen` | `{ version }`. Repetir o estado atual não emite outro evento |
| Estados | `open`, `in_progress`, `completed`. Prioridade `low\|normal\|high\|urgent`. Atraso é derivado (`dueAt` no passado e estado ≠ `completed`); a UI mostra o texto, não só cor |
| Membership | Quem perde a leitura do canal privado é **desatribuído** (não há transferência automática). A tarefa permanece para quem ainda lê a origem |
| Inbox | Kind `task` (prioridade 8, reason `assigned`) para cada assignee de tarefa não concluída, com o mesmo filtro de menção/DND/`HidePreview` do action item |
| Eventos | Outbox `task.created`, `task.updated`, `task.completed`, `task.overdue`. Payload sem título nem body. `task.overdue` sai uma vez (`OverdueNotifiedAt`). `restricted` vai ao criador e aos assignees; `channel` ao grupo do canal |
| Delete/purge | Soft-delete e purge da origem setam `SourceRedacted`. `[[task:uuid]]` inacessível ou redigido volta `redacted` sem título |

### Formulários e aprovações (B-124)

Coleta estruturada em `messaging.forms`. Schema publicado imutável em `messaging.form_versions` (`form.schema.v1`). Resposta em `form_submissions`. Decisão append-only em `form_decisions`. Flag `Forms:Enabled` (**off** por default; 404 `FormDisabled` sem gravar). ADR-034.

| Item | Regra |
|------|--------|
| Tabelas | `forms`, `form_versions`, `form_submissions`, `form_decisions`. `TenantId` + FORCE RLS. Unique `(TenantId, CreatedBy, IdempotencyKey)`, `(TenantId, FormId, Version)`, `(TenantId, SubmittedBy, IdempotencyKey)`, `(TenantId, ActorUserId, IdempotencyKey)` e `(TenantId, SubmissionId, ActorUserId)` |
| Permissões | `form.create` cria e edita o próprio rascunho. `form.publish` publica e arquiva o próprio. `form.submit` lista, lê e envia. `form.review` decide e exporta. Member tem as quatro. Auditor só `form.review`. Guest e bot sem `form.*` |
| Schema | Campos `text\|number\|choice\|date\|user\|attachment`. Aprovação `single` ou `n_of_m`. Resultado `none\|message\|task`. Propriedade desconhecida → **400** `InvalidFormSchema`. Campo oculto e obrigatório ao mesmo tempo é inválido |
| `POST …/forms` | `{ idempotencyKey, channelId, title, schema }`. Replay da chave devolve o mesmo rascunho. Revisor sem leitura do canal → **400** `ReviewerNotMember`. Teto 30 vivos por canal |
| `PATCH` | Atualiza título e rascunho. Não altera versão já publicada |
| `POST …/publish` | Grava nova `form_versions` só se o JSON canônico mudou. Submission antiga permanece na versão em que foi enviada |
| `POST …/submissions` | `{ idempotencyKey, answers }`. Só formulário publicado. Campo oculto → **400** `HiddenField`. Chave desconhecida → **400** `UnknownField`. Replay não duplica |
| `POST …/decisions` | `{ idempotencyKey, decision, actorUserId? }`. Actor gravado é o autenticado. `actorUserId` de outra pessoa → **400** `ActorNotAllowed` sem gravar. Quem não está na lista da versão → **403**. Segunda decisão do mesmo actor → **409** `AlreadyDecided` |
| Resultado | No quórum, no máximo uma mensagem (body = título congelado, sem respostas) e, se `task`, uma tarefa apontando para ela. Chave `form-msg:` / `form-task:` + id. Profundidade 1 |
| Export / retenção | `GET …/export` só no escopo (autor, criador ou revisor da versão). Audit `form.exported` sem respostas. Retenção zera `AnswersJson` e marca `answersPurged` |
| Eventos | Outbox `form.published`, `form.submitted`, `form.approved`, `form.rejected`. Payload sem título e sem respostas |

### Automation builder (B-125)

Definição declarativa em `automation.definitions`. Versão publicada imutável em `automation.versions` (`automation.definition.v1`). Execução em `automation.runs` e `automation.steps`. Flag `Automation:Enabled` (**off** por default; 404 `AutomationDisabled` sem gravar). ADR-035.

| Item | Regra |
|------|--------|
| Tabelas | `definitions`, `versions`, `runs`, `steps`. `TenantId` + FORCE RLS. Unique `(TenantId, CreatedBy, IdempotencyKey)`, `(TenantId, AutomationId, Version)`, `(TenantId, IdempotencyKey)` em run e step |
| Permissões | `automation.manage` cria, publica, liga, desliga, testa e lê o histórico. Só Owner/Admin. `automation.notify` e `automation.connector` são capabilities da definição, não da API |
| Definição | Triggers `message\|form\|task\|schedule\|webhook`. Ações `post_message\|create_task\|update_task\|notify\|call_connector`. Propriedade desconhecida → **400** `InvalidDefinition`. Valor de segredo → **400** `SecretValueNotAllowed` |
| `POST …/publish` | Congela o JSON e a interseção de capabilities. Identidade `automation:{id}` com `Role.Bot`. Token de webhook só na resposta e só como hash no banco |
| `POST …/enable` / `disable` | Desligar impede runs novos. Ligar zera o circuito |
| `POST …/dry-run` | Plano sem efeito externo |
| `POST …/trigger` e `POST …/hooks` | Idempotentes por evento. Hook anônimo com `X-Automation-Token`. Token inválido → **404** |
| Efeitos | Retry usa a mesma chave e não duplica mensagem, tarefa ou notificação. Loop na profundidade 3 grava `automation.loop_stopped`. Conector inexistente → `ConnectorNotAllowed` sem rede |
| Eventos | Outbox `automation.created`, `automation.published`, `automation.enabled`, `automation.disabled`, `automation.notified`, `automation.loop_stopped`, `automation.circuit_open`, `automation.rate_limited`. Payload sem body e sem segredo |

### Incident rooms (B-126)

Sala privada em `incident.rooms`. Passos em `incident.steps`. Pessoas em `incident.responders`. Timeline em `incident.timeline` (só `kind` + `sourceId`). Flag `Incidents:Enabled` (**off** por default; 404 `IncidentDisabled` sem gravar). ADR-036.

| Item | Regra |
|------|--------|
| Tabelas | `rooms`, `responders`, `steps`, `timeline`. `TenantId` + FORCE RLS. Unique `(TenantId, CreatedBy, IdempotencyKey)`, `(TenantId, ChannelId)`, `(TenantId, IncidentId, UserId)`, `(TenantId, IncidentId, StepIndex)`, `(TenantId, IncidentId, IdempotencyKey)` |
| Permissões | `incident.manage` cria a sala. Só Owner/Admin. `incident.export` lê o postmortem. Só Owner/Admin e só se também for membro. Commander e responder não ganham `workspace.admin` |
| Template | `default` (`incident.playbook.v1`): `acknowledge`, `mitigate`, `communicate`, `postmortem`. Outro template → **400** `UnknownIncidentTemplate` |
| `POST …/incidents` | `{ idempotencyKey, templateKey, title, severity, responderIds?, correlationId?, automationId? }`. Cria canal `Private` e a sala. Replay da chave devolve a mesma sala. Quem não é membro do workspace → **400** `ResponderNotMember`. Teto 30 salas não resolvidas |
| Estados | `declared → mitigating → resolved`. Outro salto → **409** `InvalidIncidentTransition`. `resolved` grava o snapshot e recusa evidência nova (`IncidentClosed`) |
| Timeline | `message`, `decision` e `task` precisam existir no canal da sala. `audit` precisa ser deste incidente. Fora isso → **400** `EvidenceNotFound` sem body. Atualização grava a nota só no audit |
| Automação | `POST …/steps/{id}/run` não chama o runner de B-125. Automação ausente, desligada ou não publicada → passo `failed` / `AutomationFailed` e evento `incident.automation_failed`. A sala não muda de estado |
| Leitura | Quem não está em `responders` recebe **404** `IncidentNotFound` sem título nem severidade. Outro tenant → **403** |
| Eventos | Outbox `incident.declared`, `incident.transitioned`, `incident.resolved`, `incident.automation_failed`. Payload sem título e sem nota |

### Conectores bidirecionais (B-127)

Instalação por workspace em `connectors.installations`. Efeito idempotente em
`connectors.effects`. Tentativa externa em `connectors.deliveries` (sem body).
Cursor em `connectors.checkpoints`. Flag `Connectors:Enabled` (**off** por
default; 404 `ConnectorDisabled` sem gravar). ADR-037.

| Item | Regra |
|------|--------|
| Tabelas | `installations`, `effects`, `deliveries`, `checkpoints`. `TenantId` + FORCE RLS. Unique `(TenantId, CreatedBy, IdempotencyKey)`, `OpaqueKey`, `(InstallationId, ExternalEventId)` |
| Permissão | `connector.manage` lista, cria, rotaciona, revoga, reconecta e dispara entrega. Só Owner/Admin |
| Catálogo | `github` (`issues:read`, `issues:write`), `gitlab` (`read_api`, `write_repository`), `calendar` (`calendar:read`, `calendar:write`). Outro provedor → **400** `UnknownConnector`. Escopos diferentes do mínimo → **400** `ScopeMismatch` |
| `POST …/connectors` | `{ idempotencyKey, provider, destinationKind, destinationId, scopes, token?, clientId?, baseUrl? }`. Destino `channel`, `task` ou `incident` no mesmo workspace. Replay da chave devolve a mesma instalação. Calendário exige URL HTTPS pública; host privado ou rebinding → **400** `EgressBlocked` ou `DnsRebinding`. GitHub/GitLab recusam `baseUrl` |
| Segredo | Envelope AES-GCM. Resposta só `secretMask`. Token curto ou com URL → **400** `InvalidToken` |
| OAuth | PKCE público. `POST …/oauth/start` devolve `authorizeUrl` e os escopos mínimos. `POST …/oauth/callback` troca o code no host do catálogo e não devolve o access token |
| Inbound | `POST /api/v1/connectors/inbound/{opaqueKey}` anônimo. Assinatura `X-VibeChat-Connector-Signature` ou `X-Hub-Signature-256`; GitLab também aceita `X-Gitlab-Token`. Tenant do JSON é ignorado. Repetido → mesmo `effectId`. Revogado → **410** `ConnectorRevoked` sem efeito e sem rede |
| Efeito | Canal: uma mensagem do bot `connector:{id}` com resumo curto. Tarefa: um item de checklist. Incidente: uma linha `connector` na timeline, só `sourceId`. Incidente `resolved` → `IncidentClosed` |
| Outbound | `POST …/deliveries` e `POST …/reconnect`. Circuito abre na 3ª falha por 60s. Backoff não chama a rede. Redirect para fora da allowlist ou para IP privado não é seguido. Falha não grava outbox de mensagem |
| Revogar | Limpa envelopes. Chamadas seguintes não saem |

### Base de conhecimento (B-120)

Páginas server-authoritative em `knowledge.pages`. Coleções em `knowledge.collections` com escopo `workspace`, `space` ou `channel`. Histórico append-only em `knowledge.page_versions`. Flag `Knowledge:Pages:Enabled` (**off** por default; 404 `KnowledgeDisabled` sem gravar). ADR-032.

| Item | Regra |
|------|--------|
| Tabelas | `knowledge.collections`, `knowledge.pages`, `knowledge.page_versions`. `TenantId` + FORCE RLS. Unique parcial de slug ignora soft-delete. `pages.Version` é concurrency token |
| Permissões | `knowledge.view` lê, busca e exporta a página visível. `knowledge.edit` cria coleção/página e atualiza. `knowledge.manage` apaga, restaura e exporta o workspace. Auditor só view. Member view+edit. Guest e bot sem `knowledge.*` |
| ACL | Canal: membership atual. Workspace/space: membership do workspace. Manage não atravessa canal privado |
| `POST …/knowledge/collections` | `{ idempotencyKey, title, scope, scopeId?, parentId? }`. Árvore de um nível. Teto 100 coleções vivas por workspace |
| `POST …/knowledge/pages` | `{ idempotencyKey, collectionId, title, body }`. Body ≤ 16000, sem HTML nem `javascript:`/`data:`/`vbscript:`. Senão **400** `UnsafeKnowledgeBody`. Teto 200 páginas vivas por coleção |
| `PATCH …/pages/{pageId}` | `{ version, title, body }`. Versão diferente, ou duas gravações que colidem no índice único de `knowledge.page_versions`, → **409** `KnowledgeVersionConflict` com `currentVersion` e sem gravar |
| `POST …/restore` | `{ expectedVersion, sourceVersion }` cria versão nova com o conteúdo antigo. A mesma corrida de versão do PATCH também responde **409** `KnowledgeVersionConflict` sem gravar. `knowledge.manage` |
| `GET …/search?q=` | Mínimo 2 caracteres. Só páginas de coleções visíveis. Sem título de página inacessível |
| Referências | Tokens `[[message:uuid]]`, `[[decision:uuid]]`, `[[task:uuid]]`, `[[attachment:uuid]]`. `task` resolve `messaging.tasks` (B-123). Inacessível ou apagado → `{ state: redacted }` sem label |
| Export | `GET …/pages/{id}/export` devolve página e versões para quem vê. `GET …/knowledge/export` exige manage e o mesmo filtro de ACL |
| Retenção | Soft-delete entra no purge do tenant: página e versões saem juntas |
| Evento | Outbox `knowledge.page.changed` (`tenantId`, `workspaceId`, `pageId`, `collectionId`, `version`, `action`, `channelId?`). Sem título nem body. SignalR só no escopo canal |

### Inbox unificada (B-117)

Consulta reconstruível. Não há segunda fonte de leitura: menção e DM usam
`messaging.read_cursors`; thread seguida usa `thread_subscriptions.LastReadSeq`.
O único estado novo é o snooze pessoal.

| Item | Regra |
|------|--------|
| Ordem | `announcement` (1), `mention` (2), `dm` (3), `thread` (4), `saved` (5), `topic` (6), `commitment` (7), `task` (8). Dentro do tipo: `sortAt` decrescente e `itemKey` ascendente. DM usa `channel.CreatedAt` (estável); os demais usam o instante imutável da origem |
| Dedup | Anúncio, menção e salvo do mesmo `messageId` aparecem uma vez, no tipo de maior prioridade. DM e thread não colapsam |
| `GET /api/v1/workspaces/{workspaceId}/inbox` | `message.read`. Query `type` (`announcement\|mention\|dm\|thread\|saved\|topic\|commitment\|task`), `state` (`open` default, `snoozed`, `all`), `cursor`, `limit` (máx. 50). Outro tenant ou workspace inacessível → **403**. Filtro inválido → **400** `InvalidInboxFilter`. Cursor inválido → **400** `InvalidCursor` |
| Item | `{ itemKey, kind, priority, reason, channelId, channelName, messageId?, threadId?, sequence?, bodyPreview, messageRemoved, sortAt, snoozedUntil?, navigation }`. `reason`: `pending_ack`, `mention`, `unread_dm`, `followed_thread`, `saved`. Sem membership o item some por completo |
| Contagens | `counts.{announcement,mention,direct,thread,saved,total}` do estado pedido, antes do filtro de tipo |
| Ler | `POST .../inbox/read` body `{ itemKey }` avança o cursor canônico até a sequência do item (DM: última sequência do canal; thread: `LastReadSeq` da assinatura). Repetir após sumir → **404** `InboxItemNotFound`. Anúncio e salvo → **400** `InboxReadNotApplicable`. Publica `ReadCursorChanged` no canal |
| Não lido | `POST .../inbox/unread` retrocede o mesmo cursor para `sequence - 1` |
| Snooze | `PUT .../inbox/snooze` body `{ itemKey, until }`. `until` futuro e no máximo 30 dias; senão **400** `InvalidSnoozeUntil`. Upsert por `(TenantId, UserId, ItemKey)`. Item invisível → **404** sem título nem preview. `DELETE .../inbox/snooze?itemKey=` é idempotente (**204**) |
| Tabela | `messaging.inbox_snoozes` (`Id`, `TenantId`, `WorkspaceId`, `UserId`, `ItemKey`, `SnoozeUntil`, `CreatedAt`); unique `(TenantId, UserId, ItemKey)`; FORCE RLS. Expirado volta a `open` sem job |
| Realtime | O cliente recarrega em `MessageCreated`, `ReadCursorChanged`, `announcement.acknowledged` e reconnect. A projeção não tem tabela de leitura |

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

### Tópicos de canal (B-118 / ADR-031)

Modo explícito. `ChannelResponse.layout` é `Stream` (default) ou `Topics`. Criar com `layout: "Topics"` exige `Conversations:Topics:Enabled`; senão **404** `TopicsDisabled`. Canal `Stream` não muda. Postar na raiz de um canal `Topics` → **400** `TopicRequired`.

| Endpoint | Notas |
|----------|-------|
| `GET /api/v1/channels/{channelId}/topics` | `filter=recent\|open\|closed\|unread`, `tag`, `cursor`, `limit`. Membership do canal. `{ items, nextCursor, canManage }` |
| `POST /api/v1/channels/{channelId}/topics` | `{ title, idempotencyKey, tags? }`. `conversation.topic.create`. Cria a conversa do tópico e segue o autor. **201** |
| `GET /api/v1/topics/{topicId}` | Metadado. Sem acesso ao canal pai → **403**. Outro tenant → **404** |
| `PUT /api/v1/topics/{topicId}` | `{ title, idempotencyKey }`. Autor ou `conversation.topic.manage`. Slug estável |
| `POST /api/v1/topics/{topicId}/close` e `/reopen` | `{ idempotencyKey }`. `conversation.topic.manage` |
| `POST /api/v1/topics/{topicId}/move` | `{ channelId, idempotencyKey }`. Destino no mesmo workspace, layout `Topics`, manage nos dois. ACL passa a ser a do destino |
| `POST` / `DELETE /api/v1/topics/{topicId}/subscription` | Seguir. Não lidas usam o read cursor da conversa do tópico |
| `PUT /api/v1/topics/{topicId}/read-cursor` | Mesmo corpo do cursor de canal, no id do tópico |
| `GET` / `POST /api/v1/topics/{topicId}/messages` | Seq independente. Post exige `message.send`. Fechado sem manage → **403** `TopicClosed` |

Inbox: kind `topic` (prioridade depois de `saved`). `topicId` na navegação. Eventos de outbox `topic.created`, `topic.renamed`, `topic.closed`, `topic.reopened`, `topic.moved`, `topic.followed`.

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
| `POST /api/v1/workspaces/{workspaceId}/channels` | Body `{ name, type, spaceId? }`; exige `channel.create`; `spaceId` deve pertencer ao workspace. `type: ConfidentialE2EE` → 400 `ContentAuditBlocksE2ee` com auditoria ligada; 400 `ConfidentialE2eeUnavailable` com auditoria desligada até B-064 (ADR-043) |
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

`ChannelResponse` inclui `spaceId?`, `topic?`, `hasGuests?`, `layout` (`Stream` \| `Topics`) e, para DMs, `peerUserId` / `peerDisplayName`. GroupDm inclui `participantCount?`, `participantNames?`, `participantUserIds?`; `name` é o Title ou os nomes dos outros participantes. Channels `Private`/`Direct`/`Group`/`GroupDm` só aparecem na listagem para membros do canal. Guest (B-040) vê **somente** o canal do convite. Spaces agrupam channels na UI; DMs (1:1 e grupo) ficam fora de spaces. History de GroupDm filtra `seq > JoinedSeq`.

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

### `messaging.message.moved`

Evento de realtime `MessageMoved` no canal de destino. Não entra na lista de webhooks. Não carrega body do canal de origem para quem não é membro — o payload vai só ao grupo do destino.

```json
{
  "messageId": "…",
  "channelId": "…",
  "conversationId": "…",
  "sequence": 7,
  "authorId": "…",
  "body": "texto copiado",
  "movedFromMessageId": "…",
  "movedFromChannelId": "…"
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

### `attachment.scan.requested` / `attachment.scan.completed` (B-131)

Com `Files:MalwareScan:Enabled` (default off), o `complete` grava `PendingScan`
e enfileira `attachment.scan.requested`. O worker publica
`AttachmentScanCompleted` (`status`, `scanStatus`, `scanReason` — sem storage
key) e, se `Clean`, o outbox `files.attachment.ready`. Flag off usa o adapter
nulo e não enfileira scan.

### `files.attachment.ready`

Metadados prontos / vírus scan ok (quando a flag de scan está ligada, só depois
de `Clean` ou release). No caminho sem scan, o `complete` segue direto. Anexos
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
| `AttachmentScanCompleted` | Scan de anexo terminou (B-131): `status`, `scanStatus`, `scanReason` |
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
(PDF). Scan (B-131): `ScanStatus`, `ScanEngine`, `ScanSignatureVersion`,
`ScanReason`, `ScanAttempts`, `ScanRequestedAt`, `ScannedAt`. Miniatura WebP lado maior 640 px em
`tenants/{tenantId}/channels/{channelId}/{attachmentId}/thumb.webp`. Forward
clona os campos de thumbnail junto com `StorageKey`.

### Endpoints (API)

| Endpoint | Notas |
|----------|-------|
| `POST /api/v1/channels/{channelId}/attachments` | Body `{ fileName, contentType, sizeBytes, kind?, durationMs?, waveform?, width?, height? }` → URL pré-assinada PUT; exige membership + `file.upload`; `kind=Audio` exige `durationMs` e aceita `waveform` (0–100, ≤100 pts); `kind=Video` exige `durationMs`, aceita `width`/`height` |
| `POST /api/v1/channels/{channelId}/attachments/{id}/complete` | Confirma objeto no MinIO. Flag de scan off: `Ready` + `scanStatus=NotRequired` e, para image/PDF, `ThumbnailStatus=Pending` + outbox `files.attachment.ready`. Flag on: `PendingScan` + outbox `attachment.scan.requested` (B-131) |
| `GET /api/v1/channels/{channelId}/attachments/{id}/download` | URL pré-assinada GET do original; exige membership + `file.download`. `PendingScan`, `Quarantined` e `ScanFailed` (fail-closed) respondem 404 |
| `POST /api/v1/channels/{channelId}/attachments/{id}/rescan` | Reenfileira `ScanFailed`. `PendingScan` repete o estado. Flag off → 404 `MalwareScanDisabled` |
| `GET /api/v1/workspaces/{workspaceId}/quarantine` | Lista do tenant; `file.quarantine.manage`; sem storage key |
| `POST /api/v1/workspaces/{workspaceId}/quarantine/{id}/release` | `Clean` + audit; justificativa 3–500 |
| `POST /api/v1/workspaces/{workspaceId}/quarantine/{id}/delete` | Apaga o objeto se não houver outra referência; audit |
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
| `GET /api/v1/workspaces/{workspaceId}/semantic/settings` | B-121. `ai.search`. `404 SemanticSearchDisabled` se o kill switch de processo está off. Devolve `enabled`, orçamento, `tokensUsed`, `indexedChunks` e `embeddingProfile`. Não devolve texto indexado. |
| `PUT /api/v1/workspaces/{workspaceId}/semantic/settings` | B-121. `workspace.admin`. Body `{ enabled }`. Off apaga a projeção do workspace (chunks). On não indexa sozinho — o outbox e `rebuild` preenchem. |
| `POST /api/v1/workspaces/{workspaceId}/semantic/query` | B-121. `ai.search` + `search.messages`. Body `{ q }`. Resposta `{ evidence: cited\|none, answer, confidence, tokensCharged, citations[] }`. `answer` só repete trechos citados. ACL revalidada na leitura. `404 SemanticSearchDisabled`, `429 SemanticBudgetExceeded` \| `SemanticRateLimited`, `400 InvalidSemanticQuery`. Não substitui o FTS. |
| `POST /api/v1/workspaces/{workspaceId}/semantic/report` | B-121. Mesmas permissões da query. Body `{ citationIds }`. Audita a contagem, sem gravar a pergunta nem o trecho. |
| `POST /api/v1/workspaces/{workspaceId}/semantic/rebuild` | B-121. `workspace.admin`. Reconstrói a projeção das mensagens recentes do workspace e das páginas de conhecimento. Qdrant, se `SemanticSearch:QdrantUrl` estiver definido, recebe só vetor e ids. |

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
| `GET /api/v1/admin/conversations/{channelId}/messages?after=&limit=` | Histórico admin do canal/DM (root); com `contentAuditEnabled=true`, body visível mesmo com soft-delete; com `false`, `body` vazio e `bodyRedacted: true` (ids, seq e timestamps permanecem); inclui `deletedBy` / anexos; exige `admin.dashboard`; canal fora do tenant → 403 |
| `GET /api/v1/admin/threads/{threadId}/messages?after=&limit=` | Histórico admin de replies da thread; mesma authZ e semântica de body |
| `GET /api/v1/admin/settings?workspaceId=` | Settings sensíveis mascarados (B-069 / ADR-020); exige `workspace.admin` (Auditor com só `admin.dashboard` → 403); `workspaceId` opcional (default: primeiro workspace do actor) |
| `PUT /api/v1/admin/settings` | Atualiza flags não-secretas (AI/email/webhooks/retention/linkPreview/files/rateLimit/`contentAudit`/`openRouterBaseUrl`/`*.processEnabled`); mesma authZ; rejeita secrets no body (`SecretsNotWritable`); audit `settings.change`. `contentAudit.enabled: false` exige `confirmDisable: true` (400 `ContentAuditConfirmationRequired`) e recusa 409 `ContentAuditBlockedByLegalHold` se houver hold ativo; audit `settings.content_audit.change` sem body de mensagem (B-169 / ADR-043) |
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
| `GET /api/v1/admin/workspaces/{workspaceId}/export` | Export compliance do workspace (B-046); ZIP `application/zip` com JSON (`manifest`, `workspace`, `members`, `contact-groups`, `spaces`, `channels`, `threads`, `messages`, `attachments` metadata); com `contentAuditEnabled=true`, corpos soft-deleted incluídos (paridade B-067); com `false`, `body` vazio e `bodyRedacted: true` em mensagens e versões; **sem** binários MinIO; exige `workspace.admin` (Auditor → 403); audit `workspace.export`; workspace fora do tenant/membership → 403 |

`AuditEventResponse`: `id`, `action`, `entityType`, `entityId`, `actorUserId`, `occurredAt`, `metadataJson`.

`AdminConversationResponse`: `id`, `workspaceId`, `name`, `type`, `spaceId`, `peerUserId`, `peerDisplayName`.

`AdminConversationMessageResponse`: `id`, `channelId`, `conversationId`, `sequence`, `authorId`, `authorName`, `body`, `createdAt`, `editedAt`, `deletedAt`, `deletedBy`, `deletedByName`, `threadId`, `replyToMessageId`, `replyCount`, `attachments`, `poll?` (B-096; anônima sem votantes), `bodyRedacted`. Com `contentAuditEnabled=false`, `body` vem vazio e `bodyRedacted` é `true`.

Ações mínimas: `admin.login`, `channel.create`, `space.create`, `message.send`, `message.delete`, `attachment.upload`, `member.role.change`, `member.invite`, `contact_group.create`, `contact_group.delete`, `contact_group.members.replace` (só departamento), `settings.change`, `settings.content_audit.change`, `settings.credential.rotate`, `settings.encryption.reencrypt`, `workspace.export`, `message.purge`, `message.preserve`, `poll.create`, `poll.vote`, `poll.unvote`, `poll.close`.

**Legal hold e eDiscovery (B-129 / ADR-042).** Flag `Features:LegalHold:Enabled` default false → 404 `LegalHoldDisabled`. Rotas em `/api/v1/workspaces/{workspaceId}/compliance/`: `access` (`workspace.read`), `grants` (`workspace.admin`, sem auto-nomeação), `holds` (`compliance.hold`, aprovação de outra pessoa, duas confirmações), `cases` e `exports` (`compliance.discovery`). Export `vibechat.ediscovery.export.v1` com progresso e SHA-256 do manifesto. Hold ativo e não expirado impede o purge só no escopo. `compliance.custody_events` é append-only.

**Classificação e DLP (B-130 / ADR-044).** Flag `Features:Dlp:Enabled` default false → 404 `DlpDisabled` e o gate não muda efeitos. Rotas em `/api/v1/workspaces/{workspaceId}/dlp/`: `label` (`workspace.read`), demais (`compliance.dlp`, grant, não papel). Herança canal → space → workspace. Sem política, simulação mede e não bloqueia. Enforce é explícito. `block`/`quarantine` impedem encaminhamento, convite, export, webhook HTTP, IA e download antes do efeito. Decisão não guarda body. E2EE responde `NotInspectable`.

**SIEM (B-132 / ADR-045).** Flag `Features:Siem:Enabled` default false → 404 `SiemDisabled` e o worker não envia. Rotas em `/api/v1/workspaces/{workspaceId}/siem/destinations` exigem grant `security.audit.export` (capability `audit.export`, não papel). Transporte `webhook-json` ou `syslog-cef`, HMAC-SHA256, envelope `audit.event.recorded` v1. Sem body de mensagem. Retry reutiliza `eventId`. Backfill máximo 24 h e 100 eventos do próprio tenant. Destino inválido abre o circuito.

**Policy packs (B-133 / ADR-046).** Flag `Features:PolicyPacks:Enabled` default false → 404 `PolicyPackDisabled` e o convite não consulta pack. Rotas em `/api/v1/workspaces/{workspaceId}/policy-packs` exigem grant `compliance.policy` (capability `policy`, não papel). Manifesto `policy-pack.v1`: `small-team`, `enterprise-baseline`, `regulated`. Preview e apply devolvem o mesmo documento. Override que enfraquece controle locked → 409 `PolicyPackOverrideForbidden`. Import com tenant, segredo ou código → 400 `PolicyPackImportRejected`. Export não leva segredo. Drift compara o documento com IA, retenção, upload, DLP e auditoria de conteúdo. Rollback exige versão anterior. `certification` é sempre false.

**Administração delegada (B-134 / ADR-047).** Flag `Features:DelegatedAdmin:Enabled` default false → 404 `DelegatedAdminDisabled` e o chat não consulta quota. Escopos fixos: `tenant`, `workspace`, `space`, `compliance`, `security`, `billing-read`. Grant exige `Idempotency-Key`, motivo e não pode subir acima do grantor nem atribuir a si mesmo (`DelegationScopeExceeded`, `DelegationSelfGrant`). Space só administra o próprio `spaceId`. Expiração revoga na leitura. Break-glass dura no máximo 4 horas, alerta na abertura e de novo ao expirar. Quotas `users`, `channels`, `storage`, `messages`, `automations`, `ai-budget`: hard limit é um `UPDATE` condicional; estouro → 409 `QuotaHardLimit`. Sem linha de quota o recurso não bloqueia. Falha do contador não derruba o envio.

**Capacidade e SLOs (B-146 / ADR-048).** Flag `Features:CapacitySlo:Enabled` default false → 404 `CapacitySloDisabled`. `GET /api/v1/workspaces/{workspaceId}/capacity` e `POST .../capacity/evaluations` exigem `workspace.admin`. O corpo é o catálogo da instalação (`isSla: false`): perfis `small`/`standard`/`large`, recuperação Basic/Standard/HA e SLIs. Sem amostra, `burnState` é `unmeasured`. Resultado medido: `vibechat.capacity-result.v1`, com commit, ambiente, data e margem. `isSla: true` → 400 `CapacitySloSlaRejected`. E-mail ou campo de pessoa → 400 `CapacitySloEnvelopeRejected`. A avaliação não grava linha.

**Metadata de `message.delete` (B-169 / ADR-043):** quando `contentAuditEnabled=true`,
`metadataJson` inclui `channelId`, `threadId?`, `sequence`, `authorId`, `deletedAt` e `body`
(conteúdo no soft-delete). Com `false`, os mesmos ids e timestamps, sem `body`.
`settings.content_audit.change` não carrega body de mensagem. O dashboard admin
inclui `contentAuditEnabled` para o indicador de modo confidencial.

### Export de workspace (B-046)

- Formato: `vibechat.workspace.export.v1` (`manifest.json.format`)
- AuthZ alinhada a settings sensíveis (`workspace.admin`); não usar só `admin.dashboard`
- Mensagens: body persistido inclusive soft-delete (`deletedAt` / `deletedBy`) quando `contentAuditEnabled=true`; com `false`, `body` vazio e `bodyRedacted: true` (também em `message-versions.json`)
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
| `contentAudit.enabled` | `audit.content_audit_settings`; ausência da linha = `true` (B-169 / ADR-043). `false` exige `confirmDisable` e não convive com legal hold ativo |

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
- B-139 / ADR-041: com `Integrations:EventSchemas:Enabled=true`, a entrega valida o body no `PublicEventCatalog`. Sem `schemaVersion` no JSON, a versão é 1. Tipo ou versão desconhecidos não fazem POST; `LastError` recebe `UnknownEventType`, `UnknownSchemaVersion` ou `SchemaMismatch`; a outbox segue. Headers adicionais: `X-VibeChat-Event-Type`, `X-VibeChat-Schema-Version`. Flag off não altera body nem headers. Catálogo e exemplos: [`eventos-publicos.md`](eventos-publicos.md)

### Eventos públicos (B-139 / ADR-041)

Flag `Integrations:EventSchemas:Enabled` default **false**.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/event-schemas` | `workspace.admin`; flag off → 404 `EventSchemasDisabled` |
| GET | `/api/v1/admin/event-schemas/{eventType}/versions/{schemaVersion}` | `workspace.admin`; desconhecido → 404 `UnknownEventType` ou `UnknownSchemaVersion` |
| POST | `/api/v1/admin/event-schemas/check` | `workspace.admin`; body `{ eventType, schemaVersion, document }`; 422 `SchemaMismatch`; 400 `SchemaDocumentTooLarge` (16 KiB). Não persiste o documento |

O catálogo não contém dado de tenant. `messaging.message.created` v1 (wire `MessageCreated`, deprecated até 2027-04-19) e v2 (envelope) coexistem. O wire default continua v1. Métrica `vibechat.events.schema.rejected` com `reason` e `event_type` apenas.

### Portal do desenvolvedor (B-135)

Flag `Integrations:DeveloperPortal:Enabled` default **false**. Off → 404 `DeveloperPortalDisabled`. O chat e o send de B-109 não dependem desta flag. O send real continua em `Integrations:Bots:Enabled`.

Credencial reusa `integrations.bots` / `bot_tokens` / `bot_channel_scopes` (hash SHA-256, prefixo `vc_int_`, escopo de canal e `allowDms`). `POST` de criação e rotação exigem `Idempotency-Key`. A resposta inclui `token` uma vez; o replay e o `GET` devolvem `token: null` e só `last4`. Revogação grava `RevokedAt` na mesma transação; o próximo send com esse token é 401.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/workspaces/{workspaceId}/developer/catalog` | `integration.read` |
| GET | `/api/v1/workspaces/{workspaceId}/developer/openapi` | `integration.read`; paths iguais a `POST /api/v1/integrations/v1/channels/{channelId}/messages` e `POST /api/v1/integrations/v1/dms` |
| GET | `/api/v1/workspaces/{workspaceId}/developer/contract-kit` | `integration.read`; OpenAPI + schemas de `PublicEventCatalog` |
| GET | `/api/v1/workspaces/{workspaceId}/developer/credentials` | `integration.read` |
| POST | `/api/v1/workspaces/{workspaceId}/developer/credentials` | `integration.manage`; body `{ name, channelIds[], allowDms }` |
| POST | `/api/v1/workspaces/{workspaceId}/developer/credentials/{credentialId}/rotate` | `integration.manage` |
| POST | `/api/v1/workspaces/{workspaceId}/developer/credentials/{credentialId}/revoke` | `integration.manage` |
| POST | `/api/v1/workspaces/{workspaceId}/developer/explore` | `integration.manage`; body `{ credentialId, operation, channelId? }`; `messages.send` exige o canal no escopo; `messages.dm` exige `allowDms`; resposta `synthetic: true`, `delivered: false`; não chama `SendMessage` |
| POST | `/api/v1/workspaces/{workspaceId}/developer/webhook-inspector` | `integration.manage`; payload sintético de `messaging.message.created` v1; sem POST externo |
| GET | `/api/v1/workspaces/{workspaceId}/developer/usage` | `integration.read`; chamadas `developer.*` do próprio workspace |

`integration.read` e `integration.manage` estão no catálogo de Admin/Owner. Member, Auditor, Guest e Bot não têm. Respostas do portal usam `Cache-Control: no-store`. O token não entra em query string, auditoria nem idempotência.

Sandbox é o tenant autenticado: a chamada de teste não cria outro tenant e não atravessa o workspace. Usage filtra `workspaceId` no metadata e a RLS de `audit.audit_events`.

### SDK de plugins (B-136 / ADR-051)

Flag `Integrations:PluginSdk:Enabled` default **false**. Off → 404 `PluginSdkDisabled`. O chat, o send de B-109 e o portal de B-135 não dependem desta flag.

O SDK `1.0.0` (contrato HTTP `1`, licença Apache-2.0) deriva de `WebhookDelivery` e de `PublicEventCatalog`. Não há feed público (`published: false`). Pacotes: `sdks/dotnet/VibeChat.Sdk` e `sdks/typescript`. O verificador recusa assinatura inválida, replay (cache do caller) e entrega fora de 5 minutos. O segredo `sandbox-not-a-secret` só assina a rota sintética e não é credencial. Respostas usam `Cache-Control: no-store` e não devolvem `vc_int_` nem o segredo de sandbox.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/workspaces/{workspaceId}/developer/sdk` | `integration.read`; catálogo, matriz e exemplos sintéticos |
| POST | `/api/v1/workspaces/{workspaceId}/developer/sdk/verify` | `integration.read`; body `{ body, signature, deliveryId, deliveredAt, seenDeliveryIds }`; não persiste o segredo |
| POST | `/api/v1/workspaces/{workspaceId}/developer/sdk/contract-tests` | `integration.read`; runner sintético com 401, 403 e cross-tenant; sem token na resposta |
| POST | `/api/v1/workspaces/{workspaceId}/developer/sdk/sandbox` | `integration.read`; `messages.send` exige idempotency key; `delivered: false`; `tenantId` do body é ignorado |

`integration.read` está no catálogo de Admin/Owner. Member recebe 403. Workspace sem membership recebe 403. Auditoria `developer.sdk.verify`, `developer.sdk.contract` e `developer.sdk.sandbox` não leva corpo nem segredo.

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

Manifesto v1 (`vibechat.plugin.manifest.v1`): `id`, `name`, `version` (`N.N.N`), `capabilities`. Campo desconhecido → 400 `InvalidPluginManifest`. Capability fora de `messages.send`, `commands.slash`, `events.subscribe`, `ui.hooks`, `messages.interact`, `history.read` e `attachments.send` → 400 `UnknownPluginCapability`. Corpo acima de 4 KiB → 400 `PluginManifestTooLarge`. Built-ins do binário: `incoming-messages` e, com a flag abaixo, `slash-note` e `action-ack`. Não há loja nem download de pacote.

Flag `Features:Plugins:Enabled` default **false** (ADR-049). Off preserva `messages.send` e responde 404 `PluginPlatformDisabled` para capability extra, slash e callback. `commands.slash` exige `commands[]` (`name` slug + `description`). Comando na paleta: `pluginId/comando`. Execução `POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/plugin-commands` com `{ name, args, idempotencyKey }`, permissão `message.send`, capability e canal no escopo do bot. Fora do escopo → 403 `PluginCommandOutOfScope`. O built-in `slash-note` publica o argumento como mensagem do bot. Manifesto customizado não executa código: `callback.url` recebe POST HMAC (`X-VibeChat-Signature`, paridade B-048). URL http só em `localhost` / `127.0.0.1`; metadata, IP privado e redirect → `EgressBlocked` / falha fechada. Segredo `vc_cb_` uma vez, envelope AES-GCM. `events.subscribe` usa o mesmo callback no outbox (`MessageCreated`, `MessageEdited`, `MessageDeleted`, `ReactionChanged`) e não reenvia o eco do próprio bot. `ui.hooks` aceita só `message.badge.bot` e `message.menu` como dados.

B-111 (ADR-050), mesma flag. `messages.interact` exige `actions[]`. O cartão na mensagem é dado (`pluginId`, nome, expiração, ações `pluginId/ação`). Clique `POST …/plugin-actions` com `{ action, idempotencyKey }`, permissão `message.send` e membership do canal. Built-in `action-ack` confirma no servidor. Callback customizado é HMAC `plugin.action.invoked`; timeout → 502 `PluginCallbackFailed` e a mesma chave pode repetir. Expirada → 409 `PluginActionExpired`. `history.read` exige grant de canal e janela (máx. 7 dias), distinto do escopo do bot. Sem grant, ou canal inexistente, → 404 `PluginHistoryGrantNotFound`. Janela fora do grant → 403 `PluginHistoryWindowDenied`. Revogação grava `RevokedAt` e a leitura seguinte falha. `attachments.send` inicia e completa o upload pelo pipeline de anexos (tipo, tamanho, quota, scan, tenant) e rejeita script/DLL antes do objeto. O envio com `attachmentIds` no POST de mensagem do bot exige essa capability. Quotas diárias por plugin: interact 200, history 100, attachments 20. Acima → 429 `PluginQuotaExceeded`. Grant, revoke, leitura, ação e anexo entram em audit.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/workspaces/{workspaceId}/plugins/builtins` | `workspace.admin`; catálogo compilado, sem pacote remoto |
| GET | `/api/v1/admin/workspaces/{workspaceId}/plugins` | `workspace.admin` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugins` | `workspace.admin`; body com exatamente um de `builtinId` ou `manifest`, mais `channelIds[]` e `allowDms`; resposta inclui `token` **uma vez** |
| PATCH | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}` | `workspace.admin`; body `{ enabled }`; desligar responde 403 nos sends novos e preserva histórico |
| DELETE | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}` | `workspace.admin`; revoga o token na mesma transação; 204 |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}/rotate` | `workspace.admin`; novo `token` uma vez |
| POST | `/api/v1/workspaces/{workspaceId}/channels/{channelId}/plugin-commands` | `message.send`; body `{ name, args, idempotencyKey }`; 202 |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}/history-grants` | `workspace.admin`; body `{ channelId, windowStart, windowEnd }`; 201 |
| DELETE | `/api/v1/admin/workspaces/{workspaceId}/plugins/{installedId}/history-grants/{grantId}` | `workspace.admin`; revoga na hora; 204 |
| POST | `/api/v1/integrations/v1/channels/{channelId}/interactive-messages` | token do plugin; `{ body, actions, idempotencyKey, expiresInSeconds? }`; 202 |
| GET | `/api/v1/integrations/v1/channels/{channelId}/history` | token do plugin; `from`, `to`, `idempotencyKey`; só a janela concedida |
| POST | `/api/v1/integrations/v1/channels/{channelId}/attachments` | token do plugin; `{ fileName, contentType, sizeBytes, idempotencyKey }` |
| POST | `/api/v1/integrations/v1/channels/{channelId}/attachments/{attachmentId}/complete` | token do plugin; mesmo scan do anexo de usuário |
| POST | `/api/v1/workspaces/{workspaceId}/channels/{channelId}/messages/{messageId}/plugin-actions` | `message.send`; `{ action, idempotencyKey }`; 202 |

Slug duplicado ou acima de 20 plugins no workspace → 409 (`PluginAlreadyInstalled` / `PluginLimitReached`). Id de outro workspace → 404 `PluginNotFound`. Member → 403. Tabelas `integrations.plugins`, `plugin_history_grants`, `plugin_interactive_offers`, `plugin_action_receipts` e `plugin_quota_counters` com RLS FORCE. Uninstall não apaga mensagens. O `Down` remove as tabelas de B-111 e `LastUsedAt`; bots, mensagens e anexos ficam.

### Registry assinado (B-137 / ADR-053)

Flag `Integrations:PluginRegistry:Enabled` default **false**. Off → 404 `PluginRegistryDisabled`. O install local e o SDK não dependem desta flag.

Índice `vibechat.registry-index.v1`. Cada entrada traz o payload canônico `vibechat.registry-entry.v1` e a assinatura `rsapss-sha256=`. O payload inclui publisher, source, checksum `sha256=` do manifesto, capabilities, versão, risco e o manifesto. A assinatura é conferida antes de gravar. `http`/`https` em `catalogUrl` ou `artifactRef` → 400 `RegistryEgressBlocked`, sem conexão. Mirror: `mirror://`, `official://`, `private://`. Catálogo `allowlisted` só com o id na allowlist.

Instalar exige `grantCapabilities` cobrindo o pacote. Sem isso → 400 `PluginGrantRequired` e nada é gravado. Update com capability nova sem `confirmCapabilities` → 409 `PluginCapabilityConfirmationRequired` e a versão atual fica. Update para versão menor → 409 `PluginDowngradeRejected`. Rollback restaura o payload anterior compatível. Revoke desliga o plugin/bot e impede reinstalar aquela versão (`PluginVersionRevoked`). Dependência ausente no índice → 400 `InvalidRegistryIndex`; ausente no workspace → 409 `PluginDependencyMissing`.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry` | `workspace.admin`; instalações do workspace, sem token |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry/preview` | `workspace.admin`; body `{ index, catalogUrl? }`; não persiste |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry/install` | `workspace.admin`; `{ index, pluginId, version, grantCapabilities, channelIds?, allowDms, idempotencyKey }`; token **uma vez** |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry/update` | `workspace.admin`; capability nova exige `confirmCapabilities` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry/rollback` | `workspace.admin`; restaura a versão anterior assinada |
| POST | `/api/v1/admin/workspaces/{workspaceId}/plugin-registry/revoke` | `workspace.admin`; desliga a versão e grava quarentena |

`workspace.admin` apenas. Member e workspace sem membership → 403. Tabelas `integrations.plugin_registry_installs` e `plugin_registry_quarantine` com RLS FORCE. O `Down` remove só essas duas tabelas. Auditoria `integration.plugin.registry.*` não leva manifesto, assinatura crua nem `vc_int_`.

### Bridges (B-138 / ADR-054)

Flag `Integrations:Bridges:Enabled` default **false**. Off → 404 `BridgesDisabled`. O chat não depende desta flag.

Envelope `vibechat.bridge.envelope.v1` (schemaVersion 1). A API não abre conexão: o processo externo puxa `POST /bridges/{bridgeId}/outbound` e empurra `POST /bridges/{bridgeId}/events` com o token do bot (`X-VibeChat-Integration-Token`). Destino `http`/`https` → 400 `BridgeEgressBlocked`. Host fora de `PinnedHosts` → 400 `BridgeDestinationUnpinned`.

Uma ponte por canal, nunca DM. Estado `pending` até `acknowledgeCopy`. Sem consentimento, inbound e outbound respondem 409 `BridgeTrafficBlocked`. Pause e disconnect também. Disconnect desativa o bot. O mesmo `eventId` não cria outra mensagem. Eco com `InstanceId` local, ou id já emitido no pull, é loop e não grava mensagem. `deleteRemoteGuaranteed` é sempre false. Sem capability `deletes`, a mensagem local permanece e a auditoria registra `integration.bridge.delete.limited`. DLP `federation` que não prossegue omite o corpo.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/workspaces/{workspaceId}/channels/{channelId}/bridge` | membership + `message.read`; estado, destino e limitação |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bridges` | `workspace.admin`; token do bot **uma vez** |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/consent` | `workspace.admin`; `acknowledgeCopy: true` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/pause` | `workspace.admin` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/resume` | `workspace.admin` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/disconnect` | `workspace.admin`; desativa o bot |
| GET | `/api/v1/admin/workspaces/{workspaceId}/bridges/{bridgeId}/mapping` | `workspace.admin`; subject opaco, sem e-mail local |
| POST | `/api/v1/bridges/{bridgeId}/events` | token do bot desta ponte |
| POST | `/api/v1/bridges/{bridgeId}/outbound` | token do bot desta ponte |

Member → 403 nas rotas admin. Outro tenant não lê a linha (RLS FORCE). Auditoria não leva corpo nem `vc_int_`. O `Down` remove só as três tabelas novas.

### White-label (B-140 / ADR-055)

Sem flag. Sem linha em `directory.brand_settings`, a marca é a oficial do VibeChat. `productName` na resposta é sempre `VibeChat` e não é gravável.

O login público (`GET /api/v1/public/branding`) resolve o tenant só pelo host em `Branding:Hosts` (operador). `?tenantId=` é ignorado. Host fora do mapa devolve a marca oficial, sem vazar outro tenant. Shell e admin usam o tenant da membership.

Cores `#RRGGBB` precisam de contraste ≥ 3:1 nas superfícies clara e escura; senão `400 BrandContrastFailed`. Arquivo só PNG/JPEG, com scan, tamanho e encaixe no slot; SVG/HTML/script → `400`. Rollback restaura o snapshot anterior uma vez (`409 BrandRollbackEmpty` se não houver). Idempotency-Key obrigatória na escrita.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/public/branding` | anônimo; host do operador |
| GET | `/api/v1/public/branding/assets/{kind}` | anônimo; mesmo host; 404 se não houver arquivo |
| GET | `/api/v1/branding` | membership |
| GET | `/api/v1/branding/assets/{kind}` | membership |
| GET | `/api/v1/admin/branding` | `admin.brand` |
| PUT | `/api/v1/admin/branding` | `admin.brand` |
| POST | `/api/v1/admin/branding/assets/{kind}` | `admin.brand` |
| DELETE | `/api/v1/admin/branding/assets/{kind}` | `admin.brand` |
| POST | `/api/v1/admin/branding/rollback` | `admin.brand` |

`kind` é `logo-light`, `logo-dark` ou `favicon`. Member e Auditor → 403 na escrita. RLS `FORCE` em `directory.brand_settings`. O `Down` remove só essa tabela. Auditoria `brand.update` / `brand.asset` / `brand.rollback` não leva o nome nem os bytes.

### Cliente desktop (B-141 / ADR-056)

Flag `Desktop:Enabled` default **false**. Off → 404 `DesktopDisabled`. O chat e a PWA não dependem desta flag. Não há endpoint privilegiado: as rotas usam `message.read`, o mesmo contrato de `/api/v1`.

O shell é Tauri 2 e carrega o mesmo bundle web (`uiFork: false`, `apiBase: /api/v1`). Esquema `vibechat://{host}/v1/{channel|thread|workspace}/{guid}`. Host fora de `Desktop:AllowedHosts`, versão diferente de `v1`, userinfo, query, fragmento ou destino desconhecido → 400 `DesktopDeepLinkRejected`.

Auto-update fica **off** quando `Desktop:UpdateFeedUrl` está vazio ou não é `https` na porta padrão. A API não busca a URL. O manifesto canônico `vibechat-update/1` é conferido com RSA-PSS/SHA-256 (`Desktop:UpdatePublicKeyPem`, SPKI base64 ou PEM). Assinatura ou host divergente → 400 `DesktopUpdateRejected`. Feed ausente → 409 `DesktopUpdateOff`.

`Desktop:SigningCredentialRef` é só uma referência. PEM, `token`, `secret` ou `private` → `signing: rejected` e o valor não volta na resposta. A chave privada e o certificado não entram no repositório.

Registro de aparelho não aceita `access_token`, `refresh_token`, `authorization` nem `Bearer`. A tabela `identity.desktop_devices` não tem coluna de token. Logout remoto (`DELETE`) marca `RevokedAt` e a resposta pede `wipeSecureStorage: true` para o shell apagar o secure storage do OS. Dono ou `workspace.admin`. Outro membro → 403. Outro tenant → 404.

Matrix de pacote: `windows-x64`, `macos-arm64`, `linux-x64`. O hash de `apps/desktop/shell.lock.json` é o build reproduzível. Tray, notificações e launch-at-login nascem desligados; launch-at-login só liga com `true` explícito.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/desktop/shell` | `message.read` |
| POST | `/api/v1/desktop/deep-links/validate` | `message.read`; body `{ url }` |
| POST | `/api/v1/desktop/updates/verify` | `message.read`; manifesto + `signature` |
| GET | `/api/v1/desktop/devices` | `message.read`; só os do caller |
| POST | `/api/v1/desktop/devices` | `message.read`; `{ platform, appVersion }` + `Idempotency-Key` |
| PUT | `/api/v1/desktop/devices/{deviceId}/preferences` | `message.read`; dono; `{ launchAtLogin, notifications, tray }` |
| DELETE | `/api/v1/desktop/devices/{deviceId}` | `message.read`; dono ou `workspace.admin` |

RLS `FORCE` em `identity.desktop_devices`. O `Down` remove só essa tabela. Auditoria `desktop.device.register` / `desktop.device.preferences` / `desktop.device.logout` com metadata `{}`.

### Cliente mobile (B-063 / ADR-057)

Flag `Mobile:Enabled` default **false**. Off → 404 `MobileDisabled`. O chat, a PWA e o desktop não dependem desta flag. Não há endpoint privilegiado: as rotas usam `message.read`, o mesmo contrato de `/api/v1`.

O shell é Capacitor 7 e carrega o mesmo bundle web (`uiFork: false`, `apiBase: /api/v1`). Esquema igual ao desktop: `vibechat://{host}/v1/{channel|thread|workspace}/{guid}`. Host fora de `Mobile:AllowedHosts` → 400 `MobileDeepLinkRejected`. Share exige `confirm: true` e devolve `sendsMessage: false`; `tenantId`, `text` ou token no body → 400 `MobileTokenRejected`. O share não cria mensagem.

Push nativo não usa FCM nem APNs no servidor. `POST /mobile/push/preview` aceita só `{ kind: message|mention, conversationId }` e responde `containsContent: false`. Campo de texto ou token de push → 400 `MobileTokenRejected`.

`Mobile:StoreCredentialRef` vazio mantém `distribution: sideload`. Referência segura (`operator/mobile-store`) marca `store: ready`. PEM, `token`, `secret` ou `private` → `store: rejected` e o valor não volta na resposta. Sem conta de loja, `apps/mobile/sideload.json` continua sendo o artefato. O hash de `apps/mobile/shell.lock.json` cobre `android-arm64` e `ios-arm64`.

Registro de aparelho não aceita token. A tabela `identity.mobile_devices` não tem coluna de token. Biometria, câmera, seletor de arquivo e notificação nascem desligados; biometria só liga com `true` explícito do dono. Logout remoto marca `RevokedAt` e pede `wipeSecureStorage: true`. Dono ou `workspace.admin`. Outro membro → 403. Outro tenant → 404.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/mobile/shell` | `message.read` |
| POST | `/api/v1/mobile/deep-links/validate` | `message.read`; body `{ url }` |
| POST | `/api/v1/mobile/share/validate` | `message.read`; `{ host, kind, id, confirm: true }`; não envia |
| POST | `/api/v1/mobile/push/preview` | `message.read`; só ids |
| GET | `/api/v1/mobile/devices` | `message.read`; só os do caller |
| POST | `/api/v1/mobile/devices` | `message.read`; `{ platform, appVersion }` + `Idempotency-Key` |
| PUT | `/api/v1/mobile/devices/{deviceId}/preferences` | `message.read`; dono; `{ biometricLock, notifications, camera, filePicker }` |
| DELETE | `/api/v1/mobile/devices/{deviceId}` | `message.read`; dono ou `workspace.admin` |

RLS `FORCE` em `identity.mobile_devices`. O `Down` remove só essa tabela. Auditoria `mobile.device.register` / `mobile.device.preferences` / `mobile.device.logout` com metadata `{}`.

### Sync offline (B-143 / ADR-059)

Flag `Features:OfflineWrite:Enabled` default **false**. Off, ou `CursorKey` vazia/`CHANGE_ME`/curta, → 404 `OfflineWriteDisabled`. O envio online não muda.

`GET /api/v1/sync?deviceId={guid}&cursor=` exige `message.read`. `deviceId` vazio → 400 `InvalidDevice`.

Resposta: `protocolVersion` (1), `serverTime`, `cursor` opaco, `changes[]`, `tombstones[]`, `hasMore`, `rebuild.reason` ou null, `authorizedConversationIds[]`, `conversationCoverage` (`complete`|`partial`).

Item: `kind` (`message`|`task`), `conversationId`, `channelId`, `id`, `seq`, `version`, `body`, `reason`. Tombstone de mensagem omite `body` e usa `reason=deleted`. A janela tem no máximo 50 itens. Não substitui `GET .../messages`.

| Motivo | Quando |
|--------|--------|
| `CursorExpired` | Cursor com mais de 7 dias. Snapshot recente. A fila local ainda não confirmada permanece |
| `FullResyncRequired` | Assinatura, tenant, usuário ou `deviceId` não batem. Só dados do caller |
| `ScopeRevoked` | Principal sem membership nenhuma. Sem corpos |
| `ProtocolUpgradeRequired` | Versão do cursor ≠ 1. **409**, sem corpos |

`PUT .../messages/{id}` aceita `expectedVersion` opcional (ms de `editedAt`, ou 0). Diferente → **409** `MessageVersionConflict` com `currentVersion` e `currentBody`, sem gravar. Ausente: comportamento anterior.

`PUT .../reactions` aceita `intent` opcional `add`|`remove`. Já no estado pedido → 200 sem novo evento. Outro valor → 400 `InvalidReactionIntent`. Ausente: toggle atual.

Não há migration. A chave do cursor não entra em log.

### Lifecycle de storage (B-145 / ADR-060)

Flag `Features:StorageLifecycle:Enabled` default **false**. Off → 404 `StorageLifecycleDisabled`. O upload não consulta este ledger. A quota genérica de B-134 continua no caminho dela.

| Método | Caminho | AuthZ |
|--------|---------|-------|
| GET | `/api/v1/workspaces/{workspaceId}/storage` | `workspace.admin` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/limits` | `workspace.admin`; `{ kind, softLimit, hardLimit }` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/reservations` | `workspace.admin`; `{ kind, bytes }` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/objects` | `workspace.admin`; sem devolver `storageKey` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/reconcile` | `workspace.admin`; `Idempotency-Key` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/gc` | `workspace.admin`; `{ dryRun }`; `Idempotency-Key` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/restore` | `workspace.admin`; `{ objectId }`; `Idempotency-Key` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/objects/{objectId}/archive` | `workspace.admin` |
| POST | `/api/v1/workspaces/{workspaceId}/storage/objects/{objectId}/purge` | `workspace.admin`; `Idempotency-Key` |
| GET | `/api/v1/workspaces/{workspaceId}/storage/objects/{objectId}/cdn` | `message.read` |
| GET | `/api/v1/storage/cdn?token=` | `message.read` |

`kind`: `attachment`, `preview`, `export`, `recording`. Estouro → 409 `StorageQuotaHardLimit` antes da URL de upload. Hold → 409 `StorageHoldActive`. Referência → 409 `StorageReferenced`. Graça → 409 `StorageGracePending`. Tombstone → 409 `StorageAlreadyPurged`. CDN off ou chave inválida → 404 `StorageCdnDisabled`. Token vencido → 410 `StorageCdnExpired`.

A resposta de CDN é `{ url, expiresAt }`. `url` não contém a object key. O evento `storage.lifecycle.changed` leva só id, tipo, estado e versão.

RLS `FORCE` nas quatro tabelas `files.storage_*`. O `Down` remove só essas tabelas.

### Backup de chat (B-172 / ADR-061)

Flag `Features:ChatBackup:Enabled` default **false**. Off → 404 `ChatBackupDisabled`. O envio de mensagem não consulta esta flag. `DriveEnabled` default **false**: sem endpoint local a rota de Drive não sai da instalação.

| Método | Caminho | AuthZ |
|--------|---------|-------|
| GET | `/api/v1/workspaces/{workspaceId}/chat-backup` | `workspace.admin` |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/destinations` | `workspace.admin`; `Idempotency-Key`; segredo só na escrita |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/destinations/{destinationId}/test` | `workspace.admin` |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/destinations/{destinationId}/rotate` | `workspace.admin`; `Idempotency-Key` |
| DELETE | `/api/v1/workspaces/{workspaceId}/chat-backup/destinations/{destinationId}` | `workspace.admin`; 409 `ChatBackupDestinationInUse` se houver tarefa |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/jobs` | `workspace.admin`; `Idempotency-Key` |
| PATCH | `/api/v1/workspaces/{workspaceId}/chat-backup/jobs/{jobId}` | `workspace.admin` |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/jobs/{jobId}/run` | `workspace.admin`; `Idempotency-Key` |
| GET | `/api/v1/workspaces/{workspaceId}/chat-backup/export` | `workspace.admin`; ZIP `vibechat.chat.backup.v1` |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/import/validate` | `workspace.admin`; corpo ZIP; não grava |
| POST | `/api/v1/workspaces/{workspaceId}/chat-backup/import/execute?confirm=true` | `workspace.admin`; `Idempotency-Key`; sem `confirm` → 400 `ChatBackupConfirmationRequired` |

Formato `vibechat.chat.backup.v1`: `manifest.json` com checksum SHA-256 de cada entrada e `payload/messages.json`. Anexos opcionais em `objects/{id}`. Checksum divergente → 400 `ChatBackupChecksumMismatch`. Caminho com `..` → 400 `ChatBackupUnsafePath`.

A resposta de destino traz `configured` e `secretMask`. Nunca o segredo. Falha de destino grava run `failed` e responde 200 com o run; a API continua de pé. Três falhas abrem o circuito (`ChatBackupCircuitOpen`).

Com `contentAuditEnabled=false`, o pacote não leva corpo nem bytes de anexo. Import de id que já existe em outro tenant → 403 `ChatBackupCrossTenant`.

RLS `FORCE` nas três tabelas `administration.chat_backup_*`. O `Down` remove só essas tabelas. Audit: `chat.backup.destination.create`, `chat.backup.destination.rotate`, `chat.backup.destination.delete`, `chat.backup.job.create`, `chat.backup.job.update`, `chat.backup.run`, `chat.backup.export`, `chat.backup.import`.

### HA e rolling upgrade (B-144 / ADR-062)

Flag `Ha:Enabled` default **false**. Off → 404 `HaDisabled` em `GET /api/v1/meta/runtime` e `POST /api/v1/admin/ha/drain`. Envio e histórico não consultam a flag.

| Método | Caminho | AuthZ |
|--------|---------|-------|
| GET | `/api/v1/meta/runtime` | `support.read`; fase, revisão, RPO/RTO, `draining`; sem senha e sem body |
| POST | `/api/v1/admin/ha/drain` | `support.repair`; drena só o processo local |

Readiness (`/health/ready`, `/ready`) responde 503 enquanto o processo drena. `/health/live` permanece 200. O cliente recebe `NodeDraining` e, se o buffer estourar, `ResyncRequired`. Nenhum dos dois leva connection string. Não há tabela nova.

### Templates de workspace (B-115)

Manifesto declarativo `vibechat.workspace-template.v1`. Não carrega `tenantId`, membros, e-mails, tokens nem secrets. Recursos criados recebem o tenant do contexto. Só `workspace.admin` (Auditor/membro → 403).

Campos: `schema`, `id`, `version` (inteiro ≥ 1), `spaces[]` (`key`, `name`, `channels[]` com `key`, `name`, `type`, `topic?`), `policyDefaults?`, `checklist?`. `type` permitido: `Public`, `Private`, `Announcement`, `Group`. Campo ou schema desconhecido → 400 `UnknownTemplateField` (com `path`) ou `UnknownTemplateSchema`. Corpo inválido → 400 `InvalidTemplate`.

`policyDefaults` é a política de edição/apagar já existente (B-107). Não grava credencial, webhook nem flag. Se a política do tenant já diverge, o apply responde 409 `TemplateConflict` e não persiste.

Built-ins versionados no binário: `team`, `project`, `community`, `incidents`. Importar um desses ids → 409 `TemplateReserved`. Acima de 20 templates customizados → 409 `TemplateLimitReached`.

| Método | Caminho | AuthZ |
|--------|---------|--------|
| GET | `/api/v1/admin/workspaces/{workspaceId}/templates` | `workspace.admin`; built-ins + custom do workspace |
| POST | `/api/v1/admin/workspaces/{workspaceId}/templates/validate` | corpo = manifesto |
| POST | `/api/v1/admin/workspaces/{workspaceId}/templates/preview` | `{ templateId }` **ou** `{ manifest }`; não persiste |
| POST | `/api/v1/admin/workspaces/{workspaceId}/templates/apply` | o mesmo, mais `dryRun?`; sem dry-run exige `Idempotency-Key` |
| POST | `/api/v1/admin/workspaces/{workspaceId}/templates/import` | corpo = manifesto; sem dados pessoais |
| GET | `/api/v1/admin/workspaces/{workspaceId}/templates/export` | estrutura atual, sem DM/membros/secrets |
| GET | `/api/v1/admin/workspaces/{workspaceId}/templates/{templateId}/export` | built-in ou custom deste workspace |
| GET | `/api/v1/admin/workspaces/{workspaceId}/onboarding` | checklist; ausência de linha = `pending` |
| PUT | `/api/v1/admin/workspaces/{workspaceId}/onboarding` | `{ status?, items? }`; `skipped` / `in_progress` / `completed` |

Preview e `dryRun: true` não gravam. Retry do mesmo template reutiliza space/canal compatível (nome no workspace). Conflito de tipo, space ou política → 409 sem gravar. A mesma `Idempotency-Key` devolve o resultado anterior (`idempotent: true`). Audit por recurso criado (`space.create`, `channel.create`) mais `template.apply`, `template.import`, `template.export` e `onboarding.update`.

Tabelas `directory.workspace_templates`, `directory.workspace_onboarding`, `directory.template_applications`. RLS FORCE. O wizard em `/admin/onboarding` pode ser pulado e retomado; não bloqueia o chat.

### Importação assistida (B-153 / ADR-030)

Formato canônico `vibechat.import.v1`. Adapters `vibechat`, `slack`, `mattermost` e `discord` só traduzem o arquivo; não chamam a origem. Flag `Features:Import:Enabled` default false → 404 `ImportDisabled`. Exige `workspace.import` e `Idempotency-Key` na criação. `tenantId` no arquivo é ignorado.

| Método | Caminho | Efeito |
|--------|---------|--------|
| POST | `/api/v1/workspaces/{workspaceId}/imports` | Valida e grava o job. Não cria canal nem mensagem |
| POST | `.../imports/{importId}/plan` | Dry-run. Conflito de tipo de canal bloqueia a execução |
| POST | `.../imports/{importId}/execute` | Staging em `import.id_map`. Repetir não duplica |
| POST | `.../imports/{importId}/pause` / `resume` | Pausa antes de publicar |
| POST | `.../imports/{importId}/publish` | Cria spaces, canais, mensagens, threads e anexos limpos na transação do request |
| POST | `.../imports/{importId}/rollback` | Antes de publicar, apaga staging. Depois, exige `{ "confirm": true }` |
| GET | `.../imports` e `.../imports/{importId}` | Resumo. Sem JSON canônico |
| GET | `.../imports/{importId}/report` | Contagens e avisos. Sem body, e-mail ou payload |

Papel fora de Member/Moderator/Auditor/Admin → 422 `ImportRoleForbidden`. Autor sem `mappedUserId` vira perfil histórico sem membership. Anexo hostil fica em quarentena e não vai ao MinIO. Publicação não emite `MessageCreated`. Tabelas `import.jobs`, `import.id_map`, `import.historical_principals` com RLS FORCE. Wizard em `/admin/import`.

### Diagnóstico e support bundle (B-154)

Contrato `vibechat.support-bundle.v1` (`SupportBundleComposer`). Flag `Features:SupportBundle:Enabled` default false: preflight, probe e repair seguem; criar bundle → 404 `SupportBundleDisabled`. Development e TestHost ligam a flag.

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/workspaces/{workspaceId}/diagnostics` | `support.read` (Admin e Auditor) |
| POST | `.../diagnostics/probes/{kind}` | `support.repair`; `kind` = `email`, `push` ou `storage` |
| POST | `.../diagnostics/bundles` | `support.bundle` + `Idempotency-Key` |
| GET | `.../diagnostics/bundles/{bundleId}` | `support.bundle`; manifesto JSON; TTL 15 min e no máximo 3 downloads |
| POST | `.../diagnostics/repairs` | `support.repair` + `Idempotency-Key` |
| POST | `.../repairs/{repairId}/apply` | `support.repair` |
| POST | `.../repairs/{repairId}/cancel` | `support.repair` |

`PlatformOwner` vê evidência de endpoint. Admin de workspace vê o mesmo status com evidência mascarada. Member → 403. Feature desligada é `Skipped` / `feature.off`, não `Fail`. Probe de e-mail e push não envia para a rede; storage grava um objeto sintético e apaga. Repair allowlist: `search.reindex` e `membership.reconcile`. `target` ou ação fora da lista → 400 `RepairActionNotAllowed`. Dry-run não altera `search_vector`. Apply de reindex só reescreve a projeção. Bundle não inclui body, e-mail, token nem connection string; checksum `sha256` e scrub allowlist. Tabelas `support.bundles` e `support.repair_jobs` com RLS FORCE. Página `/admin/diagnostics`. Geração termina no request porque o manifesto é limitado e não faz fan-out; o evento `support.bundle.created` sai pelo outbox.

### Auditoria de conversa (B-067)

Distinta do feed `audit_events` (B-042). Viewer compliance: admin/Auditor com `admin.dashboard` lê histórico completo **dentro do tenant**, inclusive DMs onde não é membro e corpos soft-deleted (ADR-018), enquanto `contentAuditEnabled=true`. Com a flag desligada, o body sai vazio (`bodyRedacted`). Membro comum → 403. Canal/thread de outro tenant → 403. Histórico normal (`GET /channels/.../messages`) continua redigindo body deletado e exigindo membership, nos dois modos.

Provisionamento (B-068): no primeiro login OIDC, `EnsureProfile` vincula stub `pending:{email}` ao `sub` real — a membership já provisionada pelo admin passa a valer sem self-signup.

SCIM 2.0 (B-128 / ADR-039): flag `Identity:Scim:Enabled` default false. Protocolo em `/scim/v2` (Users, Groups, Bulk ≤ 20, filtro `eq`, página `startIndex`/`count` ≤ 100). Bearer `vc_scim_` só como SHA-256; lista admin não devolve segredo. `externalId` único por tenant é a idempotência. O mesmo `IProvisioningPort` atende o SCIM e `POST /api/v1/admin/workspaces/{workspaceId}/scim/provisioning/users`. Mapeamento não concede Admin, WorkspaceOwner, PlatformOwner nem Bot. Desativar remove memberships do tenant, marca saída de canal e grava `identity.access_revocations` sem apagar mensagem. `/me` de quem ficou sem membership responde 401 `SessionRevoked`. Flag off → 404 `ScimDisabled`. `Identity:Scim:KeycloakAdapter` default false não bloqueia o comando.

SSO corporativo (B-164 / ADR-038): flag `Identity:Sso:Enabled` default false. Ligada, claims `broker_issuer` + `broker_subject` gravam `identity.external_identities` (`issuer`, `subject`, `user_id`, `reason`, `provider_alias`). E-mail não vincula perfil que não seja `pending:`. Par já ligado a outro `sub` → 409 `IdentityLinkConflict`. Primeiro login sem convite cria perfil sem membership. `GET /api/v1/auth/sso-providers` é a primeira rota do grupo `/api/v1`: herda `RequireAuthorization` do grupo e declara `AllowAnonymous` mais a isenção `anonymous SSO directory (B-164)`. Sem permissão de produto. Flag off → 404 `SsoDisabled`; cada item traz `alias`, `displayName` e `domains` (sem segredo). O cliente manda `kc_idp_hint` com o alias. JWT com `iss` diferente do Authority do Keycloak é recusado.

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

`DigestEnabled` continua o interruptor da agenda. O envio do resumo é B-122
(ADR-033): snapshot de ids, não o corpo das pushes suprimidas no DND.

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `GET /api/v1/notifications/preferences` | `message.read` | Preferência global do actor + `channelOverrides[]` |
| `PUT /api/v1/notifications/preferences` | `message.read` | Upsert; valida `TimeZone` quando `DndEnabled=true`; `PriorityContactUserIds` filtrado a membros do tenant |
| `PUT /api/v1/notifications/preferences/channels/{channelId}` | `message.read` | `{ level, duration? }`; canal de outro tenant → 403 |
| `DELETE /api/v1/notifications/preferences/channels/{channelId}` | `message.read` | Remove override (volta ao padrão); canal de outro tenant → 403 |

### Digests e catch-up (B-122)

Flag `Notifications:Digests:Enabled` default **false**. Off → 404
`DigestDisabled`. Tabelas `notifications.digest_schedules`, `digest_runs`,
`digest_items` com RLS `FORCE`. Itens guardam ids; a leitura revalida membership
e omite mensagem apagada ou canal revogado. Não atualiza `read_cursors`.
Entrega externa é só contagem. `digest.generated` (audit + outbox) não leva body.
IA só com `SemanticSearch:Enabled` e opt-in do workspace; falha mantém a lista.

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `GET /api/v1/notifications/digests/settings` | `message.read` | Agenda do actor. 404 se a flag está off |
| `PUT /api/v1/notifications/digests/settings` | `message.read` | `{ enabled, cadence, localTime, weekday, timeZone, delivery, includeAi, channelIds }`. 400 `InvalidDigestSettings` |
| `POST /api/v1/workspaces/{workspaceId}/digests/catch-up` | `message.read` | Header `Idempotency-Key`. Mesma chave devolve o mesmo run e não reenvia |
| `GET /api/v1/workspaces/{workspaceId}/digests/latest` | `message.read` | Último run do actor naquele workspace |
| `GET /api/v1/workspaces/{workspaceId}/digests/{digestId}` | `message.read` | 404 se for de outro usuário. Outro tenant → 403 |

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

### Status personalizado e disponibilidade (B-116)

Uma linha por `(TenantId, UserId)` em `identity.user_statuses` (RLS, FORCE):
`State` (`focus`|`meeting`|`vacation`|`custom`), `Emoji` (máx. 16), `Text` (máx. 80),
`ClearAtEndOfDay`, `ExpiresAt?`, `UpdatedAt`. O usuário altera só o próprio.
`ExpiresAt` no passado some na leitura (como o mute de B-097) e o cliente também
esconde pelo `expiresAt`. “Limpar ao fim do dia” grava o instante UTC da meia-noite
seguinte no fuso IANA das preferências de notificação (fallback UTC).

Disponibilidade é derivada, não armazenada: DND ativo → `busy` (e continua
suprimindo push; status não fura o DND — a exceção segue o contato prioritário de
B-097); senão férias → `vacation`; foco/reunião → `busy`; senão presence
`online`/`away`/`offline` → `available`/`away`/`offline`. A resposta não inclui
janela de DND nem eventos de agenda.

Evento SignalR `UserStatusChanged` no grupo `t:{tenantId}`:
`{ tenantId, userId, presence, availability, status? }`. Sem dado de agenda.

| Endpoint | AuthZ | Notas |
|----------|-------|-------|
| `GET /api/v1/me/status` | `message.read` | Próprio; `status` null se vazio ou expirado |
| `PUT /api/v1/me/status` | `message.read` | Upsert. Body `{ state, emoji?, text?, clearAtEndOfDay?, expiresAt? }` |
| `DELETE /api/v1/me/status` | `message.read` | Limpa o próprio |
| `GET /api/v1/workspaces/{workspaceId}/availability` | membership + `message.read` | Membros do workspace. Outsider → 403 |
| `DELETE /api/v1/workspaces/{workspaceId}/members/{userId}/status` | `workspace.admin` | Clear de abuso. Audit `user_status.clear` sem o texto |
| `POST /api/v1/workspaces/{workspaceId}/members/{userId}/status/report` | `message.read` | Denúncia. Audit `user_status.report` sem o texto. Não denuncia a si |
| `GET /api/v1/me/availability/calendar` | `message.read` | Flag `Features:AvailabilityCalendar:Enabled` default **false** → 404 `CalendarIntegrationDisabled`. Ligada: `{ enabled: true, connected: false }` e nenhum evento |

O conteúdo do status é controlado pelo usuário (PII possível). Tamanho limitado;
admin limpa abuso. Esta fatia não sincroniza calendário.

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
