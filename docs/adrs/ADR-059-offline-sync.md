# ADR-059 — Sync offline no cliente PWA

- Status: Accepted
- Data: 2026-10-25
- Relacionado: B-143, B-089, B-094, D-20, ADR-056, ADR-057

## Contexto

O shell offline não lia um cache autorizado nem guardava envios com
idempotência estável. Desktop e mobile já carregam o mesmo bundle web. O
protocolo de sync pede cursor opaco, tombstone e rebuild sem transformar `seq`
local em verdade.

## Decisão

1. Flag `Features:OfflineWrite:Enabled` default **false**. Sem chave de cursor
   com pelo menos 16 caracteres, ou com `CHANGE_ME`, a rota responde 404
   `OfflineWriteDisabled`. O envio online não consulta a flag.
2. A PWA é o cliente de referência. Desktop e mobile não ganham endpoint
   privilegiado. Não há Background Sync no service worker.
3. `GET /api/v1/sync` devolve protocolo 1, hora do servidor, cursor HMAC,
   mudanças, tombstones e o motivo de rebuild. O cursor amarra tenant, usuário
   e `deviceId`. Assinatura, dono ou aparelho divergente vira
   `FullResyncRequired` do caller. Cursor vencido (7 dias) vira `CursorExpired`
   e um snapshot recente. Versão diferente de 1 responde 409
   `ProtocolUpgradeRequired`, sem corpos.
4. A janela é limitada (50 itens). O histórico completo continua em
   `GET .../messages`. `seq` segue sendo a ordem. O corpo de tombstone não sai.
5. Comandos reutilizam as APIs atuais. Edição com `expectedVersion` diferente
   responde 409 `MessageVersionConflict` e não grava. Reação com `intent`
   `add`/`remove` não inverte no retry. O cliente só mostra sucesso depois do
   ack.
6. O cache e a fila ficam no IndexedDB, cifrados com AES-GCM e chave
   não exportável por conta e tenant. Sem `crypto.subtle` ou IndexedDB o
   opt-in recusa e não grava texto. Logout apaga o banco local. Revogação total
   devolve `ScopeRevoked` e o cliente limpa a partição.
7. O opt-in é o controle “dados offline neste dispositivo”. Desligar apaga a
   fila e o cache depois de confirmação.

## Alternativas

- Tabela de cursores no Postgres: o HMAC já impede troca de tenant e evita
  migration. A autorização continua na membership, não no cursor.
- Cache em `localStorage` sem cifra: rejeitado. O corpo não fica em claro.
- Last-write-wins na edição: rejeitado. Conflito fica visível.

## Consequências

- Rollback: `Features:OfflineWrite:Enabled=false` e reiniciar a API. A leitura
  online e a PWA seguem. Não há migration.
- A chave de cursor só protege a integridade do cursor. Não é segredo de
  conteúdo e não entra em log.
- Cliente incompatível para na fila e pede atualização. Não reenvia com outra
  chave de idempotência.
