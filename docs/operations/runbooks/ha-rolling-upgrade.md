# HA, drain e rolling upgrade (B-144 / ADR-062)

Perfil opcional. O Standard permanece um API e um worker com `Ha:Enabled=false`.

Objetivo de referência (não é SLA): disponibilidade 99,95%, RPO ≤ 5 min,
RTO ≤ 30 min (D-25 / D-28).

## Ligar o perfil

```bash
docker compose -f compose.yaml -f compose.dev.yaml -f compose.ha.yaml --profile apps up -d
```

`compose.ha.yaml` só exporta `Ha__Enabled=true` para API e worker. Não publica
porta extra e não substitui o Compose padrão.

Para um segundo processo atrás do proxy, não publique `5080` no host e aponte
o upstream para os dois containers. WebSocket não precisa de sticky session.
Não habilite long polling neste perfil.

## Drain de um nó

1. Confirme `GET /health/live` = 200.
2. Como admin (`support.repair`), `POST /api/v1/admin/ha/drain` no processo
   que vai sair. Member recebe 403. Flag off recebe 404 `HaDisabled`.
3. O nó avisa os clientes locais (`NodeDraining`). O banner fala em
   manutenção, sem nome de host. O cliente reconecta e completa o gap-fill
   pelo histórico. Rascunhos ficam no browser.
4. `/health/ready` e `/ready` respondem 503. `/health/live` continua 200.
   O balanceador tira o nó. O histórico segue disponível até o processo sair.
5. Suba a versão nova. Quando o processo novo estiver ready, o cliente
   completa o sync. Versões adjacentes na fase `expand` ou `migrate` podem
   coexistir. A fase `contract` não aceita mistura; o rollback de binário
   espera a fase anterior.

`GET /api/v1/meta/runtime` (`support.read`) mostra fase, revisão, RPO/RTO e
`draining`. Não devolve senha nem body.

## Failover do worker

Com a flag ligada, um worker segura `pg_try_advisory_lock`. O outro espera.
Se o líder cair, a conexão solta o lock e o follower assume. Efeito já
gravado não é repetido. A troca medida no lab tem de ficar abaixo do RTO
(30 min; o teste exige menos de 30 s).

Flag off: não há lock. Continua um worker.

## Postgres, Redis e MinIO

- Postgres continua a fonte da verdade. O RPO ≤ 5 min do perfil HA usa o
  WAL/PITR do perfil Standard (D-28 / B-031), com arquivo contínuo no
  operador. Promover uma réplica streaming:
  1. Pare a escrita no primário, ou confirme que ele está inacessível.
  2. No standby, `pg_ctl promote` (ou o equivalente da imagem).
  3. Aponte `ConnectionStrings__Database` para o novo primário e suba API
     e worker.
  4. O alvo é RTO ≤ 30 min. Não há segundo Postgres obrigatório no Compose
     do lab.
- Redis com AOF é backplane efêmero. A queda degrada presença, typing e
  fan-out. Envio e histórico seguem.
- MinIO do lab permanece um nó. Erasure distribuído não entra neste corte.

## Rollback

1. `Ha__Enabled=false` e reinicie API e worker.
2. Não há migration para reverter.
3. Um processo novo nasce fora de drain.

## O que isto não faz

- Multi-região com escrita ativa.
- Kubernetes, bus ou OpenSearch.
- SLA comercial.
