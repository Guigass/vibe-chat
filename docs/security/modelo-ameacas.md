# Modelo de Ameaças — VibeChat

## Objetivo

Identificar ameaças relevantes ao chat corporativo self-hosted e controles mínimos. Não é um threat model STRIDE completo certificado — é a base para design e testes.

## Ativos críticos

| Ativo | Por quê |
|-------|---------|
| Conteúdo de mensagens | Confidencialidade / compliance |
| Anexos | Dados sensíveis, malware |
| Tokens OIDC / sessões | Impersonation |
| Memberships e papéis | Autorização |
| Outbox / eventos | Integridade de entrega |
| Credenciais infra (DB, Redis, MinIO, Keycloak) | Comprometimento total |
| Logs/traces | Podem vazar PII |

## Atores

- Colaborador legítimo (insider limitado)
- Admin de workspace / tenant admin
- Operador de infra (acesso elevado)
- Atacante externo (rede / internet)
- Tenant malicioso em instância multi-tenant
- Dependência/fornecedor de IA (quando habilitado)

## STRIDE (resumo)

| Categoria | Exemplos | Controles |
|-----------|----------|-----------|
| **Spoofing** | Roubo de token; JWT forjado; IdP broker mal configurado; linking por e-mail | OIDC Keycloak; validar issuer/aud/exp; HTTPS; rotação de chaves; app rejeita tokens do IdP externo; linking só com política explícita (B-164) |
| **Tampering** | Alterar messageId/seq; forjar tenant_id | Constraints DB; tenant só do contexto; assinatura não necessária se SoT é DB |
| **Repudiation** | Negar ação admin | Audit log; correlation ids |
| **Information Disclosure** | Cross-tenant read; IDOR channel; Member em `/admin/*`; wallpaper/accent de outro usuário | RLS + membership + `RequirePermission`; preferência visual só do caller (B-185); matriz [`authz-matriz.md`](authz-matriz.md); testes security |
| **Denial of Service** | Flood de mensagens/hubs | Rate-limit Redis; limites de payload; timeouts |
| **Elevation of Privilege** | Guest→admin; bypass membership; typing sem `message.send` | AuthZ centralizada; least privilege; reviews |

## Superfícies de ataque

1. **HTTP API** — IDOR, mass assignment, injection
2. **SignalR hub** — join em grupos sem authZ; message spoofing
3. **Uploads MinIO** — MIME spoofing, zip bombs, URLs pré-assinadas vazadas
4. **Keycloak** — realm misconfig, clients públicos mal configurados;
   Identity Brokering OIDC/SAML (ACS/redirect, audience, certificado,
   account linking) — B-164
5. **Postgres** — conexão sem RLS context; app como owner/superuser/`BYPASSRLS`;
   policy sem `FORCE`/`WITH CHECK`
6. **Redis** — sem AUTH em rede exposta; flush
7. **AI provider** — exfiltração de contexto de prompts
8. **Supply chain** — deps npm/nuget
9. **Admin settings / integrações** — leitura de secrets por membro; escrita de tokens via API (R-17)
10. **Auditoria de conversa** — leitura privilegiada de DMs/soft-deletes por quem não é membro do canal (R-18)
11. **Export de workspace** — download ZIP com conteúdo (incl. soft-delete) por quem não é `workspace.admin` ou cross-tenant
12. **Retenção / purge** — hard-delete prematuro ou cross-tenant via settings/job; bypass do kill switch de processo
13. **Importação** — archive hostil, mass assignment, autoria/papel falso e
    exaustão de storage
14. **Support bundle/repair** — exfiltração de secret/PII e ação administrativa
    destrutiva
