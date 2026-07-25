# Segurança Multi-Tenant — VibeChat

## Modelo

**Shared database, shared schema**, isolamento lógico por `tenant_id`, reforçado por RLS (ADR-009).

```text
┌──────────────────────────────────────────┐
│ Camada 1 — Identidade (Keycloak / JWT)   │
│  sub, roles, tenant claim mapeada        │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 2 — TenantContext (API/Worker)    │
│  Nunca ler tenant do body/query solta    │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 3 — Autorização de domínio        │
│  Membership / CanPost / CanAccess        │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 4 — PostgreSQL RLS                │
│  policy: tenant_id = current_setting     │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 5 — Efeitos colaterais            │
│  Outbox payload com tenant_id            │
│  SignalR groups namespaced por tenant    │
│  Redis keys prefixadas por tenant        │
│  MinIO keys prefixadas por tenant        │
└──────────────────────────────────────────┘
```

## Regras não negociáveis

1. **Tenant do token/contexto** — ignorar tentativas de override do cliente
2. **Toda tabela de negócio** tem `tenant_id` NOT NULL + índice
3. **Grupos SignalR** no formato `t:{tenantId}:c:{conversationId}`
4. **Redis**: `t:{tenantId}:…` em presence/typing/cache
5. **Object keys**: `{tenantId}/{workspaceId}/…`
6. **Busca** filtra por membership, não só por tenant
7. **AI**: contexto isolado; proibido batch cross-tenant
8. **Jobs do worker** reestabelecem TenantContext por evento

## RLS (orientação)

- Habilitar RLS nas tabelas sensíveis
- Policy de SELECT/INSERT/UPDATE/DELETE baseada em `app.tenant_id`
- Role da aplicação **sem** atributo bypassrls
- Migrations rodam com role privilegiada controlada
- Após abrir conexão/unit of work: `SET LOCAL app.tenant_id = '…'`

## Casos de teste obrigatórios

| # | Caso | Resultado esperado |
|---|------|--------------------|
| T1 | User A lista channels do tenant B | Vazio / 403 |
| T2 | User A lê messageId do tenant B | 404/403 |
| T3 | User A subscribe hub conversation do tenant B | Rejeitado |
| T4 | Body com tenant_id de B + token de A | Persiste sob A (ou rejeita); nunca sob B |
| T5 | Presigned URL de anexo do tenant B | 403 / objeto inacessível |
| T6 | Search query não retorna hits de outro tenant | Garantido |
| T7 | Membro sem `admin.dashboard` lê `/admin/conversations/*/messages` | 403 |
| T8 | Admin lê DM onde não é `channel_member` (mesmo tenant) | 200 (B-067) |
| T9 | Admin lê canal de outro tenant | 403 |
| T10 | Auditor (`admin.dashboard` sem `workspace.admin`) lê `/admin/settings` | 403 (B-069) |
| T11 | Admin `GET /admin/settings` com webhook ativo | `webhooks.secretMask` só; secret nunca em claro (B-048) |
| T12 | Membro/Auditor chama `GET /admin/workspaces/{id}/export` | 403; só `workspace.admin` (B-046) |
| T13 | Admin exporta workspace de outro tenant | 403 (B-046) |
| T14 | Membro/Auditor altera `retention.*` em `/admin/settings` | 403; só `workspace.admin` (B-047) |
| T15 | Purge com `MessageRetention:Enabled=false` | Nenhuma mensagem hard-deletada (B-047) |

## Operação multi-tenant vs single-tenant

| Modo | Notas |
|------|-------|
| Single-tenant deploy | Ainda manter `tenant_id` + RLS (mesmo tenant); simplifica upgrades futuros |
| Multi-tenant SaaS self-host | Monitorar noisy neighbor (rate-limit por tenant) |

## Admin e break-glass

- Impersonation (se existir) só para role platform-admin, auditada
- Acesso SQL direto é break-glass — fora do app; playbooks em `operacao.md`
- Nunca compartilhar credenciais de role bypass entre app e humanos no dia a dia

## Checklist de review de PR

- [ ] Novos endpoints usam TenantContext?
- [ ] Novas tabelas com RLS + tenant_id?
- [ ] Novas keys Redis/MinIO prefixadas?
- [ ] Teste negativo cross-tenant adicionado?
