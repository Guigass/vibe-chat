# Backup e Restore — VibeChat

Atalho operacional (checklist): [`runbooks/backup-restore.md`](./runbooks/backup-restore.md).

## O que precisa de backup

| Dado | Onde | Prioridade |
|------|------|------------|
| Mensagens, memberships, metadados | PostgreSQL | P0 |
| Anexos | MinIO / S3 | P0 |
| Realms/users IdP | Keycloak (DB embutido ou DB externo) | P0 |
| Configurações / secrets | Vault / arquivos de deploy (fora do git) | P0 |
| Redis | Efêmero | Não backup crítico |
| Dashboards Grafana | Provisionamento em git | P2 |

## Princípios

1. Backup **automatizado** e testado (restore drill)
2. Criptografia em repouso dos artefatos de backup
3. Retenção alinhada à política legal (D-03 / ADR-018: soft-delete default; purge configurável depois)
4. **Sem SLA comercial** em self-host (D-08). RPO/RTO são **best effort**; alvo operacional sugerido: backup diário Postgres → RPO ≤ 24h, RTO ≤ 4h

## Scripts (dev / staging)

```bash
# Backup Postgres (+ MinIO se `mc` estiver instalado)
./infra/scripts/backup.sh
# Artefatos em .backups/<timestamp>/ (postgres.dump, minio/, MANIFEST.txt)

# Drill de restore em DB separado (nunca prod sem confirmação explícita)
CONFIRM_RESTORE_DRILL=yes ./infra/scripts/restore-drill.sh .backups/<timestamp>
```

Variáveis úteis: `BACKUP_DIR`, `RESTORE_DB` (default `vibechat_restore_drill`), `RESTORE_MINIO_BUCKET`.

## PostgreSQL

### Backup

```bash
# Preferir o script acima; equivalente manual:
pg_dump -Fc -d vibechat -f vibechat_$(date +%F).dump
```

Em produção: PITR com WAL archiving quando RPO &lt; 24h for necessário.

### Restore

```bash
# Ambiente limpo / staging primeiro — preferir restore-drill.sh
pg_restore -d vibechat vibechat_YYYY-MM-DD.dump
# Rodar migrações se necessário para alinhar versão da app
```

Após restore: validar RLS, contagem de mensagens, login OIDC.

## MinIO / S3

- Versionamento de bucket recomendado
- `mc mirror` / replicação para bucket secundário
- Backup consistente: ideal snapshot próximo ao dump do DB (metadados ↔ objetos)

Restore parcial: restaurar objetos + linhas `attachments` correspondentes.

## Keycloak

- Preferir **database externo** para Keycloak em produção (backup junto ao Postgres ou instância dedicada)
- Export de realm como complemento (`infra/keycloak`), não substituto do DB

## Procedimento de drill (mensal)

1. Gerar backup com `./infra/scripts/backup.sh` (ou usar artefato offsite)
2. Subir Compose/staging vazio (ou usar DB `vibechat_restore_drill`)
3. Rodar `CONFIRM_RESTORE_DRILL=yes ./infra/scripts/restore-drill.sh <backup_dir>`
4. Apontar API de staging para o DB/bucket de drill (ou restaurar no stack limpo)
5. Checklist:
   - [ ] Login alice/bob (ou users reais de staging)
   - [ ] Histórico do channel visível
   - [ ] Anexo abre
   - [ ] Envio de nova mensagem funciona
   - [ ] RLS ainda isola tenant (smoke security)
6. Registrar tempo de restore (RTO real) e apagar artefatos de drill se contiverem PII

## Desastre completo

1. Provisionar host/Compose
2. Restaurar secrets
3. Restaurar Postgres → MinIO → Keycloak
4. Deploy apps
5. Comunicar usuários; forçar re-login se chaves rotacionadas

## O que não fazer

- Confiar só em volumes Docker sem cópia offsite
- Restaurar backup de prod em dev sem anonimizar (LGPD/PII)
- Restaurar só DB sem MinIO (anexos quebrados) sem avisar
