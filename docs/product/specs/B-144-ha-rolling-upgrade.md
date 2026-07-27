# B-144 — HA e rolling upgrade

> Wave 16 · Trilha A/B/C/E/F · Deps: B-146, D-25 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

O profile Standard não atende o objetivo HA nem prova compatibilidade durante
upgrade sem downtime.

## Escopo

- Perfil HA opcional com API/Worker replicáveis.
- PostgreSQL HA/backup conforme runbook OSS e Redis/MinIO adequados.
- Leader election/claim idempotente para jobs.
- Expand/migrate/contract migrations.
- Readiness, drain, rolling upgrade e rollback.
- SLO 99,95%, RPO≤5m e RTO≤30m como objetivo de referência.

## Fora de escopo

- Multi-region active-active/write.
- Kubernetes obrigatório.
- SLA comercial.

## Contratos

ADR de topologia baseada em B-146 e ADR-017; compatibility window API/event/db;
version endpoint e migration state.

## UX

Manutenção/degradação visível sem expor internals. Reconnect preserva mensagens e
rascunhos.

## Multi-tenant e authZ

HA não altera isolation. Failover tests incluem TenantContext/RLS e no
cross-tenant cache.

## Aceite

- [ ] Rolling upgrade mantém send/history.
- [ ] Worker failover não duplica efeito.
- [ ] DB failover atende alvo medido.
- [ ] Rollback compatível funciona.
- [ ] Standard continua suportado.

## Testes

Chaos/failover, mixed-version contract, migrations, load SLO, restore e security.

## Riscos

Complexidade operacional prematura. Implementar apenas topologia comprovada por
B-146 e manter Standard como baseline.

