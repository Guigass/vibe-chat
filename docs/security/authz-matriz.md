# Matriz de cobertura authZ — API e Hub

> B-175 / W7-11. Fonte de verdade para revisão de gates.
> Autorização de produto vem de `workspace_members.role` + `RolePermissionCatalog`
> (não de claims JWT do Keycloak — ver B-176).

Papéis resumidos: **M** = Member · **A** = Auditor · **Ad** = Admin/Owner.
Guest fora de escopo (B-040). Bot fora desta matriz.

Gates:

| Gate | Significado |
|------|-------------|
| `membership` | Só membership workspace/canal (via `ResolveWorkspaceAsync` / `ResolveChannelAsync` / hub `CanAccess`) |
| `permission` | Filtro `RequirePermission` e/ou `HasPermissionAsync` no handler |
| `condicional` | Own vs any / autor vs admin — marcado `AllowPermissionGateExempt` |
| `exempt` | Lab-only ou membership intencional sem permissão tipada |

Nota: `GET /health` (fora de `/api/v1`) é anônimo e **não** está nesta matriz;
não confundir com `GET /api/v1/admin/health-summary`.

Paths abaixo usam o template literal de `apps/api/Program.cs` (inclui `:guid`)
para o gate de drift CI.

## API `/api/v1`

