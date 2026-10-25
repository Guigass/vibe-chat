# ADR-048 — Capacity model, benchmarks e SLOs

- Status: Accepted
- Data: 2026-10-21
- Relacionado: B-146, D-25, D-28, ADR-015, ADR-016, ADR-017

## Contexto

HA, busca dedicada e um bus externo só se justificam com porte e limites
explícitos. D-25 e D-28 já fixaram objetivos de disponibilidade e recuperação.
Faltava o envelope de carga, os SLIs e os números que disparam ADR-015, ADR-016
e ADR-017.

## Decisão

1. Flag `Features:CapacitySlo:Enabled` nasce **false**. Desligada, `GET` e
   `POST /capacity` respondem 404 `CapacitySloDisabled`. O caminho de mensagem
   não consulta o modelo.
2. O catálogo é de instalação, não de tenant. A leitura exige `workspace.admin`
   no workspace do ator. Member e Auditor recebem 403. Workspace de outro
   tenant também recebe 403, sem o catálogo.
3. Perfis de carga: `small`, `standard`, `large`. Cada um registra usuários,
   concorrência, conexões/nó, msg/s, maior canal, fan-out, storage, retenção e
   tenants. São envelopes de planejamento, não medição.
4. Recuperação segue D-28 e não vira SLA (`isSla` é sempre false). Basic:
   melhor esforço, RPO ≤ 24 h, RTO ≤ 4 h, sem objetivo de disponibilidade.
   Standard: 99,9% (D-25), RPO ≤ 1 h, RTO ≤ 4 h. HA: 99,95%, RPO ≤ 5 min,
   RTO ≤ 30 min, marcado como não vigente até B-144.
5. SLIs de Standard: disponibilidade da API 99,9%; latência p95 500 ms; eco
   1 s; outbox 5 s; busca 10 s; persistência 400 ms; entrega realtime 1 s;
   reconnect 3 s; sync 800 ms; revogação 30 s; anexo de 1 MiB 5 s; restore
   verificado. Sem amostra, o burn rate fica `unmeasured`.
6. Burn rate de razão = erro observado / (1 − objetivo). Burn rate de latência
   = observado / limite. `<= 1` está dentro; `> 1` está queimando.
7. Gatilhos, sustentados por 15 min: ADR-015 quando o lag p95 do outbox passa
   de 30 s com pelo menos 2 workers; ADR-016 quando a busca p95 passa de 500 ms
   com pelo menos 5 milhões de mensagens indexadas, ou quando a CPU de busca no
   primário passa de 40%; ADR-017 quando as réplicas de API passam de 3, ou
   quando as conexões/nó passam de 2000 e o restart rolante do Compose não
   cabe sem downtime.
8. Resultado medido usa `vibechat.capacity-result.v1`. Exige commit, ambiente,
   data e margem de erro. `isSla: true`, e-mail ou campo de pessoa são
   rejeitados. A avaliação é pura: não grava linha e não leva PII.
9. Smoke k6 usa o perfil `small` por 15 s. O benchmark completo é
   `MODE=full` via `task load:capacity` e o workflow semanal só inspeciona o
   script. Não há número de marketing.

## Alternativas

- Gravar cada amostra em tabela: o resultado versionado cabe no formato e no
  git; uma tabela nova não muda o objetivo.
- Tratar o objetivo como SLA: D-25 e D-28 proíbem.
- Expor o painel a member: a spec reserva o burn rate à operação.

## Consequências

- Rollback: `Features:CapacitySlo:Enabled=false`. Não há migration.
- B-170 mediu em cima destes perfis e confirmou os gatilhos, sem mudá-los.
  B-144 continua bloqueado até a evidência de HA.
- Os números envelhecem. Cada resultado medido carrega data, commit, ambiente
  e margem. O baseline deste ADR é envelope de planejamento de 2026-10-21.
