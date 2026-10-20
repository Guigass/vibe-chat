# ADR-043 — Auditoria de conteúdo por tenant

- Status: Accepted
- Data: 2026-10-20
- Relacionado: B-169, B-064, B-067, B-046, B-107, B-129, D-26, ADR-018, ADR-020, ADR-042

## Contexto

Auditoria de conversa e export leem plaintext, inclusive de soft-delete. O
evento `message.delete` não guardava o body, então o purge apagava o conteúdo
que um tenant de compliance precisava. D-26 torna isso incompatível com canal
E2EE: o tenant escolhe um dos dois eixos.

## Decisão

1. `contentAuditEnabled` fica em `audit.content_audit_settings`, por tenant,
   com RLS FORCE. Sem linha, o valor é `true`. Só `workspace.admin` altera,
   via `PUT /api/v1/admin/settings`. Auditor com só `admin.dashboard` recebe 403.
2. Com a flag ligada, B-067 e o export de workspace devolvem o body. O
   soft-delete grava em `message.delete` os ids, `sequence`, `authorId`,
   `deletedAt` e `body`. O purge da mensagem não apaga o evento.
3. Com a flag desligada, B-067 e o export devolvem body vazio
   (`bodyRedacted: true`). O evento de delete traz os mesmos ids e timestamps,
   sem `body`. A mensagem na conversa do membro não muda.
4. Desligar exige `confirmDisable: true`. Se existir legal hold `active` e não
   expirado no tenant, a API responde 409 `ContentAuditBlockedByLegalHold` e
   audita a recusa. Ligar de novo não reescreve mensagens nem inventa
   plaintext de um ciphertext que o servidor não tem.
5. `type: ConfidentialE2EE` em `POST .../channels` responde 400
   `ContentAuditBlocksE2ee` enquanto a flag está ligada. Com a flag desligada
   a criação responde 400 `ConfidentialE2eeUnavailable`: o protocolo é B-064,
   e persistir o tipo sem crypto guardaria plaintext com rótulo confidencial.
6. A mudança grava `settings.content_audit.change` sem body de mensagem.

## Alternativas

- Default `false`: quebraria B-067 e o export atuais.
- Criar o canal confidencial sem crypto quando a auditoria está desligada:
  o servidor continuaria lendo o texto.
- Apagar snapshots antigos ao desligar: a spec manda avisar que eles permanecem.

## Consequências

Busca, IA e DLP server-side continuam vendo plaintext enquanto a flag está
ligada. B-064 substitui `ConfidentialE2eeUnavailable` pelo canal de verdade.
Desligar a auditoria não é purge.
