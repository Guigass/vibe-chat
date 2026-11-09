# ADR-068 — Canvas colaborativo

- Status: Accepted
- Data: 2026-11-09
- Relacionado: B-152, B-120, D-17, ADR-032

## Contexto

D-17 entrega páginas server-authoritative em B-120 e só então coedição por CRDT.
B-152 pede presença efêmera, updates duráveis, snapshot, compactação, histórico,
restore, merge sem conflito, referências já existentes, export, retenção, legal
hold e cota. A ACL continua sendo a da página.

## Decisão

1. A flag `Features:CanvasRealtime:Enabled` nasce false na instância. Off responde
   404 `CanvasRealtimeDisabled` e não grava estado. O PATCH da página continua
   server-authoritative. Com a flag ligada e um documento já aberto, o PATCH
   altera o título e ignora o body: o texto só muda pelo CRDT.
2. O CRDT é **Yrs** via YDotNet 0.6.0 (MIT), wire **lib0 v1** e offsets **UTF-16**,
   o mesmo contrato do `yjs` 13.6.27 no browser. O documento tem um `Y.Text`
   chamado `body`. O servidor aplica o update, relê o texto e recusa HTML,
   scheme inseguro ou estado acima da cota. Não há planilha nem execução de código.
3. O estado vive em `knowledge.canvas_documents`. Cada update idempotente vai
   para `knowledge.canvas_updates`. A cada 20 updates, ou sob comando, o servidor
   grava `knowledge.canvas_snapshots` a partir do próprio estado e apaga updates
   já cobertos, menos o último (ainda serve a chave de idempotência). Restore
   troca o epoch, recoloca o snapshot e rejeita update do epoch anterior.
   Cliente offline não republica conteúdo de quem perdeu a ACL: o join revalida
   a membership e o update de quem saiu não grava.
4. Cursores ficam num mapa de processo com TTL de 20s. Não entram no banco nem
   no outbox. O evento durável `knowledge.canvas.updated` leva ids, epoch e seq,
   sem título nem body.
5. Export da página inclui epoch, seq, ids de snapshot e SHA-256 do estado.
   Export do workspace inclui as contagens. Purge de página apagada remove
   documento, updates e snapshots. Hold ativo no workspace preserva esses três
   e audita `knowledge.canvas.preserve`.

## Alternativas

- **Automerge** (MIT): CRDT JSON maduro, sem binding .NET estável. Rejeitado.
- **Loro** (MIT): CRDT rico em Rust, sem binding .NET estável. Rejeitado.
- **CRDT caseiro**: a spec pede biblioteca OSS. Rejeitado.
- **Log opaco sem merge no servidor**: o snapshot do cliente poderia apagar
  edição alheia. O servidor precisa aplicar o update para validar o texto.

## Rollback

1. `Features:CanvasRealtime:Enabled=false`. Página nova não abre canvas. O body
   já projetado em `knowledge.pages` continua legível pelo PATCH clássico.
2. O `Down` da migration remove `canvas_documents`, `canvas_updates` e
   `canvas_snapshots`. Páginas e versões permanecem.

## Consequências

- **+** Dois clientes convergem sem versionamento otimista no texto.
- **+** Quem perde a membership não publica, e hold bloqueia a compactação.
- **−** A presença não atravessa réplica. Com a flag de HA desligada, uma API
  basta. O log durável continua no Postgres.
- **−** YDotNet 0.6 ainda declara a API instável. O wire preso em v1 recusa
  outro `wireVersion`.