15. **Link preview / egress HTTP** — SSRF via URL em mensagem (B-091 / ADR-021)
16. **Web Push** — prévia em tela bloqueada; chave VAPID vazada; push após sair do canal (B-095 / ADR-022)
17. **Group DM** — vazamento de histórico ao adicionar participante; peer de outro tenant/workspace; hub após sair (B-101 / ADR-023)
18. **Membros de canal privado** — add de usuário de outro workspace/tenant; membro removido continua lendo histórico ou no hub (B-186)
19. **Grupo de contatos** — departamento criado por membro; leitura de grupo pessoal alheio; tratar o grupo como ACL de canal/DM (B-166)

## Controles mínimos obrigatórios (fase 1)

- [x] TLS em trânsito (terminação no proxy ou HTTPS direto) — referência Compose profile `proxy` (W5-2)
- [x] Secrets de AI/SMTP só via env/secret store; API admin devolve máscara (`••••last4` / `configured`) — B-069
- [x] `TenantContext` + catálogo inicial RLS — W3-1/B-009
- [x] Role runtime separada + FORCE/WITH CHECK + teste com credencial real —
  `SEC-RLS-RUNTIME`
- [x] AuthZ nas entradas de hub/API existentes — W3-2; toda entrada nova reabre a obrigação de teste
- [x] Idempotency no envio — B-004
- [x] Limite de tamanho de body validado antes do banco — B-078 / W7-5
- [x] Rate limiting por usuário/IP nos caminhos de send/hub — B-028
- [x] Headers de segurança completos — CSP compartilhada em `infra/nginx/security-headers.conf`
  (proxy profile `proxy` + container web); lab inclui localhost Keycloak/MinIO; B-077 / W7-4 Done
- [x] Dependabot/Renovate ou equivalente — B-076 / W7-3
  (`.github/dependabot.yml` + [`dependencias.md`](../operations/dependencias.md))
- [x] Testes em `tests/security` para cross-tenant (API + hub T3 `JoinChannel`/`SendTyping`)

### R-17 — Secrets/webhooks expostos a membros

| Item | Controle |
|------|----------|
| Leitura | `GET /admin/settings` exige `workspace.admin`; membro/Auditor → 403 |
| Escrita | `PUT /admin/settings` mesma authZ; rejeita secrets no body (`SecretsNotWritable`) |
| Rotação | `POST /admin/settings/credentials/{openrouter\|smtp\|webhook}/rotate` — `workspace.admin`; body `{ value }`; resposta só máscara/versão |
| Re-encrypt | `POST /admin/settings/encryption/reencrypt` — regrava envelopes para `ActiveKeyVersion` |
| Resposta | Nunca retorna secret em claro; só máscara / `*Configured` / `keyVersion` / `rotatedAt` |
| SoT | Infra (Postgres, IdP, MinIO, keyring) em env; produto (kill switches, VAPID, OpenRouter, SMTP, webhook) no DB quando `RuntimeSettings:DatabaseOverridesEnabled` (B-187 / ADR-020); chave mestra só no env |
| Em repouso | AES-256-GCM + AAD tenant/workspace/kind; dual-read temporário do webhook plaintext legado |
| Audit | `settings.change`, `settings.credential.rotate`, `settings.encryption.reencrypt` — sem valor/ciphertext/nonce/tag |
| Webhooks | Delivery via outbox + HMAC depois do realtime (B-048 / B-108 / ADR-026). Eventos assináveis: `MessageCreated` (default), `MessageEdited`, `MessageDeleted`, `ReactionChanged`. Até 5 endpoints. Filtro de canal só aceita id do tenant. Ping `WebhookTest` não grava mensagem. URL `https` ou localhost de lab; sem redirect; timeout 5s. Falha HTTP não reprocessa o outbox da mensagem. Secret só máscara |
| Flag | Sem endpoint = sem fan-out. Eventos novos só se o admin assinar. Sem kill switch global (preserva B-048) |

### R-18 — Auditoria de conversa (break-glass de leitura)

