# B-132 — Streaming de audit para SIEM

> Wave 14 · Trilha B/E/A · Deps: B-042, B-108 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Audit local não integra com monitoramento e retenção de segurança corporativa.

## Escopo

- Export near-real-time por webhook assinado e syslog/CEF ou JSON padrão.
- Cursor/checkpoint e retry idempotente.
- Filtros por categoria/severidade, nunca por outro tenant.
- Redaction/minimization configurada por policy.
- Backfill limitado e health/lag.
- Rotate/revoke secret.

## Fora de escopo

- Exportar body de mensagem por default.
- Garantir ingestão do SIEM externo.
- Um endpoint compartilhando eventos de vários tenants.

## Contratos

Envelope audit versionado via B-139; delivery log e checkpoint; endpoint
configurado por tenant, HTTPS/TLS e egress protections.

## UX

Admin security configura destino, testa com evento sintético e vê lag/último
sucesso sem visualizar secret.

## Multi-tenant e authZ

Permission `security.audit.export`; config, queue e checkpoints isolados. Test
event não contém dados reais.

## Aceite

- [ ] Retry não duplica eventId.
- [ ] Body/secret não saem por default.
- [ ] Backfill respeita limite e ACL.
- [ ] Destino inválido abre circuit breaker.
- [ ] Cross-tenant impossível.

## Testes

Fake SIEM, signing, retry/checkpoint, SSRF, redaction e security.

## Riscos

SIEM virar canal de exfiltração ou backpressure. Minimização, queue isolada,
limites e circuit breaker.

