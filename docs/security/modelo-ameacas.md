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

## Controles mínimos obrigatórios (fase 1)

- [x] TLS em trânsito (terminação no proxy ou HTTPS direto) — referência Compose profile `proxy` (W5-2)
- [x] Secrets de AI/SMTP só via env/secret store; API admin devolve máscara (`••••last4` / `configured`) — B-069
- [ ] `TenantContext` + RLS
- [ ] AuthZ em **toda** entrada de hub e API
- [ ] Idempotency + limites de tamanho de body
- [ ] Rate limiting por usuário/IP
- [ ] Headers de segurança no web (CSP básica, etc.)
- [ ] Dependabot/renovate ou equivalente
- [ ] Testes em `tests/security` para cross-tenant

### R-17 — Secrets/webhooks expostos a membros

| Item | Controle |
|------|----------|
| Leitura | `GET /admin/settings` exige `workspace.admin` ou `admin.dashboard`; membro → 403 |
| Escrita | `PUT /admin/settings` mesma authZ; rejeita `apiKey` / `smtpPassword` no body |
| Resposta | Nunca retorna secret em claro; só máscara / `*Configured` |
| SoT | Env / secret store para chaves; DB só flags e SMTP não-secreto |
| Audit | `settings.change` em `audit.audit_events` |
| Webhooks | Placeholder `planned` (B-048); sem delivery neste turno |

### R-18 — Auditoria de conversa (break-glass de leitura)

| Item | Controle |
|------|----------|
| AuthZ | `GET /admin/conversations*` e `/admin/threads/*/messages` exigem `admin.dashboard` |
| Escopo | Só canais/threads do `tenant_id` do actor; cross-tenant → 403 |
| Membership | Bypass de `channel_members` **apenas** nesses endpoints admin |
| Membro | Sem `admin.dashboard` → 403 (não vê body soft-deleted nem DMs alheias) |
| Histórico normal | Continua redigindo body deletado + ACL de canal |

## Ameaças priorizadas para a fatia vertical

1. Leitura/escrita cross-tenant
2. Join SignalR em conversation sem membership
3. Replay/duplicação abusiva sem rate-limit
4. Token leak no frontend (storage inseguro / logs)
5. Exposição de AI/SMTP secrets a membros (R-17 / B-069)
6. Abuso de auditoria de conversa fora do tenant / por membro (R-18 / B-067)

## O que está fora (por ora)

- E2EE client-side (mensagens são legíveis ao servidor por design self-host)
- Formal verification
- Bug bounty

Ver também: `multi-tenant.md`.