| Item | Controle |
|------|----------|
| AuthZ | `GET /admin/conversations*` e `/admin/threads/*/messages` exigem `admin.dashboard` |
| Escopo | Só canais/threads do `tenant_id` do actor; cross-tenant → 403 |
| Membership | Bypass de `channel_members` **apenas** nesses endpoints admin |
| Membro | Sem `admin.dashboard` → 403 (não vê body soft-deleted nem DMs alheias) |
| Histórico normal | Continua redigindo body deletado + ACL de canal |
| Dashboard/health/version | `GET /admin/dashboard`, `/admin/health-summary`, `/admin/version` também exigem `admin.dashboard` (B-175); membership sozinha → 403 |

### Export de workspace (B-046)

| Item | Controle |
|------|----------|
| AuthZ | `GET /admin/workspaces/{id}/export` exige `workspace.admin` (não só `admin.dashboard`) |
| Escopo | Workspace do `tenant_id` do actor + membership; cross-tenant → 403 |
| Conteúdo | Soft-deleted bodies incluídos; anexos só metadados (sem `storageKey`/bytes MinIO) |
| Audit | `workspace.export` em `audit.audit_events` |
| Membro/Auditor | 403 |

### Importação assistida (B-153 / ADR-030)

| Item | Controle |
|------|----------|
| Flag | `Features:Import:Enabled` off default; rotas → 404 `ImportDisabled` |
| AuthZ | `workspace.import` (mesmo conjunto de `workspace.admin`); Member/Auditor/Moderator → 403 |
| Tenant | `tenantId` do arquivo é ignorado; o job usa o tenant do workspace do ator |
| Papel | Fora de Member/Moderator/Auditor/Admin → 422 `ImportRoleForbidden`, sem gravar |
| Staging | `plan` não cria canal nem mensagem; `execute` só escreve `import.*` |
| Conteúdo | Autor externo sem membership; anexo hostil em quarentena, sem objeto MinIO |
| Fan-out | Publicação não emite `MessageCreated` (sem push, webhook ou fetch de URL do histórico) |
| Relatório | Contagens e códigos; sem body, e-mail, payload ou segredo |
| RLS | `import.jobs`, `import.id_map`, `import.historical_principals` com FORCE + WITH CHECK |
| Rollback | Pós-publicação exige `confirm`; só apaga o que este job criou |

### Diagnóstico e support bundle (B-154)

| Item | Controle |
|------|----------|
| Flag | `Features:SupportBundle:Enabled` off default; criar bundle → 404 `SupportBundleDisabled`. Preflight permanece |
| AuthZ | `support.read` para Admin/Auditor; `support.bundle` e `support.repair` só para admin de workspace. Member → 403 |
| Audience | `PlatformOwner` vê endpoint; admin de workspace recebe evidência mascarada |
| Conteúdo | Manifesto allowlisted. Scrub de body, e-mail, token, connection string e senha. Checksum `sha256` |
| TTL | 15 minutos e no máximo 3 downloads; depois 410 `SupportBundleExpired` |
| Probe | E-mail e push sintéticos, sem rede. Storage grava e apaga objeto `diagnostics/probe-*` |
| Repair | Allowlist `search.reindex` e `membership.reconcile`. `target` arbitrário → 400. Dry-run não escreve `search_vector` |
| Tenant | Tabelas `support.bundles` e `support.repair_jobs` com FORCE RLS. Bundle de outro tenant → 404 |
| Audit | `support.bundle.create`, `support.probe`, `support.repair*` sem body nem secret |

### Templates de workspace (B-115)

| Item | Controle |
|------|----------|
| AuthZ | validate/preview/apply/import/export/onboarding exigem `workspace.admin` |
| Tenant | O manifesto não traz `TenantId`; o recurso criado usa o tenant do contexto |
| Conteúdo | Allowlist declarativa (spaces, canais, tópico, política de mensagem). Campo extra, membro, secret ou schema desconhecido → 400 |
| Dry-run | Preview e `dryRun` não gravam. Conflito no apply → 409 e rollback da transação |
| Export | Sem membros, e-mails, tokens ou tenant. DM/GroupDm ficam de fora |
| Audit | `space.create` / `channel.create` por recurso, mais `template.apply` / `template.import` / `template.export` |
| RLS | `directory.workspace_templates`, `workspace_onboarding`, `template_applications` com FORCE |

