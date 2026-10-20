# Classificação de dados e DLP

A flag `Features:Dlp:Enabled` nasce desligada. Com ela desligada a API
`/dlp/*` responde 404 `DlpDisabled` e share, guest, export, webhook, IA e
download seguem como antes.

Com a flag ligada e sem linha em `compliance.dlp_policies`, o workspace fica
em simulação: a decisão é contada em `GET .../dlp/metrics` e o efeito externo
acontece. Enforce é `PUT .../dlp/policy` com `simulation: false`, feito por
quem tem o grant `dlp` — não pelo papel de admin.

## Rollback

1. `Features:Dlp:Enabled=false` e reiniciar API e worker.
2. Conferir que um encaminhamento e um webhook de teste voltam a sair.
3. As tabelas `compliance.dlp_*` podem ficar. Não apagam mensagem.

Bloqueio inesperado com a flag ligada: voltar a política para simulação
antes de desligar a flag, para medir a regra sem repetir o efeito.

Detector com timeout não eleva o rank. Falso positivo revisado vale só para
o par detector + assunto. E2EE (`ConfidentialE2EE`) não é inspecionado.
