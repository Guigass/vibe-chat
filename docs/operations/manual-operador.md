# Manual do Operador Self-host

Mapa operacional para instalar, manter e recuperar uma instância. Comandos
detalhados permanecem nos runbooks.

## Responsabilidades

- infraestrutura, DNS/TLS e capacidade;
- `.env`/secret manager;
- upgrade/rollback;
- backup/restore;
- observabilidade;
- IdP;
- storage;
- resposta a incidentes.

O operador não usa seu acesso de infraestrutura como autorização de produto.

## Bootstrap

1. revisar requisitos e imagens;
2. copiar `.env.example`;
3. substituir todos os `CHANGE_ME`;
4. manter `SEED_ENABLED=false` em produção;
5. manter IA/retention/live/registry off até configuração;
6. validar `docker compose config --quiet`;
7. subir data plane e profile `apps`;
8. verificar health/readiness;
9. configurar TLS;
10. executar smoke de login/envio;
11. registrar backup inicial.

Fonte: [`operacao.md`](operacao.md) e
[`configuracao-env.md`](configuracao-env.md).

## Profiles

| Profile | Uso |
|---------|-----|
| default/data plane | PostgreSQL, Redis, Keycloak, MinIO |
| `apps` | API, Web, Worker |
| `tools` | ferramentas de desenvolvimento |
| `observability` | collector, métricas, logs e traces |
| `proxy` | referência TLS/nginx |

Produção não expõe Postgres/Redis/MinIO publicamente.

## Secrets

- fora do Git;
- placeholders no template;
- rotação documentada;
- AI/SMTP via env/secret store;
- webhook secret por admin API, mascarado;
- logs/traces sem valor;
- backup de configuração protegido separadamente.

## Rotina

### Diária

- health;
- erro/latência;
- outbox lag;
- uso de disco;
- falhas de backup/job;
- alertas de segurança.

### Semanal

- restore amostral conforme política;
- crescimento de storage;
- dependências/findings;
- plugins/tokens;
- capacidade.

### Por release

- release notes;
- compatibilidade;
- backup;
- migration;
- smoke;
- rollback/roll-forward;
- service worker/web cache;
- observabilidade.

## Incidente

Use [`runbooks/incidentes.md`](runbooks/incidentes.md). Prioridades:

1. cross-tenant/secret/integridade;
2. indisponibilidade do chat;
3. outbox/realtime;
4. storage;
5. componente opcional.

Componente opcional falha fechado ou degrada sem derrubar chat.

## Backup e restore

- PostgreSQL é source of truth.
- Redis é descartável.
- MinIO exige política própria.
- Keycloak/realm/config precisam de backup.
- Restore só é confiável após drill.

Use [`runbooks/backup-restore.md`](runbooks/backup-restore.md).

## Upgrade

Seguir [`release-versionamento-suporte.md`](release-versionamento-suporte.md) e
[`runbooks/upgrade.md`](runbooks/upgrade.md).

Não usar imagem `latest`; registrar tag/digest e matriz de versões.

## Standard e HA

Perfis seguem D-28:

- Basic/Dev: Compose simples e backup diário, sem objetivo de disponibilidade;
- Standard: Compose permitido, 99,9% como objetivo, PITR/WAL, RPO≤1h/RTO≤4h;
- HA: somente após B-146/B-170/B-144, 99,95%, RPO≤5m/RTO≤30m.

Kubernetes não é requisito automático. O operador deve rotular o perfil real e
não anunciar Standard/HA sem restore/failover medidos.

## Ações R4

Exigem owner:

- domínio/DNS real;
- certificado/credencial de produção;
- publicar release;
- comprar serviço/licença;
- alterar branch protection;
- alegação legal/certificação.