### Retenção / purge (B-047)

| Item | Controle |
|------|----------|
| AuthZ | `retention.*` via `GET/PUT /admin/settings` exige `workspace.admin` (membro/Auditor → 403) |
| Kill switch | Processo `MessageRetention:Enabled` off default (SoT DB de instância com overrides — B-187); sem ele o worker não hard-deleta |
| Escopo | Purge só mensagens do tenant da política; `TenantContext` no job |
| Cascata | Remove reactions; detach `attachments.MessageId` (sem delete MinIO neste slice) |
| Audit | `message.purge` em `audit.audit_events` |

### B-114 / ADR-029 — Histórico de edição e movimentação

| Item | Controle |
|------|----------|
| Flag | `Messaging:History:Enabled` off default; history/move → 404 `HistoryDisabled` |
| Versão | Body anterior em `messaging.message_versions` com RLS FORCE; o vivo fica na mensagem |
| Leitura | `message.history.read`; política `historyEnabled=false` → 403 `HistoryHidden`; admin break-glass auditado, sem body no audit |
| Move | `message.move` + membership nos dois canais do mesmo workspace; outro tenant não resolve o destino |
| Tombstone | Corpo `<system:moved>` sem id de destino. Quem não é membro não recebe id, seq nem body do destino |
| Seq | Origem conserva o seq; destino recebe identidade e seq novos |
| Export / purge | Versões entram no ZIP; soft-delete as preserva; purge de retenção remove versões e moves |
| Hold | B-129 ainda não suspende o purge; as linhas existem para esse hold |

### B-107 / ADR-025 — Política de edição e exclusão

| Item | Controle |
|------|----------|
| AuthZ | Avaliação no servidor (permissão + papel + janela). UI só esconde a ação |
| Escrita | `PUT /admin/settings` exige `workspace.admin`; membro/Auditor → 403 |
| Leitura | `GET /channels/{id}/messaging-policy` é não secreta e exige `message.read` no canal; outro tenant → 403 |
| Override de edição | `message.edit.any` só com `allowModeratorOverride` default **false** |
| Override de exclusão | `message.delete.any` com default **true** (preserva moderação atual) |
| Janela | `0` e valores fora de 1…525600 rejeitados (`InvalidMessagingPolicy`) |
| Soft-delete | Body permanece na linha (ADR-018) para o snapshot futuro de B-169; este item não copia body para o audit |
| Rollback | Sem linha na tabela = default atual; sem kill switch que ignore a checagem |

### B-112 — Anúncios e confirmação de leitura

| Item | Controle |
|------|----------|
| Publicação | Canal `Announcement`: `message.send` não basta; exige `announcement.publish`. Membro/Auditor/Guest/Bot → 403 |
| Leitura | Mesma ACL do canal. Relatório de confirmações exige `announcement.publish` ou `workspace.admin` |
| Isolamento | `messaging.announcements` e `messaging.announcement_acknowledgements` com FORCE RLS. Outro tenant ou outro canal não entra na lista |
| Idempotência | Unique `(TenantId, MessageId, UserId)` — confirmação repetida não duplica a contagem |
| Prova | Confirmação não é assinatura legal; o texto da UI deixa isso explícito |
| Fan-out | Relatório e inbox paginam (máx. 50). Contagem é agregada |

### B-091 / ADR-021 — Link preview / SSRF

