# Administração delegada e quotas

A flag `Features:DelegatedAdmin:Enabled` nasce desligada. Com ela desligada a API
responde 404 `DelegatedAdminDisabled` e o chat não consulta quota.

## Ligar

1. Confirmar que a migration `AddDelegatedAdmin` já rodou.
2. `Features:DelegatedAdmin:Enabled=true` e reiniciar a API.
3. Como WorkspaceOwner, conceder só o escopo necessário, com motivo.
   Break-glass dura no máximo 4 horas e aparece em alertas.
4. Definir soft e hard por recurso. Sem linha de quota o recurso não bloqueia.

## Desligar

1. `Features:DelegatedAdmin:Enabled=false` e reiniciar a API.
2. As atribuições e os contadores permanecem. O chat volta a ignorá-los.

## Falha do contador

Um erro do Postgres no consumo não derruba o envio: a métrica
`vibechat.quota.check_failed` sobe e a operação segue. O hard limit só é
garantido enquanto o `UPDATE` condicional responde.
