# Matriz de cobertura authZ — API e Hub

> B-175 / W7-11. Fonte de verdade para revisão de gates.
> Autorização de produto vem de `workspace_members.role` + `RolePermissionCatalog`
> (não de claims JWT do Keycloak — ver B-176).

Papéis resumidos: **M** = Member · **A** = Auditor · **Ad** = Admin/Owner.
Guest (B-040) não tem membership de workspace — só `ChannelMember` no canal do convite.
Coluna Guest: workspace → 403; canal convidado → send/react/upload. Bot fora desta matriz.

Gates:

| Gate | Significado |
|------|-------------|
| `membership` | Só membership workspace/canal (via `ResolveWorkspaceAsync` / `ResolveChannelAsync` / hub `CanAccess`) |
| `permission` | Filtro `RequirePermission` e/ou `HasPermissionAsync` no handler |
| `condicional` | Own vs any / autor vs admin — marcado `AllowPermissionGateExempt` |
| `exempt` | Lab-only ou membership intencional sem permissão tipada |

Nota: `GET /health` (fora de `/api/v1`) é anônimo e **não** está nesta matriz;
não confundir com `GET /api/v1/admin/health-summary`.

Paths abaixo usam os templates literais de `apps/api/Endpoints/*.cs` e
`apps/api/GroupDmEndpoints.cs` (incluem `:guid`). Os gates offline percorrem
os arquivos C# de `apps/api` recursivamente, excluindo `bin`/`obj`, e recusam
inventário vazio. A matriz é conferida por método HTTP + path.

## API `/api/v1`

