# B-126 — Incident rooms e playbooks

> Wave 13 · Trilha B/C/D/E · Deps: B-112, B-119, B-125 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Incidentes exigem canal, papéis, timeline, decisões e comunicação criados sob
pressão, gerando perda de evidência e inconsistência.

## Escopo

- Criar incident room por template.
- Severidade, commander, responders, status e timestamps.
- Playbook com steps, owner, evidence e automações.
- Timeline derivada de messages/decisions/tasks/audit.
- Updates/announcements e postmortem exportável.
- Integração opcional com connectors/alerts.

## Fora de escopo

- Pager/on-call completo.
- Monitoramento de infraestrutura.
- Alterar dados de observabilidade de origem.

## Contratos

Incident/PlaybookRun tenant-scoped; state machine declarada; eventos
`incident.*`; correlation IDs para sinais externos.

## UX

Header de incidente persistente, command panel, timeline e botões de status.
Operações críticas confirmadas e keyboard accessible.

## Multi-tenant e authZ

Room privado por default; roles incident-specific nunca concedem
`workspace.admin`. Export/postmortem exige permission.

## Aceite

- [ ] Template cria room e playbook idempotentemente.
- [ ] Transição inválida é rejeitada.
- [ ] Timeline referencia evidência.
- [ ] Outsider não vê título/severidade.
- [ ] Encerramento gera snapshot/postmortem.

## Testes

State transitions, template retry, security private room, automation failure,
E2E declare→mitigate→resolve.

## Riscos

Feature ser confundida com sistema de paging. Integrar alertas, não reinventar
on-call.

