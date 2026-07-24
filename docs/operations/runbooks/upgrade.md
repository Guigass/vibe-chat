# Runbook — Upgrade de versão

Compose-first (D-05). Sem Helm/K8s obrigatório.

## Antes

1. Ler changelog / ADRs impactados no release
2. Confirmar janela e comunicação aos usuários
3. Backup completo:

```bash
./infra/scripts/backup.sh
# Copiar .backups/<timestamp> para offsite
```

4. Anotar versões atuais (imagens Compose, `GET /api/v1/admin/version` se autenticado admin)

## Ordem de deploy

1. **Postgres** — só se a imagem/major mudar; preferir minor pinado
2. **Migrate** — job/`task migrate` (ou startup seed em Development; em prod preferir job explícito)
3. **Worker** — subir nova versão primeiro (consome outbox compatível)
4. **API** — deploy da nova API
5. **Web** — estáticos / container web
6. **Proxy** — só se `nginx.conf` mudou

```text
backup → migrate → worker → api → web → (proxy)
```

## Verificação pós-upgrade

- [ ] `GET /health` e `GET /ready` Healthy
- [ ] Login OIDC (ou smoke DevAuth só em lab)
- [ ] Listar channels do workspace demo/staging
- [ ] Enviar mensagem; confirmar `seq` e entrega SignalR
- [ ] (Se anexos) upload/download MinIO
- [ ] Outbox: sem crescimento sustentado de `processed_at IS NULL`
- [ ] Grafana: error rate estável

Smoke de carga opcional (lab):

```bash
task load:smoke
```

## Rollback

1. Reverter imagens API/Worker/Web para tag anterior
2. **Não** reverter migrate automaticamente se já aplicou — só com plano de down-migration ou restore
3. Se migrate quebrou dados: restore a partir do backup pré-upgrade ([backup-restore](./backup-restore.md)) em staging primeiro
4. Validar `/ready` + envio de mensagem

## Notas

- Keycloak: upgrade de major exige leitura do guia upstream; realm export em `infra/keycloak` é complemento, não substituto do DB
- Feature flags (ex.: `AI__Enabled=false`) devem permanecer explícitas no `.env` de prod (D-06)
- Após upgrade que mude superfície de auth/RLS: rodar `task test:security` em CI/staging
