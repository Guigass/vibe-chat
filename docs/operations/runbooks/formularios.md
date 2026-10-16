# Formulários e aprovações (B-124)

Flag `Forms:Enabled`. Default **false** em `appsettings.json` da API e do worker.
Development liga para o laboratório.

## Sintoma

- `GET/POST /api/v1/workspaces/{id}/forms` devolve 404 `FormDisabled`: a flag
  está off. O chat, as tarefas e as mensagens seguem.
- Resposta antiga some do export com `answersPurged`: a retenção do tenant
  zerou o JSON. A decisão permanece.
- Aprovação não cria a mensagem ou a tarefa: o criador perdeu `message.send`
  ou o quórum ainda não fechou. Repetir a mesma chave de decisão completa o
  resultado sem duplicar.

## Rollback

1. `Forms:Enabled=false` e reiniciar a API.
2. Confirmar 404 `FormDisabled` em `POST …/forms` e que `POST …/messages` segue.
3. A migration `AddForms` `Down` remove as quatro tabelas. Não apaga mensagens
   nem tarefas já criadas pelo resultado.

## Não fazer

- Não desligar RLS para “enxergar” a resposta de outro tenant.
- Não colocar o body das respostas em log, audit ou outbox.
