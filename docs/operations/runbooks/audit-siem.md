# Runbook — Export de audit para SIEM (B-132)

A flag `Features:Siem:Enabled` nasce desligada. Com ela desligada a API
responde 404 `SiemDisabled` e o worker não faz POST.

## Ligar

1. Confirmar egress HTTPS do destino (sem IP privado, sem redirect).
2. `Features:Siem:Enabled=true` e reiniciar API e worker.
3. Um admin nomeia outra pessoa com capability `audit.export`.
4. Essa pessoa cria o destino em `/compliance`, copia o segredo uma vez e
   envia o evento de teste. O circuito e o lag aparecem sem o segredo.

## Falha

- `SiemCircuitOpen`: o destino recusou ou ficou inacessível. O chat segue.
  Esperar 60 segundos ou rotacionar o segredo (a rotação fecha o circuito)
  e testar de novo.
- `SiemEgressBlocked`: a URL não passou na guarda SSRF. Nada foi enviado.
- Lag alto: o worker drena no ciclo do outbox, em lotes de 20. Backfill
  cobre no máximo 24 horas e 100 eventos.

## Rollback

1. `Features:Siem:Enabled=false` e reiniciar API e worker.
2. Entregas novas param. Destinos e o log de entrega permanecem.
3. Revogar o destino se o segredo vazou. A revogação não apaga o audit local.

Não há credencial de SIEM de produção neste repositório.
