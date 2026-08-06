# Runbooks operacionais — VibeChat (W5-4)

Procedimentos acionáveis para operação **Compose-first** (D-05 / ADR-017). Kubernetes não é obrigatório.

Use estes runbooks em incidente ou mudança. Detalhes de rotina ficam nos guias irmãos:

| Guia | Quando usar |
|------|-------------|
| [`../operacao.md`](../operacao.md) | Rotina, health, alertas, escala, multi-tenant ops |
| [`../dependencias.md`](../dependencias.md) | Dependabot, triagem de bumps, CVE crítica, rollback (B-076) |
| [`../troubleshooting.md`](../troubleshooting.md) | Sintoma → diagnóstico |
| [`../backup-restore.md`](../backup-restore.md) | Backup, restore e drill mensal |
| [`../desenvolvimento.md`](../desenvolvimento.md) | DX local / agentes |

## Índice

| Runbook | Objetivo |
|---------|----------|
| [incidentes.md](./incidentes.md) | Resposta a incidente (P0–P2), comunicação e pós-mortem |
| [backup-restore.md](./backup-restore.md) | Ponte para backup + restore drill |
| [tls-proxy.md](./tls-proxy.md) | TLS / nginx profile `proxy` (W5-2) |
| [upgrade.md](./upgrade.md) | Upgrade de versão (apps + migrate + verificação) |

## Princípios

1. Postgres é source of truth; Redis é efêmero
2. Não desabilitar RLS como “fix”
3. Secrets só via `.env` / secret manager (D-04)
4. Sem SLA comercial em self-host (D-08) — RPO/RTO best effort
5. Registrar `correlation_id` / trace id em todo incidente
