# B-143 — Offline sync e fila local

> Wave 16 · Trilha C/D/E · Deps: B-089, B-094, D-20 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

O shell offline não permite ler cache autorizado nem preparar ações confiáveis
durante perda de rede.

## Escopo

- Cache local limitado de conversas recentes opt-in.
- Fila de send/edit/reaction/task com idempotency keys.
- Sync protocol por cursor/version e conflict policy.
- Estado queued/sent/conflict/failed e retry manual.
- Criptografia local quando plataforma oferece secure key.
- Remote logout/revoke limpa dados.

## Fora de escopo

- Cache ilimitado.
- Acesso offline após revogação indefinida.
- Resolver todo conflito automaticamente.

## Contratos

Sync endpoint incremental com tombstones, server time e compatibility version;
commands reutilizam APIs canônicas. Semântica completa de IDs, ack, cursor,
gap-fill, conflito, rebuild e revogação segue
[`protocolo-sync-realtime.md`](../../architecture/protocolo-sync-realtime.md).

## UX

Banner offline, marcação de pending, conflict resolution e controle “dados
offline neste dispositivo”. Nunca mostra sucesso antes de ack.

## Multi-tenant e authZ

Cache separado por tenant/account. Sync revalida ACL e envia tombstones; logout
limpa storage.

## Aceite

- [ ] Retry não duplica.
- [ ] Ordem local reconcilia com `seq`.
- [ ] Revogação remove dados no próximo contato.
- [ ] Troca de conta não mistura cache.
- [ ] Conflito é visível e recuperável.
- [ ] Cursor expirado executa rebuild sem perder comando local ainda válido.
- [ ] Ack e evento SignalR em qualquer ordem não duplicam item.
- [ ] Operação offline anterior a delete/revogação não ressuscita conteúdo.

## Testes

Network transition/chaos, duplicate/out-of-order, logout/revoke, migrations de
cache e E2E web/desktop/mobile.

## Riscos

Dados sensíveis no dispositivo e conflito silencioso. Cache mínimo, criptografia,
expiry e server-authoritative resolution.