| Item | Controle |
|------|----------|
| Egress | Só `http`/`https`; IP privado/loopback/link-local/metadata recusado após DNS e cada redirect |
| Redirect | Máx. 3; `AllowAutoRedirect=false` + revalidação; `ConnectCallback` só a IPs públicos |
| Limites | Timeout `LinkPreview:TimeoutMs` (default 8s); HTML ≤ 1,5 MB / imagem ≤ 512 KB; sem cookies/creds |
| Isolamento | Cache por `TenantId`+`UrlHash`; imagem no MinIO do tenant; kill switch processo + admin |
| AuthZ remoção | Autor ou `workspace.admin`; alheio → 403 |
| Hot path | Fetch só no worker via outbox `MessageCreated` |

### B-095 / ADR-022 — Web Push / VAPID

| Item | Controle |
|------|----------|
| Kill switch | `Push:Enabled` off default (SoT DB de instância com overrides — B-187); sem chaves VAPID o sender é no-op |
| Secrets | Privada em envelope no DB; API devolve só a pública; nunca logar `auth`/`p256dh`/privada |
| Destinatários | Membership **no envio**; DM ou menção direta; autor excluído |
| Cursor | `read_cursors.lastReadSeq >= sequence` suprime (já lido noutro dispositivo) |
| Payload | Remetente + canal + prévia truncada; clique abre `/app?channel=&message=&seq=` |
| Revogação | 410/404 remove a assinatura; DELETE só do actor; sair do canal não recebe |
| Hot path | Worker via outbox `MessageCreated`; falha não reprocessa outbox |
| Opt-in | Permissão só após ação do usuário (primeiro envio); “agora não” persiste |

### B-101 / ADR-023 — Group DM

| Item | Controle |
|------|----------|
| Kill switch | `Directory:GroupDm:Enabled` off default; create/add/leave/rename → 404 se off |
| Membership | Só participante ativo (`LeftAt == null`); não-membro → 403 no history e no `JoinChannel` |
| Histórico | `seq > JoinedSeq`; quem entra depois não lê o que veio antes |
| Saída | `LeftAt`/`LeftSeq`; evict do grupo SignalR; history/hub 403 dali em diante |
| Peers | Mesmo workspace/tenant; peer de outro tenant → 403 |
| Limite | 3–`MaxParticipants` (default 9); 10 → 400 `GroupDmTooManyParticipants` |
| Get-or-create | `ParticipantSetKey` ordenado + índice único; add em DM 1:1 cria **outra** conversa |
| Rollback | Flag off no mesmo binário; migration só em lab |

### B-186 — Membros do canal

| Item | Controle |
|------|----------|
| Superfície | Só canal `Private`; público/DM/GroupDm → 400 `ChannelMembershipNotPrivate` |
| Quem gere | Criador ainda membro ou `channel.manage` (admin/owner). Member comum → 403 |
| Alvo | `workspace_members` do mesmo workspace; outro tenant/workspace → 403 |
| Último gestor | Não sai nem é removido (`409 LastChannelManager`) enquanto for o único criador ou holder de `channel.manage` |
| Saída | `LeftAt`; history e `JoinChannel` 403; evict do grupo SignalR |
| Quem entra | `JoinedSeq = 0` (vê o histórico). Reentrada limpa `LeftAt` na mesma linha |
| Audit | `channel.member.add` / `channel.member.remove` / `channel.member.leave` |

### B-040 / ADR-024 — Guests por convite

| Item | Controle |
|------|----------|
| Kill switch | `Directory:Invites:Enabled` off default; create/list/revoke/accept → 404 se off |
| Membership | Só `ChannelMember`; **nunca** `WorkspaceMember`. Workspace endpoints → 403 |
| Token | SHA-256; valor cru uma vez; gasto/expirado/revogado/ausente → 410 idêntico. Aceite seta `app.invite_token_hash` (SET LOCAL) só para revelar a linha do hash; escrita continua exigindo `TenantId` |
| E-mail | Opcional; mismatch → mesmo 410 (não revela o canal) |
| Revogação | `LeftAt` + evict SignalR + `AccessRevoked` |
| Escala | Guest sem `workspace.read`, busca, DM, admin, convite, criar canal |
| Rollback | Flag off no mesmo binário; migration só em lab |

