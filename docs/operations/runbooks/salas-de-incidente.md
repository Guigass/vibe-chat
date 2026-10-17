# Runbook — Salas de incidente (B-126)

Flag `Incidents:Enabled` (**off** por default). Com a flag desligada a API
responde 404 `IncidentDisabled` e não grava sala nova. O chat e as salas já
criadas no banco permanecem; a UI some.

Este runbook é da feature. Incidentes de operação da instância continuam em
[`incidentes.md`](incidentes.md).

## Falha

| Sintoma | O que olhar | Ação |
|---------|-------------|------|
| Sala não abre | Flag e papel Owner/Admin | `Incidents:Enabled=true` só onde a feature deve existir. Member recebe 403 em `POST` |
| Retry criou duas salas | `incident.rooms.IdempotencyKey` | A mesma chave do mesmo autor devolve a sala existente. Não apagar a linha para forçar |
| Passo de automação falhou | Step `AutomationFailed` | A automação precisa estar publicada e ligada no mesmo workspace. A sala não muda de estado e o chat segue |
| Outsider viu o título | Não deve ocorrer | `GET` de quem não está em `responders` é 404 sem título nem severidade |
| Postmortem recusado | Status e `incident.export` | Só `resolved`, e só Owner/Admin que também é membro |

## Rollback

1. `Incidents:Enabled=false` e reiniciar a API.
2. O chat não muda. Mensagens já enviadas no canal privado permanecem para os membros.
3. Migration `Down` só se for preciso remover `incident.rooms`, `responders`, `steps` e `timeline`. O canal privado não sai com o `Down`.

## Capacidade

30 salas não resolvidas por workspace, 8 pessoas na sala, 40 linhas de timeline.
Não há worker nem paging.
