# B-146 — Capacity model, benchmarks e SLOs

> Wave 14 · Trilha A/E/F · Deps: Wave 10 completa, D-25, D-28 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Sem perfis de carga e limites medidos, decisões de HA, busca, bus e live seriam
baseadas em ambição, não evidência.

## Escopo

- Perfis Small, Standard e Large com users, concurrency, msg/s e storage.
- Workloads k6 para send/history/search/upload/reconnect/admin.
- SLO/SLI para API, message echo, outbox lag, search lag e availability.
- SLI para persistência, entrega realtime, reconnect, sync, revogação, anexos e
  restore verificado.
- Capacity worksheet e bottleneck guide.
- Baseline de hardware e resultados versionados.
- Gatilhos objetivos para ADRs 015–017.

## Fora de escopo

- Prometer SLA comercial.
- Benchmark sem ambiente/revisão reproduzível.
- Otimizar antes de medir.

## Contratos

Sem contrato de produto; define métricas/labels estáveis e formato de resultado
versionado sem PII.

## UX

Dashboard operacional mostra SLO e burn rate; não expõe métricas internas a
member comum.

## Multi-tenant e authZ

Load usa tenants sintéticos e inclui noisy-neighbor/cross-tenant assertions.

## Aceite

- [ ] Workloads reproduzíveis por profile.
- [ ] Baseline e hardware documentados.
- [ ] SLO Standard reflete D-25.
- [ ] Perfis Basic/Standard/HA refletem D-28 sem transformar objetivo em SLA.
- [ ] Outbox/search/reconnect têm thresholds.
- [ ] Gatilhos ADR possuem números.
- [ ] Envelope registra users, concorrência, conexões/nó, msg/s, maior canal,
  fan-out, storage, retenção e tenants.

## Testes

Executar smoke na CI e full benchmark agendado; validar métricas e isolamento sob
carga.

## Riscos

Números envelhecerem ou virarem marketing. Data, commit, ambiente e margem de
erro acompanham cada resultado.

