# Legal hold e eDiscovery (B-129)

A flag `Features:LegalHold:Enabled` nasce desligada. Com ela desligada a API
responde 404 `LegalHoldDisabled`. Hold que já está `active` continua fora do
purge. Para voltar a apagar aquele escopo, ligue a flag, peça a remoção e
aprove com outra pessoa que tenha `compliance.hold`. A remoção não apaga na hora.

`workspace.admin` nomeia outra pessoa. Não nomeia a si. Quem pede o hold não
aprova. O export `vibechat.ediscovery.export.v1` devolve progresso e checksum.
`compliance.custody_events` não aceita UPDATE nem DELETE.

Rollback da superfície: flag `false`. Não drope as tabelas enquanto houver hold
ativo se a intenção é continuar preservando.
