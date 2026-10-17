# ADR-036 — Incident rooms e playbooks

- Status: Accepted
- Data: 2026-10-17
- Relacionado: B-126, B-112, B-119, B-125, ADR-009, ADR-010, ADR-015, ADR-035

## Contexto

Incidentes pedem sala, papéis, timeline e postmortem sem virar pager nem
monitor de infraestrutura. O pacote R3 de automação e playbooks proíbe script
na API e bus novo até o gatilho do ADR-015. B-125 já executa automações; B-126
só referencia uma automação admitida.

## Decisão

1. Quatro tabelas no schema `incident`: `rooms`, `responders`, `steps` e
   `timeline`. `TenantId` com FORCE RLS. Postgres continua source of truth.
   Não há worker nem `app.job_role` novo.
2. O template `default` (`incident.playbook.v1`) é código, não DSL. Passos:
   `acknowledge`, `mitigate`, `communicate`, `postmortem`. Template
   desconhecido devolve `UnknownIncidentTemplate`.
3. `POST /incidents` cria um canal `Private` e a sala na mesma transação. A
   chave de idempotência é `(TenantId, CreatedBy, IdempotencyKey)`. O retry
   devolve a mesma sala e o mesmo canal. Commander e responders entram no
   canal e em `responders`. Esses papéis não alteram `workspace.members.Role`
   e não concedem `workspace.admin`.
4. Estados: `declared → mitigating → resolved`. Qualquer outro salto devolve
   `InvalidIncidentTransition`. `resolved` grava o postmortem (título,
   severidade, passos e referências da timeline) e recusa evidência nova.
5. A timeline guarda `kind` + `sourceId`. Não copia body de mensagem, decisão
   ou tarefa. Evidência fora do canal da sala devolve `EvidenceNotFound`.
   Atualização é um audit `incident.updated`; o postmortem cita o id, não a
   nota.
6. O passo `mitigate` pode apontar para uma automação do mesmo workspace.
   Executar o passo não entra no runner de B-125 (evita loop). Automação
   ausente, desligada ou não publicada falha o passo com `AutomationFailed`,
   emite `incident.automation_failed` e deixa a sala no estado atual. O chat
   segue.
7. Quem não está em `responders` recebe 404 `IncidentNotFound` sem título nem
   severidade. `incident.export` (Owner/Admin) e membership são os dois
   requisitos do postmortem.
8. Flag `Incidents:Enabled`, **off** em `appsettings.json`. Desligada, a API
   responde `IncidentDisabled` (404) e não grava. O chat segue.

## Alternativas

- Canal público com header: vaza título e severidade para quem não responde.
- Copiar mensagens para a timeline: a evidência sobrevive a delete e à ACL.
- Executar a automação de dentro do passo: reentra no runner e pode loopar.
- Pager ou sonda de infraestrutura: fora de escopo da spec.

## Consequências

- Rollback operacional: `Incidents:Enabled=false`. Salas e mensagens já
  gravadas permanecem. O chat não muda. Runbook:
  `docs/operations/runbooks/salas-de-incidente.md`.
- Rollback de schema: `Down` remove `incident.rooms`, `responders`, `steps`
  e `timeline`. O canal privado criado permanece até um delete explícito.
- Capacidade: 30 salas abertas por workspace, 8 pessoas, 40 linhas de
  timeline, 4 passos no template. Métricas
  `vibechat.incidents.declared|resolved|automation_failed` não levam conteúdo.
- R3: sem serviço novo, sem bus, sem script, flag off por default.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-126). Tenant vem da sessão. Member
sem membership não vê título nem severidade. Responder não ganha
`workspace.admin` nem `incident.export`. Outro tenant recebe 403.