### B-109 / ADR-027 — Token de integração

| Item | Controle |
|------|----------|
| Kill switch | `Integrations:Bots:Enabled` off default; admin e send → 404 `IntegrationDisabled` |
| Segredo | SHA-256; valor cru só em create/rotate; GET não devolve; bearer `vc_int_` não passa pelo parser JWT |
| Lookup | `app.integration_token_hash` (SET LOCAL) revela só a linha do hash; WITH CHECK continua no tenant |
| Escopo | Lista explícita de canais; fora do escopo, DM sem `allowDms` ou canal de outro tenant → 403 |
| Revogação | `RevokedAt` → 401 na hora. `Enabled=false` → 403 com token ainda válido |
| Escrita | Reusa `SendMessage` (idempotência prefixada por bot, `seq`, outbox). Sem leitura de histórico |
| Rate-limit | Chave Redis própria por bot, mesmo teto de send do tenant |
| Rollback | Flag off no mesmo binário. `Down` remove só as tabelas do bot; mensagens ficam |

### B-110 / ADR-028 — Plugin local

| Item | Controle |
|------|----------|
| Kill switch | Mesma flag `Integrations:Bots:Enabled`; admin de plugin e send → 404 se off |
| Código | Manifesto é JSON. Campo extra, schema errado ou capability fora de `messages.send` → 400. Nenhum DLL/JS é carregado |
| Identidade | 1:1 com bot de B-109. O instalador não empresta o próprio papel |
| Segredo | Token só na install/rotação. GET não devolve. Uninstall revoga na mesma transação |
| Isolamento | RLS FORCE em `integrations.plugins`. Id de outro workspace → 404. Member → 403 |
| Disable | `enabled=false` bloqueia send novo (403) e preserva histórico |
| Capacidade | Máximo 20 plugins por workspace |
| Rollback | Flag off no mesmo binário. `Down` remove só `integrations.plugins`; bots e mensagens ficam |

## Ameaças priorizadas para a fatia vertical

1. Leitura/escrita cross-tenant
2. Join SignalR em canal (`JoinChannel`) sem membership / grupo sem namespace de tenant
3. Replay/duplicação abusiva sem rate-limit
4. Token leak no frontend (storage inseguro / logs)
5. Exposição de AI/SMTP secrets a membros (R-17 / B-069)
6. Abuso de auditoria de conversa fora do tenant / por membro (R-18 / B-067)
7. Abuso de export ZIP fora do tenant / por não-admin (B-046)
8. Purge hard-delete sem authZ / kill switch / isolamento de tenant (B-047)
9. SSRF via link preview (B-091 / ADR-021)
10. Prévia de push em tela bloqueada / VAPID vazada / push pós-saída (B-095 / ADR-022)
11. Vazamento de histórico de Group DM / peer cross-tenant / hub após sair (B-101 / ADR-023)
12. Add cross-workspace ou leitura/hub após sair de canal privado (B-186)
13. Guest escalando para o workspace / enumeração de convite (B-040 / ADR-024)

### B-167 — Perfil público do membro

| Item | Controle |
|------|----------|
| AuthZ | Leitura exige membership no workspace. Escrita só em `/me/profile` e `/me/profile/avatar`. Outro workspace, guest ou quem não está no diretório → 403. Alvo fora do workspace → 404 |
| Dados | Cargo, sobre, destaque e avatar em `identity.member_profiles` com FORCE RLS. `DisplayName` permanece na identidade global |
| Avatar | Allowlist PNG/JPEG/WEBP/GIF, 2 MiB, magic bytes, object key `{tenant}/profiles/{user}/`. Leitura só se a key pertence ao par tenant+usuário |
| Audit | `profile.update` e `profile.avatar` sem o texto nem o binário |
| PII | Conteúdo controlado pelo usuário. Não segue para provedor de IA nesta fatia |