| Método | Path | Gate | Permissão(ões) | M | A | Ad | Notas |
|--------|------|------|----------------|---|---|----|-------|
| GET | `/me` | membership | — | ✓ | ✓ | ✓ | Perfil do caller |
| GET | `/workspaces` | membership | — | ✓ | ✓ | ✓ | Lista só workspaces do caller |
| GET | `/workspaces/{workspaceId:guid}/channels` | membership | — | ✓ | ✓ | ✓ | Roster de canais |
| GET | `/workspaces/{workspaceId:guid}/channels/unread` | membership | — | ✓ | ✓ | ✓ | Contagens do caller |
| GET | `/workspaces/{workspaceId:guid}/spaces` | membership | — | ✓ | ✓ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/spaces` | permission | `channel.create` | ✓ | ✗ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/members` | membership | — | ✓ | ✓ | ✓ | Directory; Guest restringe em B-040 |
| GET | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members` | membership | — | ✓ | ✓ | ✓ | Autocomplete menções |
| GET | `/workspaces/{workspaceId:guid}/roles` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Catálogo assignable |
| POST | `/workspaces/{workspaceId:guid}/members` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Invite B-068 |
| PUT | `/workspaces/{workspaceId:guid}/members/{userId:guid}/role` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/presence` | membership | — | ✓ | ✓ | ✓ | Presence não é conteúdo de mensagem |
| POST | `/workspaces/{workspaceId:guid}/dms` | exempt | membership-only | ✓ | ✓ | ✓ | Abrir DM (B-021); `AllowPermissionGateExempt` |
| POST | `/workspaces/{workspaceId:guid}/channels` | permission | `channel.create` | ✓ | ✗ | ✓ | |
| PUT | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/topic` | permission | `channel.create` | ✓ | ✗ | ✓ | Slash `/topico` |
| GET | `/workspaces/{workspaceId:guid}/commands` | membership | (filtra por perm) | ✓ | ✓ | ✓ | Discovery; itens filtrados por permissão |
| GET | `/channels/{channelId:guid}/messages` | membership | — | ✓ | ✓ | ✓ | Timeline; membership-only justificado (leitura de canal) |
| POST | `/channels/{channelId:guid}/messages` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/messages/{messageId:guid}/forward` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/threads` | permission | `message.send` | ✓ | ✗ | ✓ | Get-or-create + reply |
| GET | `/threads/{threadId:guid}` | membership | — | ✓ | ✓ | ✓ | |
| GET | `/threads/{threadId:guid}/messages` | membership | — | ✓ | ✓ | ✓ | |
| POST | `/threads/{threadId:guid}/messages` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/attachments` | permission | `file.upload` + `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete` | permission | `file.upload` | ✓ | ✗ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/download` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/thumbnail` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| PUT | `/channels/{channelId:guid}/messages/{messageId:guid}` | condicional | `message.edit.own` | ✓* | ✗ | ✓* | *só autor; exempt B-023 |
| PUT | `/channels/{channelId:guid}/messages/{messageId:guid}/reactions` | permission | `message.react` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/pin` | permission | `message.pin` | ✓ | ✗ | ✓ | |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}/pin` | permission | `message.pin` | ✓ | ✗ | ✓ | |
| GET | `/channels/{channelId:guid}/pins` | permission | `message.read` | ✓ | ✓ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/saved` | permission | `message.read` | ✓ | ✓ | ✓ | |
| PATCH | `/workspaces/{workspaceId:guid}/saved/{messageId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | |
| DELETE | `/workspaces/{workspaceId:guid}/saved/{messageId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/saved` | permission | `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/reactions/{emoji}/users` | permission | `message.react` | ✓ | ✗ | ✓ | |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}` | condicional | `message.delete.own` / `message.delete.any` | ✓* | ✗ | ✓ | *autor ou DeleteAny; exempt B-023 |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}/link-preview` | condicional | autor ou `workspace.admin` | ✓* | ✗ | ✓ | exempt B-091 |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/link-preview/image` | membership | — | ✓ | ✓ | ✓ | Proxy de imagem já SSRF-guarded |
| PUT | `/channels/{channelId:guid}/read-cursor` | permission | `message.read` | ✓ | ✓ | ✓ | |
| GET | `/search/messages` | permission | `search.messages` + `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/unread-count` | membership | — | ✓ | ✓ | ✓ | Contagem do caller |
| GET | `/notifications/push/public-key` | permission | `message.read` | ✓ | ✓ | ✓ | `{ enabled, publicKey? }`; off sem erro |
| GET | `/notifications/push/subscriptions` | permission | `message.read` | ✓ | ✓ | ✓ | Só o actor |
| POST | `/notifications/push/subscriptions` | permission | `message.read` | ✓ | ✓ | ✓ | Upsert do actor |
| DELETE | `/notifications/push/subscriptions/{id:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | 404 se não for do actor |
| GET | `/admin/dashboard` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado |
| GET | `/admin/audit-events` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/conversations` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/conversations/{channelId:guid}/messages` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Bypass membership de canal |
| GET | `/admin/threads/{threadId:guid}/messages` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/settings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| PUT | `/admin/settings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/openrouter/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/smtp/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/webhook/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/encryption/reencrypt` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/admin/workspaces/{workspaceId:guid}/export` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/admin/health-summary` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado; distinto de `/health` |
| GET | `/admin/version` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize` | permission | `ai.summarize` | ✓ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply` | permission | `ai.suggest_reply` | ✓ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/attachments/{attachmentId:guid}/transcribe` | permission | `ai.transcribe` | ✓ | ✗ | ✓ | |
| POST | `/dev/seed` | exempt | AllowAnonymous lab | — | — | — | Só Development |

## Hub SignalR `/hubs/chat`

| Método | Gate | Permissão | M | A | Ad | Notas |
|--------|------|-----------|---|---|----|-------|
| `JoinChannel` | membership | — | ✓ | ✓ | ✓ | Inscrição de leitura |
| `LeaveChannel` | auth | — | ✓ | ✓ | ✓ | Remove do grupo; sem revalidar (idempotente) |
| `Heartbeat` | membership | tenant | ✓ | ✓ | ✓ | Presence |
| `SetAway` | membership | tenant | ✓ | ✓ | ✓ | Presence |
| `SendTyping` | permission | `message.send` + canal | ✓ | ✗ | ✓ | Gap B-175: Auditor sem send não digita |

## Manutenção

- Novo endpoint `/api/v1` → linha nesta matriz no mesmo PR (path = template de `Program.cs`).
- Mutações: `RequirePermission` ou `AllowPermissionGateExempt` (gate CI B-174).
- Arch test `Api_v1_maps_are_listed_in_authz_matriz` falha se o path sumir daqui.
- Paths `/admin/*` no filtro `RequirePermission` resolvem tenant via membership admin
  (não via `ResolveChannelAsync`) para preservar o bypass de `channel_members` da
  auditoria de conversa (B-067 / B-175).
