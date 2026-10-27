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
| GET | `/auth/sso-providers` | exempt | AllowAnonymous | — | — | — | B-164; primeira rota de `/api/v1`; sem sessão; flag off → 404 `SsoDisabled`; sem segredo |
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
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/plugin-commands` | permission | `message.send` | ✓ | ✗ | ✓ | B-066; capability + escopo; flag off → 404 |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/plugin-actions` | permission | `message.send` | ✓ | ✗ | ✓ | B-111; membership + capability + grant do canal; idempotente |
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
| GET | `/workspaces/{workspaceId:guid}/inbox` | permission | `message.read` | ✓ | ✓ | ✓ | B-117; consulta reconstruível; outro tenant → 403; ACL revogada omite o item |
| POST | `/workspaces/{workspaceId:guid}/inbox/read` | permission | `message.read` | ✓ | ✓ | ✓ | B-117; avança o cursor canônico do canal ou da thread; anúncio/salvo → 400 |
| POST | `/workspaces/{workspaceId:guid}/inbox/unread` | permission | `message.read` | ✓ | ✓ | ✓ | B-117; retrocede o cursor canônico (`allowRetrograde`) |
| PUT | `/workspaces/{workspaceId:guid}/inbox/snooze` | permission | `message.read` | ✓ | ✓ | ✓ | B-117; snooze pessoal; item invisível → 404 sem preview |
| DELETE | `/workspaces/{workspaceId:guid}/inbox/snooze` | permission | `message.read` | ✓ | ✓ | ✓ | B-117; `itemKey` na query; idempotente |
| POST | `/workspaces/{workspaceId:guid}/commitments` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; converte mensagem legível; idempotente; sem cópia do body |
| GET | `/workspaces/{workspaceId:guid}/commitments` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; lista por workspace/canal/pessoa; ACL atual |
| GET | `/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; invisível → 404 |
| PATCH | `/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; criador, owner ou admin no modo canal |
| POST | `/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/close` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; audit `commitment.close` |
| POST | `/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/reopen` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; audit `commitment.reopen` |
| GET | `/workspaces/{workspaceId:guid}/commitments/{commitmentId:guid}/history` | permission | `message.read` | ✓ | ✓ | ✓ | B-119; histórico sem body |
| POST | `/workspaces/{workspaceId:guid}/tasks` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; origem legível; idempotente; sem cópia do body |
| GET | `/workspaces/{workspaceId:guid}/tasks` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; views workspace/canal/minhas; ACL atual |
| GET | `/workspaces/{workspaceId:guid}/tasks/{taskId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; invisível → 404 |
| PATCH | `/workspaces/{workspaceId:guid}/tasks/{taskId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; versão otimista; criador, assignee ou admin no modo canal |
| POST | `/workspaces/{workspaceId:guid}/tasks/{taskId:guid}/complete` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; repetir não emite outro evento |
| POST | `/workspaces/{workspaceId:guid}/tasks/{taskId:guid}/reopen` | permission | `message.read` | ✓ | ✓ | ✓ | B-123; audit `task.reopen` |
| POST | `/workspaces/{workspaceId:guid}/forms` | permission | `form.create` | ✓ | ✗ | ✓ | B-124; flag off → 404 `FormDisabled`; rascunho idempotente |
| GET | `/workspaces/{workspaceId:guid}/forms` | permission | `form.submit` | ✓ | ✗ | ✓ | B-124; rascunho só do autor; canal sem leitura → 403 |
| GET | `/workspaces/{workspaceId:guid}/forms/{formId:guid}` | permission | `form.submit` | ✓ | ✗ | ✓ | B-124; rascunho alheio → 404 |
| PATCH | `/workspaces/{workspaceId:guid}/forms/{formId:guid}` | permission | `form.create` | ✓ | ✗ | ✓ | B-124; só o autor; não altera versão publicada |
| POST | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/publish` | permission | `form.publish` | ✓ | ✗ | ✓ | B-124; congela `form.schema.v1`; só o autor |
| POST | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/archive` | permission | `form.publish` | ✓ | ✗ | ✓ | B-124; arquivado não recebe resposta |
| POST | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions` | permission | `form.submit` | ✓ | ✗ | ✓ | B-124; idempotente; campo oculto → 400 `HiddenField` |
| GET | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions` | permission | `form.submit` | ✓ | ✗ | ✓ | B-124; autor, criador ou revisor da versão |
| GET | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions/{submissionId:guid}` | permission | `form.submit` | ✓ | ✗ | ✓ | B-124; fora do escopo → 404 |
| POST | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/submissions/{submissionId:guid}/decisions` | permission | `form.review` | ✓ | ✓ | ✓ | B-124; actor é o autenticado; `actorUserId` alheio → 400 |
| GET | `/workspaces/{workspaceId:guid}/forms/{formId:guid}/export` | permission | `form.review` | ✓ | ✓ | ✓ | B-124; audit sem respostas; retenção zera answers |
| POST | `/workspaces/{workspaceId:guid}/automations` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; flag off → 404 `AutomationDisabled`; só admin |
| GET | `/workspaces/{workspaceId:guid}/automations` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; lista sem token de webhook |
| GET | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; outro workspace → 404 |
| PATCH | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; rascunho; não altera versão publicada |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/publish` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; congela `automation.definition.v1`; token só uma vez |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/enable` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; limpa circuito |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/disable` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; impede novos runs |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/dry-run` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; sem efeito externo |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/trigger` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; idempotente por evento |
| GET | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; log sem segredo |
| GET | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs/{runId:guid}` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; erro por step |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/runs/{runId:guid}/replay` | permission | `automation.manage` | ✗ | ✗ | ✓ | B-125; replay explícito |
| POST | `/workspaces/{workspaceId:guid}/automations/{automationId:guid}/hooks` | exempt | — | ✗ | ✗ | ✗ | B-125; token `vc_auto_` só como hash; flag off ou token inválido → 404 |
| POST | `/workspaces/{workspaceId:guid}/incidents` | permission | `incident.manage` | ✗ | ✗ | ✓ | B-126; flag off → 404 `IncidentDisabled`; sala privada; retry idempotente |
| GET | `/workspaces/{workspaceId:guid}/incidents/current` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; sem membership da sala devolve `incident: null` sem título |
| GET | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; outsider → 404 `IncidentNotFound` sem título nem severidade |
| POST | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/transitions` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; só membro; salto inválido → 409 |
| POST | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/evidence` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; só membro; evidência fora da sala → 400 sem body |
| POST | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/updates` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; só membro; nota fica no audit, não na timeline |
| POST | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/steps/{stepId:guid}/run` | permission | `message.read` | ✓ | ✓ | ✓ | B-126; só membro; automação indisponível falha o passo |
| GET | `/workspaces/{workspaceId:guid}/incidents/{incidentId:guid}/postmortem` | permission | `incident.export` | ✗ | ✗ | ✓ | B-126; também exige membership; senão 404 sem título |
| GET | `/workspaces/{workspaceId:guid}/connectors/catalog` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; escopos mínimos; flag off → 404 `ConnectorDisabled` |
| POST | `/workspaces/{workspaceId:guid}/connectors` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; segredo só no envelope; resposta só máscara |
| GET | `/workspaces/{workspaceId:guid}/connectors` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; sem token |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/rotate` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; token novo não volta na resposta |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/revoke` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; encerra chamadas |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/reconnect` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; não fura circuito aberto |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/oauth/start` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; PKCE; URL com escopos mínimos |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/oauth/callback` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; não devolve access token |
| POST | `/workspaces/{workspaceId:guid}/connectors/{installationId:guid}/deliveries` | permission | `connector.manage` | ✗ | ✗ | ✓ | B-127; falha externa não entra na outbox de mensagens |
| POST | `/connectors/inbound/{opaqueKey}` | exempt | — | ✗ | ✗ | ✗ | B-127; assinatura; tenant da instalação, não do JSON; revogado → 410 |
| GET | `/workspaces/{workspaceId:guid}/knowledge/collections` | permission | `knowledge.view` | ✓ | ✓ | ✓ | B-120; canal privado some sem membership |
| POST | `/workspaces/{workspaceId:guid}/knowledge/collections` | permission | `knowledge.edit` | ✓ | ✗ | ✓ | B-120; flag off → 404 |
| DELETE | `/workspaces/{workspaceId:guid}/knowledge/collections/{collectionId:guid}` | permission | `knowledge.manage` | ✗ | ✗ | ✓ | B-120 |
| GET | `/workspaces/{workspaceId:guid}/knowledge/pages` | permission | `knowledge.view` | ✓ | ✓ | ✓ | B-120 |
| POST | `/workspaces/{workspaceId:guid}/knowledge/pages` | permission | `knowledge.edit` | ✓ | ✗ | ✓ | B-120 |
| GET | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}` | permission | `knowledge.view` | ✓ | ✓ | ✓ | Fora da ACL → 404 sem body |
| PATCH | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}` | permission | `knowledge.edit` | ✓ | ✗ | ✓ | Versão divergente → 409 |
| DELETE | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}` | permission | `knowledge.manage` | ✗ | ✗ | ✓ | B-120 |
| POST | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/restore` | permission | `knowledge.manage` | ✗ | ✗ | ✓ | B-120 |
| GET | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/history` | permission | `knowledge.view` | ✓ | ✓ | ✓ | B-120 |
| GET | `/workspaces/{workspaceId:guid}/knowledge/pages/{pageId:guid}/export` | permission | `knowledge.view` | ✓ | ✓ | ✓ | Inclui versões |
| GET | `/workspaces/{workspaceId:guid}/knowledge/search` | permission | `knowledge.view` | ✓ | ✓ | ✓ | Não amplia ACL |
| GET | `/workspaces/{workspaceId:guid}/knowledge/export` | permission | `knowledge.manage` | ✗ | ✗ | ✓ | Mesmo filtro de ACL |
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
| GET | `/channels/{channelId:guid}/topics` | membership | — | ✓ | ✓ | ✓ | B-118; canal `Topics`; sem acesso → 403; flag off → 404 |
| POST | `/channels/{channelId:guid}/topics` | permission | `conversation.topic.create` | ✓ | ✗ | ✓ | B-118; guest do canal também cria |
| GET | `/topics/{topicId:guid}` | membership | — | ✓ | ✓ | ✓ | B-118; outro tenant → 404 sem título |
| PUT | `/topics/{topicId:guid}` | permission | `conversation.topic.create` | ✓* | ✗ | ✓ | *autor ou `conversation.topic.manage` |
| POST | `/topics/{topicId:guid}/close` | permission | `conversation.topic.manage` | ✗ | ✗ | ✓ | B-118 |
| POST | `/topics/{topicId:guid}/reopen` | permission | `conversation.topic.manage` | ✗ | ✗ | ✓ | B-118 |
| POST | `/topics/{topicId:guid}/move` | permission | `conversation.topic.manage` | ✗ | ✗ | ✓ | Destino no mesmo workspace e layout `Topics` |
| POST | `/topics/{topicId:guid}/subscription` | permission | `message.read` | ✓ | ✓ | ✓ | B-118 follow |
| DELETE | `/topics/{topicId:guid}/subscription` | permission | `message.read` | ✓ | ✓ | ✓ | B-118 unfollow |
| PUT | `/topics/{topicId:guid}/read-cursor` | permission | `message.read` | ✓ | ✓ | ✓ | Cursor da conversa do tópico |
| GET | `/topics/{topicId:guid}/messages` | membership | — | ✓ | ✓ | ✓ | B-118 |
| POST | `/topics/{topicId:guid}/messages` | permission | `message.send` | ✓ | ✗ | ✓ | Fechado sem manage → 403 `TopicClosed` |
| POST | `/channels/{channelId:guid}/attachments` | permission | `file.upload` + `message.send` | ✓ | ✗ | ✓ | |
| POST | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete` | permission | `file.upload` | ✓ | ✗ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/download` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| GET | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/thumbnail` | permission | `file.download` + `message.read` | ✓ | ✓ | ✓ | |
| POST | `/channels/{channelId:guid}/attachments/{attachmentId:guid}/rescan` | permission | `file.upload` | ✓ | ✗ | ✓ | B-131; autor ou `file.quarantine.manage`; flag off → 404 |
| GET | `/workspaces/{workspaceId:guid}/quarantine` | permission | `file.quarantine.manage` | ✗ | ✗ | ✓ | B-131; flag off → 404; outro tenant → 404 |
| POST | `/workspaces/{workspaceId:guid}/quarantine/{attachmentId:guid}/release` | permission | `file.quarantine.manage` | ✗ | ✗ | ✓ | B-131; justificativa obrigatória |
| POST | `/workspaces/{workspaceId:guid}/quarantine/{attachmentId:guid}/delete` | permission | `file.quarantine.manage` | ✗ | ✗ | ✓ | B-131; apaga o objeto se não houver outra referência |
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
| GET | `/workspaces/{workspaceId:guid}/semantic/settings` | permission | `ai.search` | ✓ | ✓ | ✓ | B-121; off → 404 sem projeção |
| PUT | `/workspaces/{workspaceId:guid}/semantic/settings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Desligar apaga chunks do workspace |
| POST | `/workspaces/{workspaceId:guid}/semantic/query` | permission | `ai.search` + `search.messages` | ✓ | ✓ | ✓ | Citação só após revalidar ACL; sem evidência é resposta válida |
| POST | `/workspaces/{workspaceId:guid}/semantic/report` | permission | `ai.search` + `search.messages` | ✓ | ✓ | ✓ | Audit sem pergunta nem trecho |
| POST | `/workspaces/{workspaceId:guid}/semantic/rebuild` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Reconstrói projeção; não amplia ACL |
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
| GET | `/notifications/digests/settings` | permission | `message.read` | ✓ | ✓ | ✓ | B-122; off → 404 |
| PUT | `/notifications/digests/settings` | permission | `message.read` | ✓ | ✓ | ✓ | B-122; só o actor |
| POST | `/workspaces/{workspaceId:guid}/digests/catch-up` | permission | `message.read` | ✓ | ✓ | ✓ | B-122; idempotente; não marca lido |
| GET | `/workspaces/{workspaceId:guid}/digests/latest` | permission | `message.read` | ✓ | ✓ | ✓ | B-122; só runs do actor |
| GET | `/workspaces/{workspaceId:guid}/digests/{digestId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-122; outro usuário → 404; outro tenant → 403 |
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
| GET | `/admin/workspaces/{workspaceId:guid}/scim` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-128; flag off → 404 `ScimDisabled`; sem secret |
| POST | `/admin/workspaces/{workspaceId:guid}/scim/tokens` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Segredo só na criação |
| POST | `/admin/workspaces/{workspaceId:guid}/scim/tokens/{tokenId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Segredo só na resposta |
| DELETE | `/admin/workspaces/{workspaceId:guid}/scim/tokens/{tokenId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Invalida na hora |
| POST | `/admin/workspaces/{workspaceId:guid}/scim/mappings/dry-run` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Não grava membership |
| POST | `/admin/workspaces/{workspaceId:guid}/scim/mappings` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Papel allowlisted |
| DELETE | `/admin/workspaces/{workspaceId:guid}/scim/mappings/{mappingId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Remove grant do mapeamento |
| POST | `/admin/workspaces/{workspaceId:guid}/scim/provisioning/users` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Mesmo comando do SCIM |
| GET | `/admin/workspaces/{workspaceId:guid}/plugins/builtins` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-066; catálogo compilado; sem pacote remoto |
| GET | `/admin/workspaces/{workspaceId:guid}/plugins` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-110; flag off → 404; sem secret |
| POST | `/admin/workspaces/{workspaceId:guid}/plugins` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Built-in ou manifesto; token só na criação |
| PATCH | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | `enabled`; outro workspace → 404 |
| DELETE | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Revoga o token na mesma transação |
| POST | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Token só na resposta |
| POST | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/history-grants` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-111; janela máxima 7 dias; canal no escopo do bot |
| DELETE | `/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/history-grants/{grantId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Revogação imediata da leitura |
| GET | `/admin/workspaces/{workspaceId:guid}/plugin-registry` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; flag off → 404 `PluginRegistryDisabled`; sem token |
| POST | `/admin/workspaces/{workspaceId:guid}/plugin-registry/preview` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; não persiste; URL remota → `RegistryEgressBlocked` |
| POST | `/admin/workspaces/{workspaceId:guid}/plugin-registry/install` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; grant explícito; token só na criação |
| POST | `/admin/workspaces/{workspaceId:guid}/plugin-registry/update` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; capability nova exige confirmação |
| POST | `/admin/workspaces/{workspaceId:guid}/plugin-registry/rollback` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; restaura a versão anterior assinada |
| POST | `/admin/workspaces/{workspaceId:guid}/plugin-registry/revoke` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-137; desliga a versão na hora |
| GET | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/bridge` | permission | `message.read` | ✗ | ✗ | ✓ | B-138; membership; flag off → 404 `BridgesDisabled` |
| POST | `/admin/workspaces/{workspaceId:guid}/bridges` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; pin; token só na criação |
| POST | `/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/consent` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; cópia só após acknowledge |
| POST | `/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/pause` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; bloqueia os dois lados |
| POST | `/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/resume` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; só a partir de paused |
| POST | `/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/disconnect` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; desativa o bot |
| GET | `/admin/workspaces/{workspaceId:guid}/bridges/{bridgeId:guid}/mapping` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-138; subject opaco |
| POST | `/bridges/{bridgeId:guid}/events` | exempt | bridge bot token | — | — | — | B-138; `AllowAnonymous` + token; outro bot → 403 |
| POST | `/bridges/{bridgeId:guid}/outbound` | exempt | bridge bot token | — | — | — | B-138; pull; sem socket de saída |
| GET | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/federation` | permission | `message.read` | ✗ | ✗ | ✓ | B-065; membership; flag off → 404 `FederationDisabled` |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; allowlist; token só na criação |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/consent` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; cópia só após acknowledge |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/pause` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; bloqueia os dois lados |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/resume` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; só a partir de paused |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/revoke` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; desativa o bot |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/keys/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; token novo uma vez; anterior ainda vale |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/keys/retire` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; descarta a chave anterior |
| POST | `/admin/workspaces/{workspaceId:guid}/federation/trusts/{trustId:guid}/rooms` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-065; sala explícita; não é DM |
| POST | `/federation/rooms/{roomId:guid}/events` | exempt | federation bot token | — | — | — | B-065; `AllowAnonymous` + token; assinatura inválida não grava |
| POST | `/federation/rooms/{roomId:guid}/reconcile` | exempt | federation bot token | — | — | — | B-065; pull; sem socket de saída |
| GET | `/public/branding` | exempt | AllowAnonymous | — | — | — | B-140; host do operador; ignora `tenantId` |
| GET | `/public/branding/assets/{kind}` | exempt | AllowAnonymous | — | — | — | B-140; 404 sem arquivo |
| GET | `/branding` | exempt | membership | ✓ | ✓ | ✓ | B-140; tenant da sessão |
| GET | `/branding/assets/{kind}` | exempt | membership | ✓ | ✓ | ✓ | B-140 |
| GET | `/admin/branding` | permission | `admin.brand` | ✗ | ✗ | ✓ | B-140; versão VibeChat permanece |
| PUT | `/admin/branding` | permission | `admin.brand` | ✗ | ✗ | ✓ | B-140; contraste no servidor |
| POST | `/admin/branding/assets/{kind}` | permission | `admin.brand` | ✗ | ✗ | ✓ | B-140; PNG/JPEG |
| DELETE | `/admin/branding/assets/{kind}` | permission | `admin.brand` | ✗ | ✗ | ✓ | B-140; volta ao asset oficial |
| POST | `/admin/branding/rollback` | permission | `admin.brand` | ✗ | ✗ | ✓ | B-140; um passo |
| GET | `/desktop/shell` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; flag off → 404 `DesktopDisabled`; sem segredo |
| POST | `/desktop/deep-links/validate` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; host fora da lista → 400 |
| POST | `/desktop/updates/verify` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; feed vazio → 409; assinatura inválida → 400 |
| GET | `/desktop/devices` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; só os aparelhos do caller |
| POST | `/desktop/devices` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; recusa token no body |
| PUT | `/desktop/devices/{deviceId:guid}/preferences` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; dono; launch-at-login opt-in |
| DELETE | `/desktop/devices/{deviceId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-141; dono ou `workspace.admin`; limpa sessão no cliente |
| GET | `/mobile/shell` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; flag off → 404 `MobileDisabled`; sem segredo |
| POST | `/mobile/deep-links/validate` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; host fora da lista → 400 |
| POST | `/mobile/share/validate` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; confirma destino; não envia mensagem |
| POST | `/mobile/push/preview` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; payload só com ids |
| GET | `/mobile/devices` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; só os aparelhos do caller |
| POST | `/mobile/devices` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; recusa token no body |
| PUT | `/mobile/devices/{deviceId:guid}/preferences` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; dono; biometria opt-in |
| DELETE | `/mobile/devices/{deviceId:guid}` | permission | `message.read` | ✓ | ✓ | ✓ | B-063; dono ou `workspace.admin`; pede wipe do secure storage |
| GET | `/sync` | permission | `message.read` | ✓ | ✓ | ✓ | B-143; flag off ou chave inválida → 404 `OfflineWriteDisabled`; cursor não autoriza outro tenant |
| GET | `/workspaces/{workspaceId:guid}/storage` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; flag off → 404 `StorageLifecycleDisabled`; sem object key |
| POST | `/workspaces/{workspaceId:guid}/storage/limits` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; soft/hard; hard abaixo do uso → 409 |
| POST | `/workspaces/{workspaceId:guid}/storage/reservations` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; reserva atômica; estouro → 409 `StorageQuotaHardLimit` |
| POST | `/workspaces/{workspaceId:guid}/storage/objects` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; resposta sem `storageKey` |
| POST | `/workspaces/{workspaceId:guid}/storage/reconcile` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; `Idempotency-Key`; corrige o ledger |
| POST | `/workspaces/{workspaceId:guid}/storage/gc` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; dry-run; não apaga referência nem hold |
| POST | `/workspaces/{workspaceId:guid}/storage/restore` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; tombstone → 409 `StorageAlreadyPurged` |
| POST | `/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/archive` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; só a partir de `hot` |
| POST | `/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/purge` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-145; hold e referência bloqueiam |
| GET | `/workspaces/{workspaceId:guid}/storage/objects/{objectId:guid}/cdn` | permission | `message.read` | ✓ | ✓ | ✓ | B-145; só objeto Clean; URL sem key |
| GET | `/storage/cdn` | permission | `message.read` | ✓ | ✓ | ✓ | B-145; `?token=`; revalida membership; vencido → 410 |
| GET | `/workspaces/{workspaceId:guid}/chat-backup` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; flag off → 404 `ChatBackupDisabled`; segredo só mascarado |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/destinations` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; `Idempotency-Key`; segredo não volta na resposta |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}/test` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; falha de rede não derruba a API |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; rotate sem redeploy |
| DELETE | `/workspaces/{workspaceId:guid}/chat-backup/destinations/{destinationId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; tarefa ligada → 409 `ChatBackupDestinationInUse` |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/jobs` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; `Idempotency-Key` |
| PATCH | `/workspaces/{workspaceId:guid}/chat-backup/jobs/{jobId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; pausa e agenda |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/jobs/{jobId:guid}/run` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; `Idempotency-Key`; falha fica no run |
| GET | `/workspaces/{workspaceId:guid}/chat-backup/export` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; ZIP `vibechat.chat.backup.v1` |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/import/validate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; dry-run; não grava |
| POST | `/workspaces/{workspaceId:guid}/chat-backup/import/execute` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-172; `confirm=true` e `Idempotency-Key`; cross-tenant → 403 |
| GET | `/meta/runtime` | permission | `support.read` | ✗ | ✓ | ✓ | B-144; flag off → 404 `HaDisabled`; sem dado de tenant |
| POST | `/admin/ha/drain` | permission | `support.repair` | ✗ | ✗ | ✓ | B-144; drena só o processo local; flag off → 404 `HaDisabled` |
| GET | `/admin/event-schemas` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-139; flag off → 404 `EventSchemasDisabled`; sem dado de tenant |
| GET | `/admin/event-schemas/{eventType}/versions/{schemaVersion:int}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-139; versão desconhecida → 404 `UnknownSchemaVersion` |
| POST | `/admin/event-schemas/check` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-139; não persiste o documento; teto 16 KiB |
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
| GET | `/workspaces/{workspaceId:guid}/diagnostics` | permission | `support.read` | ✗ | ✓ | ✓ | B-154; feature off é `Skipped`; admin vê evidência mascarada |
| POST | `/workspaces/{workspaceId:guid}/diagnostics/probes/{kind}` | permission | `support.repair` | ✗ | ✗ | ✓ | Probe sintético; storage é apagado na mesma chamada |
| POST | `/workspaces/{workspaceId:guid}/diagnostics/bundles` | permission | `support.bundle` | ✗ | ✗ | ✓ | Flag off → 404 `SupportBundleDisabled`; exige Idempotency-Key |
| GET | `/workspaces/{workspaceId:guid}/diagnostics/bundles/{bundleId:guid}` | permission | `support.bundle` | ✗ | ✗ | ✓ | TTL 15 min e 3 downloads; depois 410 |
| POST | `/workspaces/{workspaceId:guid}/diagnostics/repairs` | permission | `support.repair` | ✗ | ✗ | ✓ | Allowlist; `target` → 400; dry-run não escreve projeção |
| POST | `/workspaces/{workspaceId:guid}/diagnostics/repairs/{repairId:guid}/apply` | permission | `support.repair` | ✗ | ✗ | ✓ | Só `search.reindex` escreve `search_vector` |
| POST | `/workspaces/{workspaceId:guid}/diagnostics/repairs/{repairId:guid}/cancel` | permission | `support.repair` | ✗ | ✗ | ✓ | Só a partir de `running` |
| POST | `/integrations/v1/channels/{channelId:guid}/messages` | exempt | integration token | — | — | — | B-109; `AllowAnonymous` + hash; fora do escopo → 403 |
| POST | `/integrations/v1/dms` | exempt | integration token | — | — | — | Exige `allowDms`; peer do mesmo workspace |
| POST | `/integrations/v1/channels/{channelId:guid}/interactive-messages` | exempt | integration token | — | — | — | B-111; `messages.interact`; cartão é dado, sem JS |
| GET | `/integrations/v1/channels/{channelId:guid}/history` | exempt | integration token | — | — | — | B-111; só grant de janela; sem grant → 404 |
| POST | `/integrations/v1/channels/{channelId:guid}/attachments` | exempt | integration token | — | — | — | B-111; tipo, tamanho e quota; script/DLL recusados |
| POST | `/integrations/v1/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete` | exempt | integration token | — | — | — | Mesmo scan do anexo de usuário |
| POST | `/admin/settings/credentials/vapid/rotate` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-187 |
| POST | `/admin/settings/encryption/reencrypt` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/admin/workspaces/{workspaceId:guid}/export` | permission | `workspace.admin` | ✗ | ✗ | ✓ | |
| GET | `/admin/health-summary` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado; distinto de `/health` |
| GET | `/admin/version` | permission | `admin.dashboard` | ✗ | ✓ | ✓ | Gap B-175 fechado |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize` | permission | `ai.summarize` | ✓ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply` | permission | `ai.suggest_reply` | ✓ | ✗ | ✓ | |
| POST | `/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/attachments/{attachmentId:guid}/transcribe` | permission | `ai.transcribe` | ✓ | ✗ | ✓ | |
| POST | `/dev/seed` | exempt | AllowAnonymous lab | — | — | — | Só Development |
| GET | `/workspaces/{workspaceId:guid}/compliance/access` | permission | `workspace.read` | ✓ | ✓ | ✓ | B-129; flag off → 404 `LegalHoldDisabled` |
| GET | `/workspaces/{workspaceId:guid}/compliance/grants` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Lista nomeações |
| POST | `/workspaces/{workspaceId:guid}/compliance/grants` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Não nomeia a si; Idempotency-Key |
| DELETE | `/workspaces/{workspaceId:guid}/compliance/grants/{grantId:guid}` | permission | `workspace.admin` | ✗ | ✗ | ✓ | Não apaga hold já ativo |
| GET | `/workspaces/{workspaceId:guid}/compliance/holds` | permission | `compliance.hold` | ✗ | ✗ | ✗ | Grant, não papel; Ad sem grant → 403 |
| POST | `/workspaces/{workspaceId:guid}/compliance/holds` | permission | `compliance.hold` | ✗ | ✗ | ✗ | Duas confirmações; aprovação é outra pessoa |
| POST | `/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/approve` | permission | `compliance.hold` | ✗ | ✗ | ✗ | Quem pediu recebe 409 `ComplianceSegregation` |
| POST | `/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/release` | permission | `compliance.hold` | ✗ | ✗ | ✗ | Não apaga a mensagem |
| POST | `/workspaces/{workspaceId:guid}/compliance/holds/{holdId:guid}/release/approve` | permission | `compliance.hold` | ✗ | ✗ | ✗ | Quem pediu a remoção não aprova |
| GET | `/workspaces/{workspaceId:guid}/compliance/cases` | permission | `compliance.discovery` | ✗ | ✗ | ✗ | Grant separado de hold |
| POST | `/workspaces/{workspaceId:guid}/compliance/cases` | permission | `compliance.discovery` | ✗ | ✗ | ✗ | Query sem escolher tenant |
| POST | `/workspaces/{workspaceId:guid}/compliance/cases/{caseId:guid}/exports` | permission | `compliance.discovery` | ✗ | ✗ | ✗ | Snapshot `vibechat.ediscovery.export.v1` + checksum |
| GET | `/workspaces/{workspaceId:guid}/compliance/cases/{caseId:guid}/exports/{exportId:guid}` | permission | `compliance.discovery` | ✗ | ✗ | ✗ | Manifesto imutável depois do purge |
| GET | `/workspaces/{workspaceId:guid}/dlp/label` | permission | `workspace.read` | ✓ | ✓ | ✓ | B-130; flag off → 404 `DlpDisabled` |
| GET | `/workspaces/{workspaceId:guid}/dlp/policy` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Grant, não papel |
| PUT | `/workspaces/{workspaceId:guid}/dlp/policy` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Simulação é o default |
| GET | `/workspaces/{workspaceId:guid}/dlp/labels` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Built-in e custom |
| POST | `/workspaces/{workspaceId:guid}/dlp/labels` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Idempotency-Key |
| PUT | `/workspaces/{workspaceId:guid}/dlp/assignments` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Downgrade → 409 `DlpDowngradeBlocked` |
| PUT | `/workspaces/{workspaceId:guid}/dlp/rules` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Uma regra por ação |
| POST | `/workspaces/{workspaceId:guid}/dlp/detectors` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Regex local; Idempotency-Key |
| POST | `/workspaces/{workspaceId:guid}/dlp/false-positives` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Não guarda o trecho |
| POST | `/workspaces/{workspaceId:guid}/dlp/exceptions` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Expira em até 30 dias |
| GET | `/workspaces/{workspaceId:guid}/dlp/metrics` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Conta decisões, sem body |
| POST | `/workspaces/{workspaceId:guid}/dlp/simulate` | permission | `compliance.dlp` | ✗ | ✗ | ✗ | Mede e não bloqueia o caller |
| GET | `/workspaces/{workspaceId:guid}/siem/destinations` | permission | `security.audit.export` | ✗ | ✗ | ✗ | B-132; flag off → 404 `SiemDisabled`; sem segredo |
| POST | `/workspaces/{workspaceId:guid}/siem/destinations` | permission | `security.audit.export` | ✗ | ✗ | ✗ | Grant, não papel; Idempotency-Key; segredo uma vez |
| POST | `/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/rotate` | permission | `security.audit.export` | ✗ | ✗ | ✗ | Novo HMAC; circuito fecha |
| POST | `/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/revoke` | permission | `security.audit.export` | ✗ | ✗ | ✗ | Para o envio; não apaga o audit local |
| POST | `/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/test` | permission | `security.audit.export` | ✗ | ✗ | ✗ | Evento sintético, sem dado real |
| POST | `/workspaces/{workspaceId:guid}/siem/destinations/{destinationId:guid}/backfill` | permission | `security.audit.export` | ✗ | ✗ | ✗ | Máximo 24 h e 100 eventos do tenant |
| GET | `/workspaces/{workspaceId:guid}/policy-packs/catalog` | permission | `compliance.policy` | ✗ | ✗ | ✗ | B-133; flag off → 404 `PolicyPackDisabled`; baseline técnica, sem selo |
| GET | `/workspaces/{workspaceId:guid}/policy-packs` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Grant, não papel; sem assignment devolve `assigned: false` |
| POST | `/workspaces/{workspaceId:guid}/policy-packs/preview` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Mesmo documento do apply; não grava |
| POST | `/workspaces/{workspaceId:guid}/policy-packs/apply` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Idempotency-Key; confirmação; override mais fraco → 409 |
| POST | `/workspaces/{workspaceId:guid}/policy-packs/rollback` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Só com versão anterior; senão 409 `PolicyPackRollbackUnsafe` |
| GET | `/workspaces/{workspaceId:guid}/policy-packs/export` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Sem segredo, sem id de tenant |
| POST | `/workspaces/{workspaceId:guid}/policy-packs/import` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Manifesto malicioso → 400 `PolicyPackImportRejected` |
| GET | `/workspaces/{workspaceId:guid}/policy-packs/drift` | permission | `compliance.policy` | ✗ | ✗ | ✗ | Compara o efetivo com o controle vivo |
| GET | `/workspaces/{workspaceId:guid}/delegated-admin/catalog` | permission | `workspace.read` | ✓ | ✓ | ✓ | B-134; flag off → 404 `DelegatedAdminDisabled`; catálogo fixo |
| GET | `/workspaces/{workspaceId:guid}/delegated-admin/me` | permission | `workspace.read` | ✓ | ✓ | ✓ | Só as áreas do caller |
| GET | `/workspaces/{workspaceId:guid}/delegated-admin/assignments` | permission | `workspace.read` | ✗ | ✗ | ✓ | Membro sem escopo → 403 `DelegationScopeExceeded` |
| POST | `/workspaces/{workspaceId:guid}/delegated-admin/assignments` | permission | `workspace.read` | ✗* | ✗ | ✓ | *só dentro do escopo; auto-atribuição 403; Idempotency-Key |
| POST | `/workspaces/{workspaceId:guid}/delegated-admin/assignments/{assignmentId:guid}/revoke` | permission | `workspace.read` | ✗* | ✗ | ✓ | *quem poderia conceder aquele escopo |
| GET | `/workspaces/{workspaceId:guid}/delegated-admin/spaces/{spaceId:guid}` | permission | `workspace.read` | ✗* | ✗ | ✓ | *space admin só no próprio space |
| GET | `/workspaces/{workspaceId:guid}/quotas` | permission | `workspace.read` | ✗ | ✗ | ✓ | Uso, tendência e ação |
| GET | `/workspaces/{workspaceId:guid}/quotas/usage` | permission | `workspace.read` | ✗ | ✗ | ✓ | Igual a `GET /quotas` |
| PUT | `/workspaces/{workspaceId:guid}/quotas/{resource}` | permission | `workspace.read` | ✗ | ✗ | ✓ | Soft ≤ hard; billing-read não grava |
| POST | `/workspaces/{workspaceId:guid}/quotas/{resource}/reserve` | permission | `workspace.read` | ✗ | ✗ | ✓ | Hard limit atômico → 409 `QuotaHardLimit` |
| GET | `/workspaces/{workspaceId:guid}/delegated-admin/alerts` | permission | `workspace.read` | ✗ | ✗ | ✓ | Soft, hard e break-glass |
| GET | `/workspaces/{workspaceId:guid}/capacity` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-146; flag off → 404 `CapacitySloDisabled`; sem SLA e sem PII |
| POST | `/workspaces/{workspaceId:guid}/capacity/evaluations` | permission | `workspace.admin` | ✗ | ✗ | ✓ | B-146; envelope `vibechat.capacity-result.v1`; não grava |
| GET | `/workspaces/{workspaceId:guid}/developer/catalog` | permission | `integration.read` | ✗ | ✗ | ✓ | B-135; flag off → 404 `DeveloperPortalDisabled`; sem segredo |
| GET | `/workspaces/{workspaceId:guid}/developer/openapi` | permission | `integration.read` | ✗ | ✗ | ✓ | B-135; paths iguais ao runtime de integração |
| GET | `/workspaces/{workspaceId:guid}/developer/contract-kit` | permission | `integration.read` | ✗ | ✗ | ✓ | B-135; schemas do catálogo público; sem credencial |
| GET | `/workspaces/{workspaceId:guid}/developer/credentials` | permission | `integration.read` | ✗ | ✗ | ✓ | B-135; só last4; nunca `vc_int_` |
| POST | `/workspaces/{workspaceId:guid}/developer/credentials` | permission | `integration.manage` | ✗ | ✗ | ✓ | B-135; token uma vez; `Idempotency-Key`; replay sem segredo |
| POST | `/workspaces/{workspaceId:guid}/developer/credentials/{credentialId:guid}/rotate` | permission | `integration.manage` | ✗ | ✗ | ✓ | B-135; invalida o token anterior na hora |
| POST | `/workspaces/{workspaceId:guid}/developer/credentials/{credentialId:guid}/revoke` | permission | `integration.manage` | ✗ | ✗ | ✓ | B-135; `RevokedAt` imediato → 401 no send |
| POST | `/workspaces/{workspaceId:guid}/developer/explore` | permission | `integration.manage` | ✗ | ✗ | ✓ | B-135; chamada sintética; fora do escopo → 403 `ExplorerScopeDenied`; não grava mensagem |
| POST | `/workspaces/{workspaceId:guid}/developer/webhook-inspector` | permission | `integration.manage` | ✗ | ✗ | ✓ | B-135; payload sintético; não faz POST externo |
| GET | `/workspaces/{workspaceId:guid}/developer/usage` | permission | `integration.read` | ✗ | ✗ | ✓ | B-135; só o workspace do ator; sem token |
| GET | `/workspaces/{workspaceId:guid}/developer/sdk` | permission | `integration.read` | ✗ | ✗ | ✓ | B-136; flag off → 404 `PluginSdkDisabled`; sem segredo |
| POST | `/workspaces/{workspaceId:guid}/developer/sdk/verify` | permission | `integration.read` | ✗ | ✗ | ✓ | B-136; HMAC sintético; replay no cache do caller |
| POST | `/workspaces/{workspaceId:guid}/developer/sdk/contract-tests` | permission | `integration.read` | ✗ | ✗ | ✓ | B-136; 401/403/cross-tenant sintéticos; sem token |
| POST | `/workspaces/{workspaceId:guid}/developer/sdk/sandbox` | permission | `integration.read` | ✗ | ✗ | ✓ | B-136; não grava mensagem; `tenantId` do body é ignorado |

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
