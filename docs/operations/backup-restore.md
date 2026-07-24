# Backup e Restore — VibeChat

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
3. Retenção alinhada à política legal (ver decisões pendentes)
4. RPO/RTO definidos pelo operador (sugerido fase 1: RPO ≤ 24h, RTO ≤ 4h)

## PostgreSQL

### Backup

```bash
# Exemplo lógico
pg_dump -Fc -d vibechat -f vibechat_$(date +%F).dump
```

Em produção: PITR com WAL archiving quando RPO &lt; 24h for necessário.

### Restore

```bash
# Ambiente limpo / staging primeiro
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

1. Subir Compose/staging vazio
2. Restaurar Postgres + MinIO (+ Keycloak DB)
3. Subir API/Worker/Web na mesma versão do backup
4. Checklist:
   - [ ] Login alice/bob (ou users reais de staging)
   - [ ] Histórico do channel visível
   - [ ] Anexo abre
   - [ ] Envio de nova mensagem funciona
5. Registrar tempo de restore (RTO real)

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
