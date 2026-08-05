# ADR-009: Multi-tenancy e RLS

## Status: Accepted

## Contexto

VibeChat deve isolar dados entre organizações (tenants) com defesa em profundidade. Bugs de filtro na aplicação não podem vazar mensagens entre tenants. Self-host pode ser single-tenant ou multi-tenant no mesmo cluster.

## Decisão

Adotar **multi-tenancy lógico em banco compartilhado** com:

1. Coluna `tenant_id` em todas as tabelas de negócio
2. **`ITenantContext`** preenchido só a partir do token/auth — nunca do body
3. **Row Level Security (RLS)** no PostgreSQL como segunda linha de defesa
4. Unit of work inicia transação e configura `SET LOCAL app.tenant_id`; conexão
   pooled nunca mantém tenant em estado permanente
5. API/Worker usam role runtime separada, sem ownership, superuser ou
   `BYPASSRLS`
6. Tabelas tenant-aware usam `ENABLE` + `FORCE ROW LEVEL SECURITY`; policies de
   escrita possuem `WITH CHECK`
7. Migrations/backup usam credenciais separadas e não disponíveis aos processos
   de aplicação
8. Testes automatizados de isolamento em `tests/security` executam com a role
   runtime real

Modelo **não** usa database-per-tenant na fase 1.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Só filtros na aplicação | Um bug = vazamento |
| Database por tenant | Operação e migrações caras cedo demais |
| Schema por tenant | Complexidade de migração elevada |
| Silo completo (deploy por cliente) apenas | Compatível como modo de deploy, mas não substitui RLS no código multi-tenant |

## Consequências

- **+** Isolamento forte; um cluster serve N tenants
- **+** Testável e auditável
- **−** Toda query/conexão deve setar contexto de tenant
- **−** Roles/grants e migrations exigem bootstrap operacional explícito

## Aderência observada

Implementação alinhada em `SEC-RLS-RUNTIME`:

- roles `vibechat_migrator` (BYPASSRLS), `vibechat_app` (runtime) e
  `vibechat_backup` (leitura);
- API/Worker usam `ConnectionStrings:Database` = app; migrate/seed/RLS usam
  `DatabaseMigrator` / bootstrap;
- `infra/compose/postgres/03-rls.sql` aplica `ENABLE` + `FORCE`, `USING` +
  `WITH CHECK`, helpers `app.current_*` e grants;
- sessão por request via `app.tenant_id` / `app.user_id` / `app.job_role`
  (`RlsSession` + interceptor);
- testes em `tests/security/RlsRuntimeEnforcementTests.cs` usam a connection
  string real de runtime.