### B-116 — Status personalizado

| Item | Controle |
|------|----------|
| AuthZ | Leitura no workspace exige membership + `message.read`. Escrita só do próprio. Outro workspace/tenant → 403 |
| Conteúdo | Texto e emoji são PII controlada pelo usuário. Máx. 80 / 16. Sem inferência de produtividade |
| Agenda | Flag `Features:AvailabilityCalendar:Enabled` default false. Resposta não traz eventos nem janela de DND |
| Notificação | DND de B-097 continua suprimindo push. Status “disponível” não fura o DND |
| Abuso | `POST .../status/report` audita sem o texto. `DELETE` admin audita `user_status.clear` sem o texto |
| Realtime | `UserStatusChanged` só no grupo do tenant, sem dado de agenda |

### B-113 — Agendamento e lembretes

- Claim cross-tenant só com `app.job_role = schedule` (FORCE RLS). API de usuário não seta esse GUC.
- Disparo revalida membership e `message.send`. Sem acesso, não cria mensagem e a notificação ao autor não inclui canal nem corpo.
- Lembrete e evento `scheduled_message.due` vão ao grupo SignalR do usuário, não ao grupo do canal.
- Web Push do lembrete/falha respeita preferência, DND e ocultar prévia.
- Retry do worker reusa a idempotency key do envio; cancelamento compete com o claim na linha `Pending`.

## O que está fora (por ora)

- E2EE client-side (mensagens são legíveis ao servidor por design self-host)
- Formal verification
- Bug bounty

## Superfícies do horizonte (ainda não implementadas)

As superfícies abaixo não descrevem o runtime atual. São controles obrigatórios
dos itens `Planned` W11–W17 antes de merge, conforme a classe R3.

| Superfície | Ameaça dominante | Controle mínimo decidido |
|------------|------------------|----------------------|
| RAG/embeddings | Conteúdo revogado continuar recuperável; ACL desatualizada | D-22; delete propagation, ACL no retrieval, audit e opt-in |
| Workflows | Loop, replay, privilégio transitivo e ação sem owner | Idempotência, depth/rate limits, identidade e capability por ação |
| Conectores/bridges | SSRF, secret leak, impersonation e cópia fora do tenant | D-21; egress policy, scopes, HMAC/OAuth, consentimento e audit |
| Registry de plugins | Supply-chain compromise e pacote revogado continuar ativo | D-18; assinatura, provenance, revisão, revogação e kill switch |
| Legal hold/DLP | Preservação indevida, abuso de busca e conflito com exclusão | D-23; authZ separada, cadeia de custódia e parecer legal |
| Offline/mobile | Token e conteúdo persistidos no dispositivo; revogação tardia | D-20; secure storage, remote logout e contrato de sync |
| Federação | Perda de soberania, retenção e controle de identidade | D-21; trust domains, allowlist e política de cópia |
| Live media | Gravação sem consentimento, abuso e exaustão de SFU/TURN | D-19; consentimento visível, quotas, moderação e SLO |
| E2EE | Recuperação de conta, moderação e compliance incompatíveis | D-26; modelo formal antes de qualquer implementação persistente |
| Canvas colaborativo | AuthZ por bloco, conflito, histórico e export incompletos | D-17; modelo de permissão e retenção antes de CRDT/OT |
| Migração/import | Archive hostil, duplicação, papel falso e cross-tenant | B-153; staging, dry-run, adapters versionados, quarentena local e quotas |
| Backup de chat / destinos | Credencial de destino vazada; exfiltração; import hostil; confusão com dump de infra | B-172; flag off default, secrets mascarados, dry-run, audit, destinos OSS |

Ver também: [`multi-tenant.md`](multi-tenant.md) e
[`ciclo-vida-dados.md`](ciclo-vida-dados.md),
[`horizonte-ambicioso.md`](../roadmap/horizonte-ambicioso.md).