| Método | Path | Gate | Permissão(ões) | M | A | Ad | Notas |
|--------|------|------|----------------|---|---|----|-------|
| GET | `/me` | membership | — | ✓ | ✓ | ✓ | Perfil do caller |
| PUT | `/me` | exempt | caller-only | ✓ | ✓ | ✓ | Só o próprio `locale`; exceção explícita, sem mudar authZ; `InvalidLocale` → 400 |
| PUT | `/users/{userId:guid}/appearance` | exempt | caller-only | ✓ | ✓ | ✓ | Wallpaper/accent do próprio usuário (B-185); outro `userId` ou cross-tenant → 403; id fora do catálogo → 400 |
| PUT | `/me/profile` | exempt | caller-only | ✓ | ✓ | ✓ | B-167; só o próprio; sem membership de workspace → 403; admin não edita alheio |
| POST | `/me/profile/avatar` | exempt | caller-only | ✓ | ✓ | ✓ | B-167; imagem allowlist + tamanho; key namespaced por tenant; `DisableAntiforgery` (auth por header, não cookie) |
| DELETE | `/me/profile/avatar` | exempt | caller-only | ✓ | ✓ | ✓ | B-167; volta às iniciais |
| GET | `/workspaces/{workspaceId:guid}/members/{userId:guid}/profile` | membership | — | ✓ | ✓ | ✓ | B-167; mesmo workspace; outro workspace/guest → 403; alvo fora do workspace → 404 |
| GET | `/workspaces/{workspaceId:guid}/members/{userId:guid}/profile/avatar` | membership | — | ✓ | ✓ | ✓ | B-167; bytes só com membership; key fora do prefixo do tenant → 404 |
| GET | `/workspaces` | membership | — | ✓ | ✓ | ✓ | Lista só workspaces do caller |
| GET | `/workspaces/{workspaceId:guid}/channels` | membership | — | ✓ | ✓ | ✓ | Roster de canais |
| GET | `/workspaces/{workspaceId:guid}/channels/unread` | membership | — | ✓ | ✓ | ✓ | Contagens do caller |
| GET | `/workspaces/{workspaceId:guid}/spaces` | membership | — | ✓ | ✓ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/spaces` | permission | `channel.create` | ✓ | ✗ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/members` | membership | — | ✓ | ✓ | ✓ | Directory; Guest 403 (sem workspace membership) |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-040; flag `Directory:Invites:Enabled` off → 404; token só na criação |
| GET | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-040; lista sem token + guests ativos |
| DELETE | `/invites/{inviteId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-040; revoga + evict hub |
| POST | `/invites/{token}/accept` | exempt | authenticated | ✓* | ✓* | ✓* | *qualquer identidade autenticada; token inválido/gasto/expirado → 410 genérico |
| GET | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members` | membership | — | ✓ | ✓ | ✓ | `?query=` autocomplete (máx. 8); sem query, roster paginado (B-186) |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members` | exempt | criador ou `channel.manage` | ✓* | ✗ | ✓ | *só o criador se for Member; canal privado; alvo do mesmo workspace |
| DELETE | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/me` | exempt | membership | ✓ | ✓ | ✓ | Sair do privado; último gestor → 409 `LastChannelManager` |
| DELETE | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/{userId:guid}` | exempt | criador ou `channel.manage` | ✓* | ✗ | ✓ | Remove outro; não remove o último gestor |
| GET | `/workspaces/{workspaceId:guid}/roles` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Catálogo assignable |
| POST | `/workspaces/{workspaceId:guid}/members` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Invite B-068 |
| PUT | `/workspaces/{workspaceId:guid}/members/{userId:guid}/role` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/contact-groups` | membership | — | ✓ | ✓ | ✓ | B-166; departamentos de todos + pessoais só do dono |
| POST | `/workspaces/{workspaceId:guid}/contact-groups` | exempt | `department` exige `workspace.admin`; `personal` é o caller | ✓* | ✗ | ✓ | *só `personal`; departamento → 403 |
| PATCH | `/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}` | exempt | dono ou admin do departamento | ✓* | ✗ | ✓ | *pessoal próprio; pessoal alheio → 404; departamento sem admin → 403 |
| DELETE | `/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}` | exempt | dono ou admin do departamento | ✓* | ✗ | ✓ | *pessoal próprio |
| PUT | `/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}/members` | exempt | dono ou admin do departamento | ✓* | ✗ | ✓ | *pessoal próprio; alvo fora do workspace → 400 `InvalidMembers` |
| GET | `/workspaces/{workspaceId:guid}/contacts` | membership | — | ✓ | ✓ | ✓ | B-166; `grouped=true` (default); seções + sem grupo |
| GET | `/workspaces/{workspaceId:guid}/presence` | membership | — | ✓ | ✓ | ✓ | Presence não é conteúdo de mensagem |
| GET | `/me/status` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; status do próprio |
| PUT | `/me/status` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; só o próprio; upsert |
| DELETE | `/me/status` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; limpa o próprio |
| GET | `/workspaces/{workspaceId:guid}/availability` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; membros do workspace; sem agenda |
| DELETE | `/workspaces/{workspaceId:guid}/members/{userId:guid}/status` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-116; clear de abuso com audit, sem texto |
| POST | `/workspaces/{workspaceId:guid}/members/{userId:guid}/status/report` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; denúncia sem copiar o texto |
| GET | `/me/availability/calendar` | permission | `message.read` | ✓ | ✓ | ✓ | B-116; flag off → 404; sem eventos |
| POST | `/workspaces/{workspaceId:guid}/dms` | exempt | membership-only | ✓ | ✓ | ✓ | Abrir DM (B-021); `AllowPermissionGateExempt` |
| POST | `/workspaces/{workspaceId:guid}/group-dms` | exempt | membership-only | ✓ | ✓ | ✓ | Group DM habilitado; participantes validados no workspace |
| POST | `/channels/{channelId:guid}/participants` | exempt | membership-only | ✓ | ✓ | ✓ | Participante atual adiciona membros do workspace |
| DELETE | `/channels/{channelId:guid}/participants/me` | exempt | membership-only | ✓ | ✓ | ✓ | Sai apenas o caller |
| PATCH | `/channels/{channelId:guid}` | exempt | membership-only | ✓ | ✓ | ✓ | Participante renomeia a DM em grupo |
| POST | `/workspaces/{workspaceId:guid}/channels` | permission | `channel.create` | ✓ | ✗ | ✓ | |
| PUT | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/topic` | permission | `channel.create` | ✓ | ✗ | ✓ | Slash `/topico` |
| GET | `/workspaces/{workspaceId:guid}/commands` | membership | (filtra por perm) | ✓ | ✓ | ✓ | Discovery; itens filtrados por permissão |
| GET | `/channels/{channelId:guid}/messages` | membership | — | ✓ | ✓ | ✓ | Timeline; membership-only justificado (leitura de canal) |
| POST | `/channels/{channelId:guid}/messages` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/polls` | permission | `message.send` + membership | ✓ | ✗ | ✓ | B-096; não DM/thread |
| POST | `/polls/{pollId:guid}/votes` | permission | `message.send` + membership | ✓ | ✗ | ✓ | Fechada → 409; outro tenant → 403 |
| DELETE | `/polls/{pollId:guid}/votes` | permission | `message.send` + membership | ✓ | ✗ | ✓ | |
| POST | `/polls/{pollId:guid}/close` | permission | `message.send` + autor ou `workspace.admin` | ✓* | ✗ | ✓ | *autor ou admin; tenant via send |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements` | permission | `announcement.acknowledge` | ✓ | ✓ | ✓ | B-112; idempotente; canal Announcement; prazo/encerrado → 409 |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements` | condicional | `announcement.publish` ou `workspace.admin` | ✗ | ✗ | ✓ | B-112; lista limitada; outro canal/tenant → 404/403 |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements/close` | permission | `announcement.publish` | ✗ | ✗ | ✓ | B-112; idempotente; audit `announcement.closed` |
| GET | `/workspaces/{workspaceId:guid}/announcements/pending` | permission | `message.read` | ✓ | ✓ | ✓ | B-112; inbox de confirmação pendente do caller |
| POST | `/workspaces/{workspaceId:guid}/messages/{messageId:guid}/forward` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/threads` | permission | `message.send` | ✓ | ✗ | ✓ | Get-or-create + reply |
| GET | `/threads/{threadId:guid}` | membership | — | ✓ | ✓ | ✓ | |
| GET | `/threads/{threadId:guid}/messages` | membership | — | ✓ | ✓ | ✓ | |
| POST | `/threads/{threadId:guid}/messages` | permission | `message.send` | ✓ | ✗ | ✓ | |
| POST | `/threads/{threadId:guid}/subscription` | permission | `message.read` | ✓ | ✓ | ✓ | B-102 seguir; outro tenant → 403 |
| DELETE | `/threads/{threadId:guid}/subscription` | permission | `message.read` | ✓ | ✓ | ✓ | B-102 deixar de seguir |
| PUT | `/threads/{threadId:guid}/subscription/read-cursor` | permission | `message.read` | ✓ | ✓ | ✓ | B-102; sem assinatura → 404 |
| GET | `/workspaces/{workspaceId:guid}/threads/following` | permission | `message.read` | ✓ | ✓ | ✓ | B-102; membership perdida esconde a linha |
| POST | `/threads/{threadId:guid}/messages/{messageId:guid}/share-to-channel` | permission | `message.send` | ✓ | ✗ | ✓ | B-102; reusa forward writer (B-085) |
| POST | `/channels/{channelId:guid}/attachments` | permission | `file.upload` + `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete` | permission | `file.upload` | ✓ | ✗ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/download` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/thumbnail` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/messaging-policy` | permission | `message.read` | ✓ | ✓ | ✓ | B-107; campos não secretos; guest do canal |
| PUT | `/channels/{channelId:guid}/messages/{messageId:guid}` | condicional | `message.edit.own` / `message.edit.any` | ✓* | ✗ | ✓* | *autor na política, ou override `message.edit.any` (default off); exempt B-023 / B-107 |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/history` | permission | `message.read` + `message.history.read` | ✓ | ✗ | ✓ | B-114; flag off → 404; política oculta → 403 `HistoryHidden` (admin break-glass auditado) |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/move` | permission | `message.read` | ✓ | ✓ | ✓ | B-114; sem acesso ao outro canal o corpo do destino não sai |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/move` | permission | `message.move` | ✓ | ✗ | ✓ | B-114; membership nos dois canais; outro tenant → 403 |
| PUT | `/channels/{channelId:guid}/messages/{messageId:guid}/reactions` | permission | `message.react` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/messages/{messageId:guid}/pin` | permission | `message.pin` | ✓ | ✗ | ✓ | |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}/pin` | permission | `message.pin` | ✓ | ✗ | ✓ | |
| GET | `/channels/{channelId:guid}/pins` | permission | `message.read` | ✓ | ✓ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/saved` | permission | `message.read` | ✓ | ✓ | ✓ | |
| PATCH | `/workspaces/{workspaceId:guid}/saved/{messageId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | |
| DELETE | `/workspaces/{workspaceId:guid}/saved/{messageId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | |
| GET | `/workspaces/{workspaceId:guid}/saved` | permission | `message.read` | ✓ | ✓ | ✓ | |
| POST | `/channels/{channelId:guid}/scheduled-messages` | permission | `message.send` | ✓ | ✗ | ✓ | B-113; horário IANA → UTC |
| PATCH | `/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}` | permission | `message.send` | ✓ | ✗ | ✓ | B-113; só o autor e só Pending |
| DELETE | `/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}` | permission | `message.send` | ✓ | ✗ | ✓ | B-113; cancelar antes do claim |
| GET | `/workspaces/{workspaceId:guid}/schedule` | permission | `message.read` | ✓ | ✓ | ✓ | B-113; lista pessoal |
| POST | `/workspaces/{workspaceId:guid}/reminders` | permission | `message.read` | ✓ | ✓ | ✓ | B-113; lembrete privado |
| PATCH | `/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-113; só o dono |
| DELETE | `/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-113 |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/reactions/{emoji}/users` | permission | `message.react` | ✓ | ✗ | ✓ | |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}` | condicional | `message.delete.own` / `message.delete.any` | ✓* | ✗ | ✓* | *autor na política, ou override `message.delete.any` (default on); exempt B-023 / B-107 |
| DELETE | `/channels/{channelId:guid}/messages/{messageId:guid}/link-preview` | condicional | autor ou `workspace.admin` | ✓* | ✗ | ✓ | exempt B-091 |
| GET | `/channels/{channelId:guid}/messages/{messageId:guid}/link-preview/image` | membership | — | ✓ | ✓ | ✓ | Proxy de imagem já SSRF-guarded |
| PUT | `/channels/{channelId:guid}/read-cursor` | permission | `message.read` | ✓ | ✓ | ✓ | |
| GET | `/search/messages` | permission | `search.messages` + `message.read` | ✓ | ✓ | ✓ | Filtros B-098 só restringem; hits de canal/pessoa/anexo usam a mesma membership; `authorId`/`channelId` invisível → 403 |
| GET | `/channels/{channelId:guid}/unread-count` | membership | — | ✓ | ✓ | ✓ | Contagem do caller |
| GET | `/notifications/push/public-key` | permission | `message.read` | ✓ | ✓ | ✓ | `{ enabled, publicKey? }`; off sem erro |
| GET | `/notifications/push/subscriptions` | permission | `message.read` | ✓ | ✓ | ✓ | Só o actor |
| POST | `/notifications/push/subscriptions` | permission | `message.read` | ✓ | ✓ | ✓ | Upsert do actor |
| DELETE | `/notifications/push/subscriptions/{id:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | 404 se não for do actor |
| GET | `/notifications/preferences` | permission | `message.read` | ✓ | ✓ | ✓ | Preferência global do actor + overrides de canal |
| PUT | `/notifications/preferences` | permission | `message.read` | ✓ | ✓ | ✓ | Upsert; sempre `(tenant, actor)` |
| PUT | `/notifications/preferences/channels/{channelId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | Canal de outro tenant → 403 |
| DELETE | `/notifications/preferences/channels/{channelId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | Canal de outro tenant → 403; preserva linha se `FollowAllThreads` |
| PUT | `/notifications/preferences/channels/{channelId:guid}/follow-all-threads` | permission | `message.read` | ✓ | ✓ | ✓ | B-102; canal de outro tenant → 403 |
| GET | `/admin/dashboard` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado |
| GET | `/admin/audit-events` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/conversations` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/conversations/{channelId:guid}/messages` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Bypass membership de canal |
| GET | `/admin/threads/{threadId:guid}/messages` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | |
| GET | `/admin/settings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| PUT | `/admin/settings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/openrouter/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/smtp/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/settings/credentials/webhook/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | 0 ou 1 endpoint; mais de um → 409 |
| POST | `/admin/webhooks` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-108; limite 5; canal de outro tenant → 400 |
| PUT | `/admin/webhooks/{endpointId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Sem secret no body |
| DELETE | `/admin/webhooks/{endpointId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| POST | `/admin/webhooks/{endpointId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Resposta só máscara |
| POST | `/admin/webhooks/{endpointId:guid}/test` | permission | `workspace.admin` | ✗ | ✗ | ✓ | `WebhookTest`; não grava mensagem |
| GET | `/admin/workspaces/{workspaceId:guid}/bots` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-109; flag off → 404; sem secret |
| POST | `/admin/workspaces/{workspaceId:guid}/bots` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Token só na criação |
| PUT | `/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Sem token no body |
| POST | `/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Token só na resposta |
| POST | `/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/revoke` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Invalida na hora |
| GET | `/admin/workspaces/{workspaceId:guid}/plugins` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-110; flag off → 404; sem secret |
| POST | `/admin/workspaces/{workspaceId:guid}/plugins` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Built-in ou manifesto; token só na criação |
| PATCH | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | `enabled`; outro workspace → 404 |
| DELETE | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Revoga o token na mesma transação |
| POST | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Token só na resposta |
| GET | `/admin/workspaces/{workspaceId:guid}/templates` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-115; built-ins + custom do workspace |
| POST | `/admin/workspaces/{workspaceId:guid}/templates/validate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Rejeita campo/versão desconhecidos |
| POST | `/admin/workspaces/{workspaceId:guid}/templates/preview` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Dry-run; não persiste |
| POST | `/admin/workspaces/{workspaceId:guid}/templates/apply` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Idempotente; conflito → 409 sem gravar |
| POST | `/admin/workspaces/{workspaceId:guid}/templates/import` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Sem membros/secrets |
| GET | `/admin/workspaces/{workspaceId:guid}/templates/export` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Estrutura, sem PII |
| GET | `/admin/workspaces/{workspaceId:guid}/templates/{templateId}/export` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Outro workspace → 404 |
| GET | `/admin/workspaces/{workspaceId:guid}/onboarding` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Checklist; não obriga template |
| PUT | `/admin/workspaces/{workspaceId:guid}/onboarding` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Pular e retomar |
| POST | `/workspaces/{workspaceId:guid}/imports` | permission | `workspace.import` | ✗ | ✗ | ✓ | B-153; flag off → 404 `ImportDisabled`; manifesto não escolhe tenant |
| GET | `/workspaces/{workspaceId:guid}/imports` | permission | `workspace.import` | ✗ | ✗ | ✓ | Resumo sem corpo |
| GET | `/workspaces/{workspaceId:guid}/imports/{importId:guid}` | permission | `workspace.import` | ✗ | ✗ | ✓ | Sem canonical JSON |
| GET | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/report` | permission | `workspace.import` | ✗ | ✗ | ✓ | Sem body, e-mail ou payload |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/plan` | permission | `workspace.import` | ✗ | ✗ | ✓ | Dry-run; não grava domínio |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/execute` | permission | `workspace.import` | ✗ | ✗ | ✓ | Staging idempotente |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/pause` | permission | `workspace.import` | ✗ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/resume` | permission | `workspace.import` | ✗ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/publish` | permission | `workspace.import` | ✗ | ✗ | ✓ | Atômico na transação do request |
| POST | `/workspaces/{workspaceId:guid}/imports/{importId:guid}/rollback` | permission | `workspace.import` | ✗ | ✗ | ✓ | Publicado exige `confirm` |
| POST | `/integrations/v1/channels/{channelId:guid}/messages` | exempt | integration token | — | — | — | B-109; `AllowAnonymous` + hash; fora do escopo → 403 |
| POST | `/integrations/v1/dms` | exempt | integration token | — | — | — | Exige `allowDms`; peer do mesmo workspace |
| POST | `/admin/settings/credentials/vapid/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-187 |
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

- Novo endpoint `/api/v1` → linha nesta matriz no mesmo PR (método + template literal do map em `apps/api`).
- Mutações: `RequirePermission` ou `AllowPermissionGateExempt` (gate CI B-174).
- Arch test `Api_v1_maps_are_listed_in_authz_matriz` falha se o par método/path sumir daqui.
- Paths `/admin/*` no filtro `RequirePermission` resolvem tenant via membership admin
  (não via `ResolveChannelAsync`) para preservar o bypass de `channel_members` da
  auditoria de conversa (B-067 / B-175).
