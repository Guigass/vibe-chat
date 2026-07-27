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
| **Spoofing** | Roubo de token; JWT forjado | OIDC Keycloak; validar issuer/aud/exp; HTTPS; rotação de chaves |
| **Tampering** | Alterar messageId/seq; forjar tenant_id | Constraints DB; tenant só do contexto; assinatura não necessária se SoT é DB |
| **Repudiation** | Negar ação admin | Audit log; correlation ids |
| **Information Disclosure** | Cross-tenant read; IDOR channel | RLS + membership checks; testes security |
| **Denial of Service** | Flood de mensagens/hubs | Rate-limit Redis; limites de payload; timeouts |
| **Elevation of Privilege** | Guest→admin; bypass membership | AuthZ centralizada; least privilege; reviews |

## Superfícies de ataque

1. **HTTP API** — IDOR, mass assignment, injection
2. **SignalR hub** — join em grupos sem authZ; message spoofing
3. **Uploads MinIO** — MIME spoofing, zip bombs, URLs pré-assinadas vazadas
4. **Keycloak** — realm misconfig, clients públicos mal configurados
5. **Postgres** — conexão sem RLS context; SQL admin bypass
6. **Redis** — sem AUTH em rede exposta; flush
7. **AI provider** — exfiltração de contexto de prompts
8. **Supply chain** — deps npm/nuget
9. **Admin settings / integrações** — leitura de secrets por membro; escrita de tokens via API (R-17)
10. **Auditoria de conversa** — leitura privilegiada de DMs/soft-deletes por quem não é membro do canal (R-18)
11. **Export de workspace** — download ZIP com conteúdo (incl. soft-delete) por quem não é `workspace.admin` ou cross-tenant
12. **Retenção / purge** — hard-delete prematuro ou cross-tenant via settings/job; bypass do kill switch de processo

## Controles mínimos obrigatórios (fase 1)

- [x] TLS em trânsito (terminação no proxy ou HTTPS direto) — referência Compose profile `proxy` (W5-2)
- [x] Secrets de AI/SMTP só via env/secret store; API admin devolve máscara (`••••last4` / `configured`) — B-069
- [x] `TenantContext` + RLS — W3-1, B-009 e gaps de catálogo RLS
- [x] AuthZ nas entradas de hub/API existentes — W3-2; toda entrada nova reabre a obrigação de teste
- [x] Idempotency no envio — B-004
- [ ] Limite de tamanho de body validado antes do banco — B-078 / W7-5
- [x] Rate limiting por usuário/IP nos caminhos de send/hub — B-028
- [ ] Headers de segurança completos — básicos presentes; CSP pendente em B-077 / W7-4
- [ ] Dependabot/Renovate ou equivalente — B-076 / W7-3
- [x] Testes em `tests/security` para cross-tenant (API + hub T3 `JoinChannel`/`SendTyping`)

### R-17 — Secrets/webhooks expostos a membros

| Item | Controle |
|------|----------|
| Leitura | `GET /admin/settings` exige `workspace.admin`; membro/Auditor → 403 |
| Escrita | `PUT /admin/settings` mesma authZ; rejeita `apiKey` / `smtpPassword` no body |
| Resposta | Nunca retorna secret em claro; só máscara / `*Configured` |
| SoT | Env / secret store para AI/SMTP; webhook HMAC secret no DB (admin-writable, B-048) |
| Audit | `settings.change` em `audit.audit_events` (inclui `webhooks.*` sem valor do secret) |
| Webhooks | Delivery `MessageCreated` via outbox + HMAC; URL/secret só admin; mask no GET |

### R-18 — Auditoria de conversa (break-glass de leitura)

| Item | Controle |
|------|----------|
| AuthZ | `GET /admin/conversations*` e `/admin/threads/*/messages` exigem `admin.dashboard` |
| Escopo | Só canais/threads do `tenant_id` do actor; cross-tenant → 403 |
| Membership | Bypass de `channel_members` **apenas** nesses endpoints admin |
| Membro | Sem `admin.dashboard` → 403 (não vê body soft-deleted nem DMs alheias) |
| Histórico normal | Continua redigindo body deletado + ACL de canal |

### Export de workspace (B-046)

| Item | Controle |
|------|----------|
| AuthZ | `GET /admin/workspaces/{id}/export` exige `workspace.admin` (não só `admin.dashboard`) |
| Escopo | Workspace do `tenant_id` do actor + membership; cross-tenant → 403 |
| Conteúdo | Soft-deleted bodies incluídos; anexos só metadados (sem `storageKey`/bytes MinIO) |
| Audit | `workspace.export` em `audit.audit_events` |
| Membro/Auditor | 403 |

### Retenção / purge (B-047)

| Item | Controle |
|------|----------|
| AuthZ | `retention.*` via `GET/PUT /admin/settings` exige `workspace.admin` (membro/Auditor → 403) |
| Kill switch | Processo `MessageRetention:Enabled` off default (SoT env); sem ele o worker não hard-deleta |
| Escopo | Purge só mensagens do tenant da política; `TenantContext` no job |
| Cascata | Remove reactions; detach `attachments.MessageId` (sem delete MinIO neste slice) |
| Audit | `message.purge` em `audit.audit_events` |

## Ameaças priorizadas para a fatia vertical

1. Leitura/escrita cross-tenant
2. Join SignalR em canal (`JoinChannel`) sem membership / grupo sem namespace de tenant
3. Replay/duplicação abusiva sem rate-limit
4. Token leak no frontend (storage inseguro / logs)
5. Exposição de AI/SMTP secrets a membros (R-17 / B-069)
6. Abuso de auditoria de conversa fora do tenant / por membro (R-18 / B-067)
7. Abuso de export ZIP fora do tenant / por não-admin (B-046)
8. Purge hard-delete sem authZ / kill switch / isolamento de tenant (B-047)

## O que está fora (por ora)

- E2EE client-side (mensagens são legíveis ao servidor por design self-host)
- Formal verification
- Bug bounty

Ver também: `multi-tenant.md`.
