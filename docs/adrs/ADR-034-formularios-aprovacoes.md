# ADR-034 — Formulários e aprovações

- Status: Accepted
- Data: 2026-10-16
- Relacionado: B-124, B-123, B-125, ADR-009, ADR-010, ADR-018

## Contexto

Pedidos repetíveis não podem continuar só como texto livre. A coleta precisa de
schema versionado, resposta imutável e decisão rastreável, sem virar o builder
de automação de B-125 e sem script na API.

## Decisão

1. Quatro tabelas em `messaging`: `forms`, `form_versions`, `form_submissions`
   e `form_decisions`. `TenantId` com FORCE RLS. Postgres continua source of truth.
2. O contrato `form.schema.v1` é um JSON allowlisted: campos `text`, `number`,
   `choice`, `date`, `user` e `attachment`; aprovação `single` ou `n_of_m`;
   resultado `none`, `message` ou `task`. Propriedade desconhecida no schema
   rejeita o rascunho. Campo `hidden` e obrigatório ao mesmo tempo é inválido.
3. Publicar congela o JSON canônico em `form_versions`. Uma edição posterior
   cria outra versão. A submission guarda `SchemaVersion` e as respostas já
   filtradas. Versão antiga não muda.
4. Resposta e decisão são idempotentes. O actor da decisão é sempre o perfil
   autenticado. `actorUserId` diferente no body devolve `ActorNotAllowed` e
   não grava. Cada revisor decide uma vez.
5. Ao atingir o quórum de aprovação, a automação tem profundidade 1: no máximo
   uma mensagem (body = título congelado, sem respostas) e, se pedido, uma
   tarefa apontando para essa mensagem. A chave deriva do id da submission.
   Não há DSL, credencial nem retry infinito.
6. Flag `Forms:Enabled`, **off** em `appsettings.json`. Desligada, os endpoints
   respondem `FormDisabled` (404) e não gravam. Development e o TestHost ligam
   a flag. O chat segue.
7. Export devolve só o que o autor, o criador ou o revisor daquela versão podem
   ver. A retenção do tenant zera `AnswersJson` (`form.answers_purged`) e
   preserva a decisão. Outbox e audit não levam respostas nem título.

## Alternativas

- Workflow visual neste item: é B-125. Fora de escopo.
- Copiar as respostas para a mensagem de resultado: vira canal de PII. Fora.
- Schema mutável na submission: quebra o aceite de versão.

## Consequências

- Rollback operacional: `Forms:Enabled=false`. O chat não muda. Linhas já
  gravadas permanecem até a migration `Down` ou até a retenção limpar respostas.
- Rollback de schema: `Down` remove `forms`, `form_versions`, `form_submissions`
  e `form_decisions`. Mensagens e tarefas criadas pelo resultado permanecem.
- Capacidade: 30 formulários vivos por canal, 20 campos, 12 revisores, página
  de 50. A métrica `vibechat.forms.published|submitted|decided` não leva conteúdo.
- R3: sem serviço novo, sem bus, sem script, flag off por default.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-124). Tenant vem da sessão. Revisor
fora da lista da versão recebe 403. Outro tenant recebe 403. Campo oculto ou
desconhecido não é persistido.
