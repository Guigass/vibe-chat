# ADR-062 — HA opcional, drain e rolling upgrade

- Status: Accepted
- Data: 2026-10-26
- Relacionado: B-144, B-170, B-146, B-031, D-25, D-28, ADR-015, ADR-016, ADR-017, ADR-058

## Contexto

O perfil Standard (D-25) é um API e um worker. B-170 confirmou que bus, OpenSearch
e Kubernetes continuam fora do caminho. O objetivo HA é 99,95%, RPO ≤ 5 min e
RTO ≤ 30 min, sem SLA comercial e sem multi-região.

## Decisão

1. Flag `Ha:Enabled` default **false**. Desligada, as rotas HA respondem 404
   `HaDisabled`. Envio, histórico, readiness e o worker único não mudam.
2. Com a flag ligada, a API restringe o SignalR a **WebSockets**. Sticky session
   não é necessária. Long polling continua disponível só no Standard, onde há
   um nó. O backplane Redis segue efêmero: queda degrada realtime, não a
   escrita nem o histórico.
3. Jobs singleton (outbox, retenção, digest, backup, automação, agenda, tarefas
   e enquetes) esperam um lease `pg_try_advisory_lock` na role de runtime.
   O segundo worker fica follower. A conexão que morre solta o lock; o outro
   assume. Um efeito já concluído não roda de novo. Não há migration: o schema
   atual está na fase **expand**.
4. `POST /api/v1/admin/ha/drain` exige `support.repair` e drena só o processo
   que recebeu a chamada. Manda `NodeDraining` às conexões locais, readiness
   (`/health/ready` e `/ready`) passa a 503 e o live continua 200. O histórico
   segue legível para o gap-fill. Member recebe 403. A resposta não leva body
   de mensagem nem connection string.
5. Backpressure por nó: buffer limitado. Evento efêmero (`Typing`,
   `PresenceChanged`) é descartado. Evento durável vira `ResyncRequired` e não
   entra numa fila infinita. Conexão sem principal, nó em drain ou acima do
   teto é recusada. O reconnect volta a `JoinChannel`, que relê membership.
6. `GET /api/v1/meta/runtime` exige `support.read` e devolve revisão, fase,
   RPO/RTO e se o objetivo do perfil está dentro de D-25/D-28. Sem segredo.
7. Topologia de lab: `compose.ha.yaml` liga a flag. Não adiciona Kubernetes,
   bus nem OpenSearch (ADR-015…017). Promoção física do Postgres fica no
   runbook, em cima do WAL/PITR já decidido. A medição automática é a troca
   do lease, que tem de caber no RTO.

## Alternativas

- Patroni ou um segundo Postgres no Compose: mais operação do que B-170
  sustentou para o lab. O runbook cobre a promoção sem obrigar o CI a subir
  réplica.
- `SKIP LOCKED` em colunas novas do outbox: seria migration expand. O lease
  na conexão já impede dois workers de processar ao mesmo tempo sem mudar o
  schema.
- Sticky no proxy para todos os transportes: desnecessário com WebSocket e
  backplane. Long polling no HA exigiria afinidade; por isso ele fica de fora
  quando a flag está ligada.

## Rollback

1. `Ha:Enabled=false` e reiniciar API e worker. O Standard volta a ser o
   único líder, sem lock.
2. Não há migration nesta entrega. A fase `expand` permite rollback de
   binário. A fase `contract` recusa versões mistas; ela não está ativa.
3. Um nó em drain volta a aceitar tráfego quando o processo sobe de novo
   sem o estado de drain (memória do processo).

## Consequências

- Admin de workspace com `support.repair` pode tirar o nó local de rotação.
  Não lê dado de outro tenant. Os clientes reconectam.
- O objetivo 99,95% / RPO ≤ 5 min / RTO ≤ 30 min é referência, não SLA.
- Security review profundo de R3 permanece na rodada humana final
  (`docs/agents/autonomia.md`).
