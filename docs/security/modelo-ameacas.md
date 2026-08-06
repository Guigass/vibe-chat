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

## Controles mínimos obrigatórios (fase 1)

- [x] TLS em trânsito (terminação no proxy ou HTTPS direto) — referência Compose profile `proxy` (W5-2)
- [x] Secrets de AI/SMTP só via env/secret store; API admin devolve máscara (`••••last4` / `configured`) — B-069
- [x] `TenantContext` + catálogo inicial RLS — W3-1/B-009
- [x] Role runtime separada + FORCE/WITH CHECK + teste com credencial real —
  `SEC-RLS-RUNTIME`
- [x] AuthZ nas entradas de hub/API existentes — W3-2; toda entrada nova reabre a obrigação de teste
- [x] Idempotency no envio — B-004
- [ ] Limite de tamanho de body validado antes do banco — B-078 / W7-5
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
| Migração/import | Archive hostil, duplicação, papel falso e cross-tenant | B-153; staging, dry-run, adapters versionados, scan e quotas |
| Diagnóstico/support | Bundle com secret/PII ou repair perigoso | B-154; schema allowlisted, scan, TTL, capability e dry-run |

Ver também: [`multi-tenant.md`](multi-tenant.md) e
[`ciclo-vida-dados.md`](ciclo-vida-dados.md),
[`horizonte-ambicioso.md`](../roadmap/horizonte-ambicioso.md).
