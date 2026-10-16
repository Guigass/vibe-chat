# ADR-035 — Automation builder

- Status: Accepted
- Data: 2026-10-16
- Relacionado: B-125, B-108, B-110, B-124, ADR-009, ADR-010, ADR-015, ADR-034

## Contexto

Webhooks e plugins cobrem integração, mas não um fluxo declarativo auditável.
B-125 pede trigger, condições e ações allowlisted, com identidade própria e
sem script na API. O pacote R3 de automação proíbe bus novo até o gatilho do
ADR-015.

## Decisão

1. Quatro tabelas no schema `automation`: `definitions`, `versions`, `runs` e
   `steps`. `TenantId` com FORCE RLS. Postgres continua source of truth. O
   worker reivindica agenda e runs pendentes com `app.job_role=automation`.
2. O contrato `automation.definition.v1` é JSON allowlisted. Triggers:
   `message`, `form`, `task`, `schedule` (`intervalMinutes` 5–1440) e
   `webhook`. Ações: `post_message`, `create_task`, `update_task`, `notify` e
   `call_connector`. Propriedade desconhecida ou valor de segredo rejeita o
   rascunho (`SecretValueNotAllowed`). `secretRefs` guarda só o nome.
3. Publicar congela o JSON e a interseção das capabilities pedidas com as
   permissões atuais de quem publica. A execução revalida essa interseção com
   o papel atual do publicador. A identidade técnica é um principal `Role.Bot`
   (`subject` `automation:{id}`), com escopo explícito de canal.
4. A chave de run deriva de automação + versão + evento + modo. A chave de
   step deriva também do índice da ação e é a idempotency key do efeito. Retry
   devolve o mesmo efeito. Profundidade máxima 3 encerra o loop e grava
   `automation.loop_stopped`. Circuito abre após 5 falhas por 15 minutos.
   Teto de 30 runs vivos por hora. Cada ação tem timeout de 2s. Erro
   transitório (`Timeout`, `ConnectorUnavailable`) tenta uma vez.
5. `call_connector` só entrega a um webhook outbound já existente, habilitado
   e com URL HTTPS válida no mesmo tenant. Payload redigido, sem body de
   mensagem. Conector ausente falha fechado (`ConnectorNotAllowed`) e não abre
   rede.
6. Dry-run grava o plano e não chama efeito. Desativar (`Enabled=false`)
   impede runs novos. Replay é explícito e usa a mesma admissão.
7. Flag `Automation:Enabled`, **off** em `appsettings.json`. É o binding da
   flag lógica `Features:Automation:Enabled`. Desligada, a API responde
   `AutomationDisabled` (404) e o worker não varre. O chat segue.
8. O log do run e do step guarda só tipo, status, código e id do efeito.
   `Redact` remove `vc_auto_`, `vc_int_` e pares `token|password|secret=`.
   O token de webhook é mostrado uma vez e persistido só como hash.

## Alternativas

- Script ou eval na definição: proibido pelo pacote R3.
- Bus ou scheduler externo: não há gatilho do ADR-015.
- Agir com o papel do admin: amplia privilégio. A interseção e a identidade
  bot evitam o confused deputy.

## Consequências

- Rollback operacional: `Automation:Enabled=false`. O chat não muda. Runs já
  gravados permanecem. Mensagens e tarefas já criadas permanecem.
- Rollback de schema: `Down` remove `automation.definitions`, `versions`,
  `runs` e `steps`.
- Capacidade: 30 automações por workspace, 8 ações, 20 canais, página de 50.
  Métricas `vibechat.automations.published|runs|stopped` não levam conteúdo.
- R3: sem serviço novo, sem bus, sem script, flag off por default.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-125). Tenant vem da sessão. Member
não tem `automation.manage`. Outro tenant recebe 403. Ação fora da capability
ou do canal falha fechada e não grava efeito.
