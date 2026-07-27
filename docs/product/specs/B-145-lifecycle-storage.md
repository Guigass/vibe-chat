# B-145 — Lifecycle, quotas de storage e CDN

> Wave 16 · Trilha A/C/E · Deps: B-131, B-134 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Anexos, previews, exports e gravações podem crescer sem limite e comprometer a
instância.

## Escopo

- Usage accounting por tenant/workspace/tipo.
- Soft/hard quotas e alertas.
- Lifecycle hot→archive→delete conforme retention/hold.
- Orphan reconciliation e garbage collection segura.
- CDN opcional para objetos Clean, com signed URLs.
- Storage provider interface e migration runbook.

## Fora de escopo

- CDN obrigatório.
- Apagar objeto em legal hold.
- Expor bucket público.

## Contratos

Usage ledger reconciliável, lifecycle jobs idempotentes, object state/version e
events. ADR para provider/CDN quando diferente de MinIO.

## UX

Admin vê uso/tendência, maiores categorias e ações. Usuário recebe erro de quota
antes do upload e preserva rascunho.

## Multi-tenant e authZ

Accounting e object keys por tenant; signed URL revalida membership. Operator
metrics agregadas sem conteúdo.

## Aceite

- [ ] Quota concorrente não ultrapassa hard limit.
- [ ] Hold impede delete.
- [ ] GC não remove objeto referenciado.
- [ ] CDN URL expira e não enumera key.
- [ ] Reconciliation corrige ledger.

## Testes

Concurrent reservation, GC property tests, retention/hold, URL security,
provider migration e load.

## Riscos

Data loss por GC/lifecycle. Mark-and-sweep conservador, dry-run, grace period e
audit.

