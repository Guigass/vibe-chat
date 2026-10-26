# ADR-061 — Backup de chat e destinos remotos

- Status: Accepted
- Data: 2026-10-26
- Relacionado: B-172, B-046, B-069, B-031, B-169, D-08, D-28, ADR-020, ADR-009

## Contexto

O dump de Postgres/MinIO (B-031) e o export compliance (B-046) não cobrem uma
tarefa recorrente que grave um pacote de chat num destino escolhido pelo
admin do workspace. Self-host espera SFTP, FTP, SMB, S3, WebDAV e Drive sem
prender a instalação a um vendor.

## Decisão

1. Flag `Features:ChatBackup:Enabled` default **false**. Desligada, as rotas
   respondem 404 `ChatBackupDisabled`. O envio de mensagem não consulta a flag.
2. O pacote é `vibechat.chat.backup.v1`: manifesto com SHA-256 por entrada,
   payload no formato próximo do export B-046 e, se pedido, bytes de anexo.
   Com `contentAuditEnabled=false` não saem corpos nem bytes.
3. Destinos ficam em Administration. O segredo usa o envelope AES-GCM da
   ADR-020. A API só devolve a máscara. Rotate não exige redeploy.
4. S3 usa o client MinIO já adotado (Apache-2.0). SFTP usa SSH.NET 2026.0.0
   (MIT). FTP, SMB e WebDAV usam a biblioteca padrão. SMB testa a conexão TCP
   e não implementa o dialeto completo: uma tarefa SMB que não consiga gravar
   fica `failed`. Drive fica off (`DriveEnabled=false`) e só fala com
   `DriveEndpoint` http(s) que não seja host Google. Esse endpoint é o
   substituto local; a instalação não chama a API do Google.
5. O worker reclama a tarefa com `app.job_role=chatbackup` e grava fora do
   hot path de `SendMessage`. A chave de idempotência é a tarefa mais o
   horário reclamado. Falha de destino vira run `failed`. Três falhas abrem
   o circuito por 60 segundos. A API e o worker seguem.
6. Import pede dry-run e `confirm=true`. Só grava no workspace autenticado.
   Id de mensagem de outro tenant é 403 e não grava. Mensagem nova recebe
   `seq` e outbox `MessageCreated`.

## Alternativas

- Reusar só o ZIP do B-046: não tem tarefa, destino nem binário.
- SDK do Google Drive: rejeitado. Credencial externa e dependência a mais.
- Cliente SMB de terceiros: licença e superfície maiores do que o teste de
  conexão pedido para este corte.

## Rollback

1. `Features:ChatBackup:Enabled=false` e reiniciar API e worker.
2. O chat, o export B-046 e o `backup.sh` seguem.
3. As tabelas `administration.chat_backup_*` podem ficar. O `Down` da
   migration as remove se o rollback de schema for necessário.
4. Pacotes já gravados no destino permanecem sob controle de quem opera
   esse destino. A flag não os apaga.

## Consequências

- RLS `FORCE` nas três tabelas, com bypass de fila só para o job role.
- Drive não é um conector Google. Ligá-lo sem endpoint local não abre rede.
- O admin do workspace escolhe o host. O host de metadata `169.254.169.254`
  e caminho com `..` são recusados.
