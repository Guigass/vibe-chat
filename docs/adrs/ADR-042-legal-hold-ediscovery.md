# ADR-042 — Legal hold e eDiscovery

- Status: Accepted
- Data: 2026-10-19
- Relacionado: B-129, B-046, B-114, D-23, ADR-009, ADR-018

## Contexto

A retenção (ADR-018) apaga soft-delete vencido. Não havia como preservar um
escopo para investigação nem exportar um snapshot com cadeia de custódia.
D-23 pede controles alinhados a LGPD/GDPR e SOC 2/ISO 27001, sem alegar
certificação. O pacote R3 de compliance manda capability separada de
`workspace.admin` e precedência do hold sobre o purge.

## Decisão

1. Flag `Features:LegalHold:Enabled` nasce **false**. Desligada, a API de
   compliance responde 404 `LegalHoldDisabled`. Hold já ativo continua
   bloqueando o purge: desligar a flag não é atalho para apagar conteúdo
   preservado.
2. Tabelas em `compliance` (`grants`, `legal_holds`, `discovery_cases`,
   `discovery_exports`, `custody_events`), com `TenantId`, RLS FORCE e
   WITH CHECK. Sem hold entre tenants.
3. `workspace.admin` nomeia outra pessoa com `hold` ou `discovery`. Não
   nomeia a si. `compliance.hold` e `compliance.discovery` não entram no
   catálogo de papéis. Aplicar e remover exigem duas pessoas: quem pede
   não aprova. As duas confirmações vêm no corpo.
4. Escopos: workspace, pessoa, canal, conversa ou intervalo. Hold expirado
   ou `released` não cobre. Remover não apaga a mensagem; o purge seguinte
   volta a valer fora do hold.
5. O caso guarda a query. O export síncrono grava
   `vibechat.ediscovery.export.v1`, progresso 100 e SHA-256 do manifesto.
   Acima de 200 mensagens a API recusa, sem snapshot parcial.
6. `custody_events` é append-only (trigger e REVOKE de UPDATE/DELETE no
   papel de runtime). A auditoria operacional recebe a ação, sem body.

## Alternativas

- Dar `compliance.*` ao Admin: junta operação diária e investigação.
- Apagar no momento da remoção: a spec pede reativar o purge futuro.
- Export assíncrono com worker: mais estados do que esta fatia precisa.
  O progresso vai na resposta pronta.

## Consequências

- Rollback da API: `Features:LegalHold:Enabled=false`. Holds ativos
  continuam no purge até uma liberação com a flag ligada.
- A migration `AddLegalHold` cria o schema. Não há dado de produção para
  voltar atrás; dropar as tabelas só com o schema vazio.
- UI em `/compliance`, fora do admin do dia a dia.
- Não há parecer jurídico, certificação, nem hold cross-tenant.
