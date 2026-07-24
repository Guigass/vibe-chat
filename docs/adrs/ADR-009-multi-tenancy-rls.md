# ADR-009: Multi-tenancy e RLS

## Status: Accepted

## Contexto

VibeChat deve isolar dados entre organizações (tenants) com defesa em profundidade. Bugs de filtro na aplicação não podem vazar mensagens entre tenants. Self-host pode ser single-tenant ou multi-tenant no mesmo cluster.

## Decisão

Adotar **multi-tenancy lógico em banco compartilhado** com:

1. Coluna `tenant_id` em todas as tabelas de negócio
2. **`ITenantContext`** preenchido só a partir do token/auth — nunca do body
3. **Row Level Security (RLS)** no PostgreSQL como segunda linha de defesa
4. Sessão DB configura `app.tenant_id` (ou equivalente) por request/conexão
5. Testes automatizados de isolamento em `tests/security`

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
- **−** Superuser/bypass RLS em migrations exige disciplina operacional
