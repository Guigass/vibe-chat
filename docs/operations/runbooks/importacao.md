# Importação assistida (B-153 / ADR-030)

A flag `Features:Import:Enabled` nasce desligada. Com ela desligada a API responde
404 `ImportDisabled` e o chat segue normal.

## Antes de publicar

1. Confirme que o operador tem `workspace.import` (admin do workspace).
2. Rode validar e simular. O dry-run não cria canal nem mensagem.
3. Conflito de tipo num canal que já existe bloqueia a execução.
4. Papel fora de Member, Moderator, Auditor ou Admin recusa o arquivo inteiro.

## Se a publicação falhar

A transação do request desfaz spaces, canais e mensagens. Objetos MinIO
enviados no meio do caminho são apagados na falha. O job permanece em
`staged` e pode ser publicado de novo sem duplicar o que já tiver `id_map`.

## Rollback

- Antes de publicar: `POST .../rollback` remove a reserva e o JSON canônico.
- Depois de publicar: o corpo precisa de `{ "confirm": true }`. Só some o que
  este job criou. Canal já existente e reutilizado permanece.
- Desligar a flag não apaga histórico já publicado.

## O que não entra no relatório

Body, e-mail, payload e segredo. A trilha de audit (`import.validate`,
`import.plan`, `import.execute`, `import.publish`, `import.rollback`) guarda
contagens.
