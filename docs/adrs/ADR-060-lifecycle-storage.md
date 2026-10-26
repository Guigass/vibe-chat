# ADR-060 — Lifecycle de storage e CDN opcional

- Status: Accepted
- Data: 2026-10-25
- Relacionado: B-145, B-131, B-134, ADR-008, ADR-009, ADR-018, ADR-042

## Contexto

Anexos, prévias, exports e gravações podem crescer sem teto. O MinIO da
ADR-008 continua sendo o único provider. B-134 já tem quota genérica de
`storage`, mas não cobre ledger por tipo, arquivo, GC nem URL assinada.

## Decisão

1. Flag `Features:StorageLifecycle:Enabled` default **false**. Desligada, as
   rotas respondem 404 `StorageLifecycleDisabled` e o upload não consulta o
   ledger novo. A quota de B-134, quando ligada, continua independente.
2. O provider permanece `IObjectStorage` / MinIO. Não há segundo bucket nem
   CDN de fornecedor. A CDN opcional é a própria API: flag
   `CdnEnabled` default false. Sem chave de pelo menos 16 caracteres, ou com
   `CHANGE_ME`, a URL responde 404 `StorageCdnDisabled`.
3. Ledger por tenant, workspace e tipo (`attachment`, `preview`, `export`,
   `recording`). Reserva e uso sobem num `UPDATE` condicional
   `Used + Reserved + delta <= HardLimit`. Sem linha, o upload não bloqueia.
4. Objeto: `hot → archived → purge-pending → purged`. Hold ativo no escopo
   impede purge e GC. Objeto referenciado não entra no GC. Órfão só sai depois
   do período de graça (`GraceHours`, default 24). O job é idempotente pela
   `Idempotency-Key`.
5. A URL da CDN é `/api/v1/storage/cdn?token=`. O token é HMAC do tenant, do
   id e da expiração. Não carrega a object key. A leitura revalida membership.
   Token vencido → 410 `StorageCdnExpired`. Só objeto `Clean` em `hot` ou
   `archived`.
6. Reconciliação iguala o ledger à soma dos objetos vivos. Restore de um id
   com tombstone apaga a linha ressuscitada e não recria o objeto.
7. Evento `storage.lifecycle.changed` leva id, tipo, estado e versão. Sem key
   e sem bytes.

## Alternativas

- CDN externo obrigatório: rejeitado. Fora de escopo e exigiria credencial.
- Apagar objeto em hold: rejeitado. A precedência continua na matriz de dados.
- Contar só a quota genérica de B-134: não separa tipo, órfão nem tombstone.

## Runbook de migração de provider

1. Manter a flag desligada. Os bytes continuam no bucket atual.
2. Copiar pelo client S3 usando a object key opaca. Conferir tamanho.
3. Trocar `MINIO_*` no env e subir a API. O ledger não muda de provider.
4. Rollback: voltar o env anterior e `Features:StorageLifecycle:Enabled=false`.
   As tabelas podem ficar. O upload volta a ignorá-las. Não há bucket público.

## Consequências

- Rollback da feature: flag false e reiniciar a API. Chat e MinIO seguem.
- Tabelas `files.storage_ledgers`, `files.storage_objects`,
  `files.storage_tombstones` e `files.storage_jobs` com RLS `FORCE`.
- A chave da CDN só assina o token. Não entra em log nem na URL.
