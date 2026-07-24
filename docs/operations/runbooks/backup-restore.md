# Runbook — Backup e restore drill

Fonte canônica: [`../backup-restore.md`](../backup-restore.md).

Este arquivo é o **atalho operacional** (W5-4). Não duplica política RPO/RTO.

## Backup sob demanda

```bash
./infra/scripts/backup.sh
# Artefatos: .backups/<timestamp>/postgres.dump (+ minio/ se mc disponível)
```

## Restore drill (mensal — D-08)

```bash
CONFIRM_RESTORE_DRILL=yes ./infra/scripts/restore-drill.sh .backups/<timestamp>
```

Checklist pós-drill (copiar do guia canônico):

- [ ] Login
- [ ] Histórico do channel
- [ ] Anexo abre
- [ ] Envio de mensagem
- [ ] Smoke RLS / `task test:security` em staging se o drill for pré-prod

## Desastre completo

Ver seção “Desastre completo” em [`../backup-restore.md`](../backup-restore.md): secrets → Postgres → MinIO → Keycloak → apps → re-login.
