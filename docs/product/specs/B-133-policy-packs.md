# B-133 — Policy packs

> Wave 14 · Trilha B/D/E · Deps: B-128, B-130 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Configurar dezenas de políticas individualmente é propenso a erro e difícil de
auditar entre ambientes.

## Escopo

- Packs declarativos versionados: SmallTeam, EnterpriseBaseline, Regulated.
- Cobrir auth, sharing, guests, retention, AI, DLP, uploads e audit.
- Preview/diff/simulation antes de aplicar.
- Lock de controles obrigatórios e overrides explícitos.
- Export/import sem secrets.
- Drift report e upgrade de versão.

## Fora de escopo

- Declarar certificação.
- Executar código.
- Aplicar silenciosamente atualização de pack.

## Contratos

Manifesto `policy-pack.v1`; validator; effective policy resolve pack + overrides;
audit com before/after sem secrets.

## UX

Admin compara impacto, conflitos e controles bloqueados. Rollback restaura versão
anterior quando seguro.

## Multi-tenant e authZ

Somente policy admin. Pack não contém IDs de tenant e aplicação usa contexto.

## Aceite

- [ ] Preview é igual ao resultado aplicado.
- [ ] Retry é idempotente.
- [ ] Secret não exporta.
- [ ] Override proibido falha fechado.
- [ ] Drift é detectado.

## Testes

Golden manifests, policy matrix, apply/rollback, import malicioso e security.

## Riscos

Pack criar falsa conformidade. Nomear como baseline técnico e expor controles
efetivos, não selo.

