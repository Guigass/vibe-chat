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
| [web-push-vapid.md](./web-push-vapid.md) | Gerar/rotacionar VAPID (B-095) |
| [upgrade.md](./upgrade.md) | Upgrade de versão (apps + migrate + verificação) |
| [importacao.md](./importacao.md) | Importação assistida: flag, falha e rollback (B-153) |
| [diagnostico.md](./diagnostico.md) | Preflight, probe sintético e support bundle (B-154) |
| [digests.md](./digests.md) | Digest/catch-up: flag, DND, falha de IA e rollback (B-122) |
| [formularios.md](./formularios.md) | Formulários: flag, retenção das respostas e rollback (B-124) |
| [sso-broker.md](./sso-broker.md) | SSO OIDC/SAML no Keycloak: rótulo, vínculo e rollback (B-164) |
| [scim.md](./scim.md) | SCIM 2.0: token, mapeamento, deprovision e rollback (B-128) |
| [policy-packs.md](./policy-packs.md) | Policy packs: flag, lock, drift e rollback (B-133) |
| [bridges.md](./bridges.md) | Bridges: flag, pin, loop e rollback (B-138) |

## Princípios

1. Postgres é source of truth; Redis é efêmero
2. Não desabilitar RLS como “fix”
3. Secrets só via `.env` / secret manager (D-04)
4. Sem SLA comercial em self-host (D-08) — RPO/RTO best effort
5. Registrar `correlation_id` / trace id em todo incidente
