# B-152 — Canvas colaborativo

> Wave 17 · Trilha B/C/D/E · Deps: B-120, D-17 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Pages com optimistic concurrency não permitem coedição fluida de planos e
documentos compartilhados.

## Escopo

- Coedição realtime de pages B-120 usando CRDT OSS escolhido por ADR.
- Presence/cursors efêmeros e updates duráveis.
- Snapshots, compaction, version history e restore.
- Offline edits/reconnect e conflict-free merge.
- Comentários/references a messages/tasks/decisions.
- Export, retention, legal hold e quotas.

## Fora de escopo

- Planilha/apresentação.
- Plugin executar código no documento.
- ACL por caractere/bloco no v1.

## Contratos

ADR compara CRDTs, licença, wire format, compaction e compatibility. Documento
tem ACL de page; updates/snapshots tenant-scoped e versionados.

## UX

Collaborator presence, offline/reconnecting, undo local, history e restore.
Reduced motion e keyboard/screen reader considerados no editor.

## Multi-tenant e authZ

Join realtime revalida page ACL; update de usuário removido é rejeitado. Refs
redigem origem inacessível.

## Aceite

- [ ] Dois clients editam sem perda.
- [ ] Reconnect/offline converge.
- [ ] Usuário removido não publica update.
- [ ] Snapshot/restore preserva documento.
- [ ] Export/hold/retention cobrem updates e snapshots.

## Testes

CRDT convergence/property, network partitions, ACL revoke, compaction,
mixed-version compatibility e E2E multi-client.

## Riscos

Complexidade, storage e acessibilidade do editor. Escolher CRDT maduro, limitar
blocos e medir compaction.

