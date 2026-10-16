# Runbook — Automações (B-125)

Flag `Automation:Enabled` (**off** por default). Com a flag desligada a API
responde 404 `AutomationDisabled` e o worker não varre. O chat, as mensagens
e as tarefas existentes seguem.

## Falha

| Sintoma | O que olhar | Ação |
|---------|-------------|------|
| Runs não saem de `pending` | Worker e `app.job_role=automation` | Reiniciar o worker. A flag precisa estar ligada |
| Mesma ação repetida | `automation.steps.IdempotencyKey` | O retry devolve o efeito já gravado. Não apagar a step para “forçar” |
| Loop de mensagens | Audit `automation.loop_stopped` | Profundidade máxima 3. Desativar a automação se o corpo precisa mudar |
| Circuito aberto | `CircuitOpenUntil` | `POST …/enable` zera o contador e o circuito |
| Conector recusado | Step `ConnectorNotAllowed` | O id precisa ser um webhook outbound habilitado, HTTPS, no mesmo tenant |
| Segredo em log | Não deve ocorrer | O log não guarda body. Se aparecer token, tratar como incidente e rotacionar |

## Rollback

1. `Automation:Enabled=false` e reiniciar API e worker.
2. Opcional: `POST …/disable` em cada automação publicada.
3. Migration `Down` só se for preciso remover o schema. Mensagens e tarefas
   criadas pela identidade `automation:` permanecem.

## Capacidade

30 automações por workspace, 30 runs vivos por hora, timeout de 2s por ação,
5 falhas consecutivas abrem o circuito por 15 minutos. Agenda mínima de 5
minutos.
