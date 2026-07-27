# B-123 — Tarefas derivadas de mensagens

> Wave 12 · Trilha C/D · Deps: B-119 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Action items precisam de execução diária simples: responsável, prazo, checklist
e estado, sem obrigar ferramenta externa.

## Escopo

- Evoluir ActionItem para Task pessoal/de time.
- Assignee(s), due date, prioridade, checklist e estado.
- Comentários em thread vinculada.
- Views minhas, do channel e do workspace autorizado.
- Lembretes e inbox.
- Eventos para automações/conectores.

## Fora de escopo

- Gantt, sprint planning ou timesheet.
- Campos arbitrários nesta fatia.
- Atribuir usuário sem acesso à origem.

## Contratos

Task tenant-scoped com optimistic concurrency; checklist ordenado; eventos
`task.created/updated/completed/overdue`.

## UX

Criação a partir da mensagem ou botão global. Quick complete, filtros e empty
states. Overdue não usa cor como único sinal.

## Multi-tenant e authZ

Visibilidade herda origem/collection. Assignee deve ser membro; remoção de
membership desatribui ou transfere segundo regra documentada.

## Aceite

- [ ] Task mantém origem.
- [ ] Concurrency não perde atualização.
- [ ] Reminder/inbox respeitam preferências.
- [ ] Ex-membro não conserva acesso.
- [ ] Eventos são idempotentes.

## Testes

Domain/concurrency, security assignment/ACL, worker overdue e E2E lifecycle.

## Riscos

Scope creep de project management. Manter modelo pequeno e usar connectors para
sistemas especializados.

