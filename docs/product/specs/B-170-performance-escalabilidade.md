# B-170 — Performance e escalabilidade

> Wave 16 · Trilha A/C/E/F · Deps: B-146, D-25 · Soft: W5-3 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Há load smoke (W5-3) e o capacity model/SLOs (B-146), mas falta um passo
explícito que **olhe** hot paths, gargalos e alavancas de escala na arquitetura
atual (Compose + monólito modular) antes de HA (B-144), bus, OpenSearch ou
Kubernetes. Sem essa revisão, otimizações viram chute e ADRs 015–017 avançam sem
evidência de que o limite é a topologia — e não um índice, N+1 ou fan-out.

## Escopo

- Inventário dos hot paths: `SendMessage` (idempotência/`seq`/outbox), history/
  sync, SignalR join/fan-out/reconnect, worker outbox, FTS, upload/presign e
  admin export leves.
- Rodar workloads k6 dos perfis Small/Standard de B-146 (além do smoke W5-3) e
  registrar p95/p99, outbox lag, conexões SignalR/nó, msg/s e maiores canais.
- Profiling e planos de query sob carga (Postgres + traces OTel); listar
  gargalos com evidência (arquivo, métrica, commit).
- Documento canônico `docs/architecture/performance-escalabilidade.md`: modelo
  de escala horizontal da API/Worker, limites do Postgres/Redis/MinIO, noisy
  neighbor, backpressure e checklist de regressão.
- Otimizações **só com evidência** e dentro da arquitetura vigente (índices,
  pooling, paging já alinhado a B-089, batch de outbox, limites de fan-out,
  cache de presença já existente). Sem novo serviço.
- Gatilhos objetivos (números) que alimentam ADR-015/016/017 — sem implementar
  bus/OpenSearch/K8s neste item.
- Painel/runbook mínimo: quais SLIs olhar, thresholds de B-146 e o que fazer
  quando queimar error budget de latência/outbox.

## Fora de escopo

- Topologia HA, rolling upgrade e zero-downtime → **B-144**.
- Lifecycle/CDN de objetos → **B-145**.
- Federação → **B-065**; live media capacity → **B-147**.
- Trocar FTS por OpenSearch, introduzir bus ou K8s sem gatilho medido.
- SLA comercial ou multi-region write (D-25).
- Redesign de contratos públicos só por micro-otimização.

## Contratos

Sem contrato de produto novo. Pode estabilizar labels/métricas e o formato do
relatório de benchmark já previsto em B-146. Qualquer mudança de API/evento
exige atualizar `contratos.md` e testes — não é o caminho default deste item.

## UX

Sem superfície nova para member. Admin/ops podem ver SLOs/burn rate já previstos
em B-146; mensagens de degradação (lento/reconnect) reutilizam padrões existentes
sem expor internals.

## Multi-tenant e authZ

- Workloads usam tenants sintéticos; incluir cenário noisy-neighbor.
- Otimizações não enfraquecem RLS, `TenantContext` nem authZ do hub.
- Cache/chave Redis continua prefixada por tenant; sem bypass de isolamento por
  “atalho de performance”.

## Aceite

- [ ] Hot paths inventariados com owner de métrica e baseline B-146
- [ ] Relatório versionado (ambiente, commit, hardware, margem) com p95/p99 e
      outbox lag por perfil Small/Standard
- [ ] `docs/architecture/performance-escalabilidade.md` publicado e linkado no
      portal/ops
- [ ] Gargalos top-N com evidência; correções R0/R1 mergeadas ou explicitamente
      adiadas com motivo
- [ ] Gatilhos numéricos para ADR-015/016/017 atualizados ou confirmados
- [ ] Regressão de load smoke (W5-3) + suíte security cross-tenant verdes
- [ ] Nenhuma dependência proprietária nem secret em logs/artefatos de bench

## Testes

- `task load:smoke` e workloads B-146 Small/Standard reproduzíveis.
- Integration/security existentes após qualquer mudança de query/índice/hub.
- Comparar métricas before/after no evidence bundle do PR de otimização.

## Riscos

- Otimizar cedo demais ou no lugar errado → só alterar código com métrica
  antes/depois.
- Transformar relatório em marketing de throughput → sempre citar ambiente e
  data; não virar SLA.
- Confundir com HA → B-170 endurece o profile Standard; B-144 só depois, com
  evidência (D-25).
