# Findings Operacionais — VibeChat

Fila canônica de problemas observados por Watchdog, QA, Security e UX quando não
são uma feature de produto. Build consulta esta fila antes do roadmap somente
para `Critical`, `High` de segurança/dados ou UX `Alta` no caminho principal.

## Regras

- evidência reproduzível obrigatória;
- um ID estável: `HOTFIX-*`, `SEC-*` ou `OPS-*`;
- não duplicar finding existente;
- `Open` exige ação no repositório;
- `External action` é R4 e não bloqueia trabalho independente;
- `Resolved` cita PR/check que resolveu;
- severidade não sobe por opinião; precisa de impacto demonstrável.

## Abertos

| ID | Categoria | Evidência | Severidade | Status | Próxima ação |
|----|-----------|-----------|------------|--------|--------------|
| OPS-QA-AUDIT | Permissão da automação | Token QA recebe 403 em comment/approve | High | External action | Conceder `pull-requests: write`; `actions:write` apenas se re-run for desejado |
| SEC-REVIEW-TEMPLATE | Cobertura de segurança | `privacy_guard` do template usa caminho interno inexistente e heurísticas de outro stack | High | External action | Substituir pelo prompt versionado `05-security-review.prompt.md` |
| OPS-REQUIRED-CHECK | Branch protection | O check de segurança ainda depende de disciplina do prompt | Critical | External action | Tornar `VibeChat Security Review` um required check |
| OPS-DOC-CHECKER | Integridade documental | Contrato e baseline DOC-006 existem, mas a CI ainda não executa checker offline | Medium | Open | Implementar as regras de `qualidade-documental.md` sem alterar prioridade das waves |
| OPS-PR-DRAFT | Tooling de PR | `open_git_pr` pode criar draft | Medium | Mitigated | Prompts convertem imediatamente para ready; monitorar |

## Resolvidos

| ID | Categoria | Evidência | Severidade | Status | Resolução |
|----|-----------|-----------|------------|--------|----------|
| SEC-RLS-RUNTIME | Isolamento multi-tenant | Roles `vibechat_migrator`/`vibechat_app`/`vibechat_backup`, FORCE+WITH CHECK, `RlsSession` SET LOCAL, testes runtime | Critical | Resolved | #72 (roles/FORCE) + #73 (SET LOCAL + validação de role app) |

## Formato de detalhe

### SEC-RLS-RUNTIME

- Observado em: `compose.yaml`, `.env.example`,
  `infra/compose/postgres/03-rls.sql` e `security/multi-tenant.md`.
- Base/head SHA: baseline documental de 2026-07-27.
- Reprodução: API e Worker recebem `POSTGRES_USER`; o mesmo usuário inicializa o
  banco/tabelas. O catálogo executa `ENABLE ROW LEVEL SECURITY`, mas não `FORCE
  ROW LEVEL SECURITY`, e não cria uma role runtime separada.
- Resultado esperado: processos de aplicação usam role sem ownership,
  `SUPERUSER` ou `BYPASSRLS`; tabelas tenant-aware forçam RLS inclusive ao owner.
- Resultado atual: contrato e Compose não provam que as policies se aplicam à
  role efetiva da aplicação.
- Impacto: defesa em profundidade cross-tenant pode ser ignorada por configuração
  privilegiada mesmo com policies presentes.
- Risk class: R3.
- Owner automático: Security + Infra + Backend.
- Critério de resolução:
  1. ADR-009 e configuração declaram `migration_owner`, `app_runtime` e, se
     necessário, `backup_operator`;
  2. API/Worker conectam como runtime sem ownership/`BYPASSRLS`/superuser;
  3. tabelas tenant-aware usam `ENABLE` + `FORCE ROW LEVEL SECURITY`;
  4. policy possui `USING` e `WITH CHECK`;
  5. contexto usa `SET LOCAL` dentro da transação e ausência falha fechado;
  6. migrations continuam possíveis por fluxo separado;
  7. integration/security tests usam a connection string real de runtime e
     tentam SELECT/INSERT/UPDATE/DELETE cross-tenant;
  8. backup/restore e seed não entregam credencial privilegiada à aplicação;
  9. threat model, catálogo de configuração e runbook de rotação são atualizados.
- Rollback: reverter roles/policies apenas em ambiente de teste; com dados,
  preferir roll-forward de grants sem desabilitar RLS.

```markdown
### OPS-exemplo

- Observado em:
- Base/head SHA:
- Reprodução:
- Resultado esperado:
- Resultado atual:
- Impacto:
- Logs/artefatos:
- Risk class:
- Owner automático:
- Critério de resolução:
```
