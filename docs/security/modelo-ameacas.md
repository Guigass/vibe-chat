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

## Controles mínimos obrigatórios (fase 1)

- [x] TLS em trânsito (terminação no proxy ou HTTPS direto) — referência Compose profile `proxy` (W5-2)
- [ ] Secrets só via env/secret store
- [ ] `TenantContext` + RLS
- [ ] AuthZ em **toda** entrada de hub e API
- [ ] Idempotency + limites de tamanho de body
- [ ] Rate limiting por usuário/IP
- [ ] Headers de segurança no web (CSP básica, etc.)
- [ ] Dependabot/renovate ou equivalente
- [ ] Testes em `tests/security` para cross-tenant

## Ameaças priorizadas para a fatia vertical

1. Leitura/escrita cross-tenant
2. Join SignalR em conversation sem membership
3. Replay/duplicação abusiva sem rate-limit
4. Token leak no frontend (storage inseguro / logs)

## O que está fora (por ora)

- E2EE client-side (mensagens são legíveis ao servidor por design self-host)
- Formal verification
- Bug bounty

Ver também: `multi-tenant.md`.
