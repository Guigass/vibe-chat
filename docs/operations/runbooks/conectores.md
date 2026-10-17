# Runbook — Conectores bidirecionais (B-127)

Flag `Connectors:Enabled` (**off** por default). Com a flag desligada a API
responde 404 `ConnectorDisabled` e não grava instalação nova. O chat segue.
Mensagens já publicadas por um conector permanecem no canal.

## Falha

| Sintoma | O que olhar | Ação |
|---------|-------------|------|
| Admin não cria conector | Flag e papel Owner/Admin | `Connectors:Enabled=true` só onde a feature deve existir. Member recebe 403 |
| Evento externo repetido | `connectors.effects` | A mesma instalação e o mesmo id externo devolvem o mesmo efeito |
| Chamada depois de revogar | `status=revoked` e envelopes vazios | Inbound responde 410 e outbound não chama a rede. O token no fornecedor deve ser revogado lá também |
| URL interna ou metadata | `LastErrorCode` `EgressBlocked` ou `DnsRebinding` | O host de GitHub/GitLab é fixo. Calendário só aceita HTTPS público. Não há payload na resposta |
| Fornecedor instável | `CircuitOpen` / `Backoff` | A terceira falha abre o circuito por 60s. Reconectar não fura o circuito aberto. A outbox de mensagens não entra nessa fila |
| Segredo na tela | Resposta da API | Só máscara. Token novo só no rotate ou no callback OAuth, nunca de volta no JSON |

## Rollback

1. `Connectors:Enabled=false` e reiniciar a API.
2. O chat não muda. Efeitos já gravados (mensagem, checklist, timeline) permanecem.
3. Migration `Down` só se for preciso remover `connectors.installations`, `effects`, `deliveries` e `checkpoints`.

## Capacidade

20 instalações por workspace. Corpo inbound até 16 KiB. Circuito: 3 falhas, aberto por 60s, backoff até 30s. Não há worker.
