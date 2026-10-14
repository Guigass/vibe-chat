# ADR-032 — Páginas de conhecimento server-authoritative

- Status: Accepted
- Data: 2026-10-14
- Relacionado: B-120, D-17, ADR-001, ADR-009, ADR-010, ADR-018

## Contexto

Pins e mensagens não organizam procedimentos curados. D-17 escolhe páginas
server-authoritative antes de qualquer CRDT. B-152 fica para coedição.

## Decisão

1. Coleções e páginas vivem no schema `knowledge` (`collections`, `pages`,
   `page_versions`). PostgreSQL continua source of truth. Não há chunk,
   embedding nem serviço novo.
2. Escopo da coleção é `workspace`, `space` ou `channel`. A ACL do canal nunca
   é mais larga que a membership atual. Workspace e space exigem membership do
   workspace. Guest e bot não recebem `knowledge.*`.
3. Permissões: `knowledge.view` (ler, buscar, exportar a página visível),
   `knowledge.edit` (criar coleção/página e atualizar) e `knowledge.manage`
   (apagar, restaurar e exportar o workspace no escopo visível).
4. `Version` é concurrency token. Update ou restore com versão diferente
   responde **409** `KnowledgeVersionConflict` e não grava. O histórico é
   append-only; restore cria uma versão nova com o conteúdo antigo.
5. O body é Markdown restrito: sem tag HTML e sem scheme `javascript:`,
   `data:` ou `vbscript:`. Referências `[[message|decision|task|attachment:uuid]]`
   são resolvidas na leitura. `task` aponta para action item (B-119) até B-123.
   Alvo inacessível, apagado ou de outro kind volta `redacted`, sem título.
6. Flag `Knowledge:Pages:Enabled`, **off** em `appsettings.json`. Desligada, os
   endpoints respondem `KnowledgeDisabled` (404) e não gravam. Development e o
   TestHost ligam a flag.
7. Evento durável `knowledge.page.changed` sai pelo outbox sem título nem body.
   Fan-out SignalR só quando a coleção é de canal. Falha de conhecimento não
   bloqueia o envio de mensagem.
8. Soft-delete entra na retenção do tenant: o purge remove a página e as
   versões juntas. Export da página inclui as versões. Export do workspace
   exige `knowledge.manage` e omite coleções fora da ACL.

## Alternativas

- Copiar o body da mensagem para a página: sobrevive a delete/purge.
- CRDT agora: contradiz D-17 e abre B-152 cedo.
- Permissão paralela por bloco: a primeira versão usa ACL da coleção.

## Consequências

- Rollback operacional: `Knowledge:Pages:Enabled=false`. O chat não muda.
- Rollback de schema: `Down` de `20261014173951_AddKnowledgePages` remove as três tabelas.
  Páginas existentes somem; mensagens não.
- R3: sem dependência nova, sem bus, sem Qdrant (isso permanece em B-121).

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-120). Tenant vem da sessão. Busca e
export não devolvem título de página fora da ACL.
