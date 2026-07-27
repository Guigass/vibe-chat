# B-129 — Legal hold e eDiscovery

> Wave 14 · Trilha B/C/D/E · Deps: B-046, B-114, D-23 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Retenção/purge não suporta preservação seletiva, cadeia de custódia e busca para
investigações autorizadas.

## Escopo

- Legal hold por workspace, user, channel, conversation ou intervalo.
- Approval segregada para aplicar/remover.
- Purge consulta hold e registra motivo de preservação.
- eDiscovery case com custodians, query, snapshot e export.
- Hashes/manifest, chain of custody e audit imutável.
- UI separada do admin cotidiano.

## Fora de escopo

- Aconselhamento legal ou certificação.
- Hold cross-tenant.
- Alterar conteúdo preservado.

## Contratos

Hold/Case/Export tenant-scoped; policy evaluation transacional no purge; formato
de export versionado. ADR define precedência e proteção de audit.

## UX

Warning forte, dupla confirmação, owner/reason/expiry e histórico. Export mostra
progresso e checksum.

## Multi-tenant e authZ

Permissions `compliance.hold` e `compliance.discovery`, separadas de
`workspace.admin`; acesso break-glass sempre auditado.

## Aceite

- [ ] Conteúdo em hold não é purgado.
- [ ] Fora do hold continua política normal.
- [ ] Remoção reativa purge futuro, não apaga imediatamente.
- [ ] Export tem manifest/checksums.
- [ ] Segregação e cross-tenant são testados.

## Testes

Retention/hold races, authZ segregada, export integrity, audit immutability e
E2E case lifecycle.

## Riscos

Conflito legal e abuso privilegiado. Escopo explícito, segregação, expiry,
justificativa e cadeia de custódia.

