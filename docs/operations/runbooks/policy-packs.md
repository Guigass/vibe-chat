# Policy packs

A flag `Features:PolicyPacks:Enabled` nasce desligada. Com ela desligada a API
responde 404 `PolicyPackDisabled` e o convite de guest ignora o pack.

## Ligar

1. Um `workspace.admin` concede a capability `policy` a outra pessoa.
2. `Features:PolicyPacks:Enabled=true` e reiniciar a API.
3. Essa pessoa simula o pack (`small-team`, `enterprise-baseline` ou
   `regulated`) e só então confirma o apply.

O documento diz `certification: false`. É baseline técnica, não selo.

## Desligar

1. `Features:PolicyPacks:Enabled=false` e reiniciar a API.
2. A linha em `compliance.policy_assignments` permanece. Os controles já
   gravados (IA, retenção, upload, simulação de DLP) não voltam sozinhos.
3. Rollback pela API só existe com a flag ligada e com uma versão anterior.

## Falha

- 409 `PolicyPackOverrideForbidden`: o override enfraquece um controle locked,
  inclusive desligar a auditoria de conteúdo.
- 409 `PolicyPackGuestsBlocked`: o pack efetivo está com guests desligados.
- 409 `PolicyPackRollbackUnsafe`: não há versão anterior.
- 400 `PolicyPackImportRejected`: o JSON tem tenant, segredo ou código.
