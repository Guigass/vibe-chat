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
| OPS-QA-AUDIT | Permissão/aprovação da automação | MCP COMMENT OK; APPROVE 422 no bot; CI job **QA Approve** (`github-actions[bot]`) adicionado | High | Mitigated | Habilitar org setting “Actions approve PRs”; opcional PAT separado se setting off |
| SEC-REVIEW-TEMPLATE | Cobertura de segurança | `privacy_guard` do template usa caminho interno inexistente e heurísticas de outro stack | High | External action | Substituir pelo prompt versionado `05-security-review.prompt.md` |
| OPS-REQUIRED-CHECK | Branch protection | O check de segurança ainda depende de disciplina do prompt | Critical | External action | Tornar `VibeChat Security Review` um required check |
| OPS-DOC-CHECKER | Integridade documental | Contrato e baseline DOC-006 existem, mas a CI ainda não executa checker offline | Medium | Open | Implementar as regras de `qualidade-documental.md` sem alterar prioridade das waves |
| OPS-PR-DRAFT | Tooling de PR | `open_git_pr` pode criar draft | Medium | Mitigated | Prompts convertem imediatamente para ready; monitorar |
| OPS-DOCS-RACE | Corrida Docs | Merges #72+#73 (~20s) abriram Docs #76+#77; #77 foi re-draftado e ficou CONFLICTING | High | External action | Colar prompts 03/06 atualizados no dashboard (repo já em #78) |

## Resolvidos

| ID | Categoria | Evidência | Severidade | Status | Resolução |
|----|-----------|-----------|------------|--------|----------|
| SEC-RLS-RUNTIME | Isolamento multi-tenant | Roles `vibechat_migrator`/`vibechat_app`/`vibechat_backup`, FORCE+WITH CHECK, `RlsSession` SET LOCAL, testes runtime | Critical | Resolved | PR #72 + #73 — roles/FORCE + SET LOCAL na txn + validação de role app |

## Formato de detalhe

### SEC-RLS-RUNTIME

- Status: **Resolved** — [#72](https://github.com/Guigass/vibe-chat/pull/72)
  (roles/FORCE/WITH CHECK) + [#73](https://github.com/Guigass/vibe-chat/pull/73)
  (`RlsSession` SET LOCAL + commit/rollback da txn + validação de role app).
- Observado em: `compose.yaml`, `.env.example`,
  `infra/compose/postgres/03-rls.sql` e `security/multi-tenant.md`.
- Base/head SHA: baseline documental de 2026-07-27; fechamento 2026-08-05.
- Reprodução (pré-fix): API e Worker recebiam `POSTGRES_USER`; o mesmo usuário
  inicializava o banco/tabelas. O catálogo executava `ENABLE ROW LEVEL SECURITY`,
  mas não `FORCE ROW LEVEL SECURITY`, e não criava role runtime separada.
- Resultado esperado: processos de aplicação usam role sem ownership,
  `SUPERUSER` ou `BYPASSRLS`; tabelas tenant-aware forçam RLS inclusive ao owner;
  GUCs `app.*` via `SET LOCAL` dentro da transação.
- Resultado atual: entregue — `vibechat_app` / `vibechat_migrator` /
  `vibechat_backup`; catálogo FORCE+WITH CHECK; `RlsSession` + testes runtime.
- Impacto (pré-fix): defesa em profundidade cross-tenant podia ser ignorada por
  configuração privilegiada mesmo com policies presentes.
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

### OPS-DOCS-RACE

- Status: **External action** — fix no repositório em
  [#78](https://github.com/Guigass/vibe-chat/pull/78); #77 fechado como
  supersedido. Resta R4: colar prompts 03/06 atualizados no dashboard.
- Observado em: PRs Docs #76 (merged) e #77 (draft → CONFLICTING → closed);
  merges #72+#73 do mesmo `SEC-RLS-RUNTIME` (~20s).
- Reprodução: dois feature PRs do mesmo ID mergeiam em sequência → dois triggers
  Docs → um survivor mergeia → o outro, se draft/órfão, fica `DIRTY`.
- Resultado esperado: um único Docs close ready; extras fechados com
  `superseded by #<n>`; nunca `convert_to_draft` para “pausar”.
- Resultado atual: prompts 03/06 + `operacao-24x7` no repo exigem dedup pré-PR,
  orphan sweep e proíbem re-draft (#78); Watchdog fecha draft/CONFLICTING
  supersedidos. Comportamento no dashboard só muda após colar os prompts.
- Impacto (pré-fix): PR draft bloqueia QA; órfão conflitado exige intervenção
  humana.
- Risk class: R1 (processo/automação).
- Owner automático: Docs + Watchdog.
- Critério de resolução:
  1. prompts 03/06 no dashboard exigem dedup pré-PR, orphan sweep e proíbem
     re-draft;
  2. Watchdog fecha Docs draft/CONFLICTING supersedidos;
  3. um ciclo Docs com merge paralelo do mesmo ID não deixa PR aberto órfão.

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
