# Segurança Multi-Tenant — VibeChat

## Modelo

**Shared database, shared schema**, isolamento lógico por `tenant_id`, reforçado por RLS (ADR-009).

```text
┌──────────────────────────────────────────┐
│ Camada 1 — Identidade (Keycloak / JWT)   │
│  sub, email, tenant claim mapeada        │
│  (realm roles NÃO autorizam produto)     │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 2 — TenantContext (API/Worker)    │
│  Nunca ler tenant do body/query solta    │
└─────────────────┬────────────────────────┘
                  ▼
┌──────────────────────────────────────────┐
│ Camada 3 — Autorização de domínio        │
│  workspace_members.role (DB) +           │
│  RolePermissionCatalog / membership      │
│  Matriz: docs/security/authz-matriz.md   │
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

**Nota (B-176):** a Camada 1 autentica; a Camada 3 autoriza. Papel de produto
vive em `tenancy.workspace_members.role`, não em claims JWT nem em realm roles
do Keycloak. Atribuir `admin` no IdP não eleva o usuário no VibeChat.
## Regras não negociáveis

1. **Tenant do token/contexto** — ignorar tentativas de override do cliente
2. **Toda tabela de negócio** tem `tenant_id` NOT NULL + índice
3. **Grupos SignalR** no formato `t:{tenantId}:c:{channelId}` (canal) e `t:{tenantId}` (presence)
4. **Redis**: `t:{tenantId}:…` em presence/typing/rate-limit/cache
5. **Object keys**: `{tenantId}/{workspaceId}/…`
6. **Busca** filtra por membership, não só por tenant
7. **AI**: contexto isolado; proibido batch cross-tenant
8. **Jobs do worker** reestabelecem TenantContext por evento

## RLS (orientação)

- Habilitar e **forçar** RLS em toda tabela tenant-aware (`ENABLE` + `FORCE ROW
  LEVEL SECURITY`)
- Policy de SELECT/DELETE usa `USING`; INSERT/UPDATE também usa `WITH CHECK`
  baseada em `app.tenant_id`
- Role runtime da aplicação não é owner, superuser nem possui `BYPASSRLS`
- Migrations rodam com role privilegiada separada e controlada
- API e Worker nunca recebem a credencial de migration/owner
- Após iniciar transação/unit of work: `SET LOCAL app.tenant_id = '…'`
- Ausência/valor inválido de `app.tenant_id` falha fechado

### Roles de banco

| Role conceitual | Uso | Proibido |
|-----------------|-----|----------|
| `vibechat_migrator` | schema, migrations, policies e grants | connection string de API/Worker |
| `vibechat_app` | queries/mutações tenant-scoped | ownership, superuser, `BYPASSRLS`, DDL |
| `vibechat_backup` | backup/restore conforme runbook | uso pela aplicação |

Nomes podem variar por ambiente, mas a separação de autoridade não. Implementação
em `SEC-RLS-RUNTIME` (`RlsSession` + `03-rls.sql` + roles Compose); ver
[`operational-findings.md`](../roadmap/operational-findings.md#sec-rls-runtime).

## Casos de teste obrigatórios

| # | Caso | Resultado esperado |
|---|------|--------------------|
| T1 | User A lista channels do tenant B | Vazio / 403 |
| T2 | User A lê messageId do tenant B | 404/403 |
| T3 | User A subscribe hub conversation do tenant B | Rejeitado — coberto por `Cross_tenant_hub_join_and_typing_are_rejected` (`JoinChannel` + `SendTyping` → `HubException`) |
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
| T16 | Catálogo RLS (`infra/compose/postgres/03-rls.sql`) inclui `messaging.message_retention_settings` | Policy `tenant_isolation_message_retention_settings` em `"TenantId"` (GAP pós B-047) |
| T17 | Catálogo RLS cobre todas as tabelas de negócio com `TenantId` (ex.: `conversation_sequences`, `outbox_messages`, `ai.settings`, `email_settings`) | ENABLE + policy por tabela; arch test `Rls_catalog_covers_all_tenant_scoped_business_tables` (GAP-rls-catalog) |
| T18 | API/Worker consultam como role dona/superuser/`BYPASSRLS` | Preflight/startup falha; credencial recusada |
| T19 | Runtime sem `app.tenant_id` executa SELECT/INSERT/UPDATE/DELETE | Falha fechado / nenhuma linha; nunca acesso global |
| T20 | Runtime tenta gravar `"TenantId"` diferente do `SET LOCAL` | Negado por `WITH CHECK` |
| T21 | Owner/migrator e runtime são comparados no mesmo cenário cross-tenant | Teste da aplicação usa runtime e prova enforcement real |

## Operação multi-tenant vs single-tenant

| Modo | Notas |
|------|-------|
| Single-tenant deploy | Ainda manter `tenant_id` + RLS (mesmo tenant); simplifica upgrades futuros |
| Multi-tenant SaaS self-host | Monitorar noisy neighbor (rate-limit por tenant) |

## Admin e break-glass

- Impersonation (se existir) só para role platform-admin, auditada
- Acesso SQL direto é break-glass — fora do app; playbooks em `operacao.md`
- Nunca compartilhar credenciais de role bypass entre app e humanos no dia a dia

## Cobertura authZ (B-175)

Matriz endpoint × gate (membership vs `RequirePermission` vs condicional):
[`authz-matriz.md`](authz-matriz.md). Inclui API `/api/v1` e hub SignalR.
Superfícies `/admin/*` sensíveis exigem `admin.dashboard` ou `workspace.admin`
— membership sozinha não basta.

## Checklist de review de PR

- [ ] Novos endpoints usam TenantContext?
- [ ] Novas tabelas com RLS + tenant_id?
- [ ] Novas keys Redis/MinIO prefixadas?
- [ ] Teste negativo cross-tenant adicionado?
- [ ] Endpoint novo listado em `authz-matriz.md` com o gate correto?
