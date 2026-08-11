# B-172 — Backup de chat: export/import, tarefas e destinos remotos

> Wave 16 · Trilha A/B/D/E/G · Deps: B-046, B-069, B-031, D-08, D-28 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Self-host precisa de cópia offsite do chat além do dump local de Postgres/MinIO
(B-031) e do export compliance pontual (B-046). Operadores esperam tarefas
agendadas, download/upload de pacotes e destinos comuns (FTP/SFTP, SMB, Drive,
S3-compatível), sem acoplar a um único vendor.

## Escopo

- Pacote versionado de backup de workspace/chat (`vibechat.chat.backup.v1`),
  reutilizando o shape de B-046 onde fizer sentido; opção de incluir anexos
  (objetos) ou só metadados.
- Export sob demanda (download) e import controlado do mesmo formato (restore
  assistido no tenant alvo, com dry-run e confirmação).
- Tarefas de backup: criar, editar, pausar, executar agora, histórico de runs
  (sucesso/falha, tamanho, checksum, destino).
- Agendamento (cron/intervalo) e retenção de artefatos no destino (contar N /
  dias).
- Destinos pluggáveis, OSS, secrets via ADR-020 / rotate (paridade B-069):
  - SFTP/FTP
  - SMB/CIFS
  - S3-compatível (MinIO, AWS S3, etc.)
  - Google Drive (OAuth; off por default)
  - WebDAV (ex.: Nextcloud)
- UI admin: lista de tarefas, destinos mascarados, último run, alerta de falha.
- Feature flag off por default; audit de create/run/import.

## Fora de escopo

- Substituir B-031 / PITR/WAL (D-28) — backup de infra continua obrigatório.
- Migrar de Slack/Mattermost/Discord — isso é B-153.
- Legal hold / eDiscovery case — B-129.
- Backup contínuo em streaming ou RPO de HA (B-144).
- SDK proprietário fechado como único caminho; preferir protocolos abertos.
- Escrow de chaves E2EE (B-064).

## Contratos

- Formato `vibechat.chat.backup.v1` (manifest + payload + checksums).
- APIs admin tenant-scoped: destinations, jobs, runs, export, import
  (`validate` / `execute`).
- Capability `workspace.backup` (ou reuso estrito de `workspace.admin` + audit).
- Credenciais nunca retornadas em claro; só `configured` / máscara.
- Worker executa jobs fora do hot path de `SendMessage`; idempotência por run.
- ADR se o módulo de destinos sair de Administration/Integrations.

## UX

- Admin configura destino → testa conexão → cria tarefa → vê histórico.
- Export manual gera download; import pede arquivo + dry-run antes de aplicar.
- Falhas mostram motivo sanitizado (sem secret); notificação opcional (e-mail
  se B-043 ligado).

## Multi-tenant e authZ

- Job/destino/artefato amarrados a `tenant_id` + workspace.
- Import não atravessa tenant; restore só no contexto autenticado.
- Cross-tenant e destino apontando para bucket/share de outro tenant → 403.
- Respeitar `contentAuditEnabled` (B-169) no conteúdo privilegiado do pacote.

## Aceite

- [ ] Export/import round-trip do formato `v1` (com e sem anexos).
- [ ] Tarefa agendada grava no destino SFTP e S3-compatível com checksum.
- [ ] Destinos SMB, Drive e WebDAV têm adapter + teste de conexão.
- [ ] Secrets mascarados; rotate sem redeploy.
- [ ] Falha de destino não derruba API/worker; run fica `failed` auditado.
- [ ] Flag off: APIs 404/disabled; UI oculta.
- [ ] Distinto e complementar a B-031 e B-046.

## Testes

- Integration: job + destino fake/localstack; import dry-run e apply.
- Security: cross-tenant, secret leak, path traversal no SMB/SFTP.
- Contract: manifest versionado; checksum mismatch rejeita import.
- Ops: runbook curto apontando diferença vs `backup-restore.md`.

## Riscos

- Dados sensíveis em destino externo (R3) — criptografia em trânsito, opt-in
  Drive, audit, retenção e kill switch.
- Import hostil — dry-run, quotas, scan (alinhar B-131 quando existir).
- Confusão com dump de infra — docs e glossário separam os três caminhos.
