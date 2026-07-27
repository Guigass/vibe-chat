# Riscos Conhecidos — VibeChat

## Matriz

| ID | Risco | Prob. | Impacto | Mitigação | Owner sugerido |
|----|-------|-------|---------|-----------|----------------|
| R-01 | Vazamento cross-tenant | M | Crítico | RLS + testes security + reviews; hardening contínuo no Registro de GAPs (`roadmap.md`) | Security / Backend |
| R-02 | Scope creep (K8s, Kafka, ES) | A | Alto | ADRs 015–017; roadmap waves | Tech lead humano |
| R-03 | Dual-write mensagem/evento | M | Alto | Outbox obrigatório (ADR-010) | Backend |
| R-04 | Misconfig Keycloak em prod | M | Alto | Realm as code; checklist ops | Infra |
| R-05 | Secrets commitados | M | Crítico | .gitignore; scanning; env only | Todos |
| R-06 | SignalR scale sem backplane | M | Médio | Redis backplane doc + config | Backend / Infra |
| R-07 | Outbox lag sob carga | M | Médio | Métricas + replicas worker | Ops |
| R-08 | Dependência de AI vazar PII | B | Alto | Flag off; redaction; aceite admin | Security / Produto |
| R-09 | Licença/marca indefinidas | A | Alto | **Fechado** — D-01 Apache-2.0 e D-02 “VibeChat” decididos em 2026-07-24 | Legal / Founder |
| R-10 | Retenção legal indefinida | A | Alto | **Mitigado** — D-03 + ADR-018; retenção configurável por tenant e purge com kill switch (B-047), off por default | Legal |
| R-11 | Agentes divergindo do glossário | A | Médio | orientacoes.md + glossário canônico | Agents lead |
| R-12 | Flaky E2E OIDC | M | Médio | **Mitigado (W7-1)** — job E2E na CI roda em modo `devauth`, tirando o Keycloak do caminho crítico; modo `oidc` continua manual | QA |
| R-13 | Postgres como search esgota | B | Médio | ADR-016 gatilhos | Backend |
| R-14 | MinIO disco cheio | M | Alto | Alertas disco; retenção anexos | Ops |
| R-15 | Design genérico (clone Slack) | M | Médio | design-system.md enforce em review | Frontend |
| R-16 | Realtime degradado (só typing) | B | Alto | **Mitigado (B-070 Done)** — gap-fill + E2E dois usuários; monitorar métricas SignalR/outbox | Backend / Frontend |
| R-17 | Secrets/webhooks expostos a membros | M | Crítico | **Mitigado (B-069/B-048)** — settings só `workspace.admin`; secret HMAC mascarado; nunca logar tokens | Security |
| R-18 | Auditoria de conversa (break-glass de leitura) | M | Alto | B-067 authZ `admin.dashboard` + escopo tenant; testes security; ver `modelo-ameacas.md` | Security |
| R-19 | Dependência UI comercial (PrimeNG/PrimeUI) | A | Alto | **Mitigação em curso (D-15 / D-16 / B-104)** — sair do PrimeNG; adotar spartan/ui (MIT) + CDK + tokens; NG-ZORRO rejeitado; não gerar chave nem esconder banner por CSS | Frontend |

## Riscos técnicos detalhados

### R-01 Isolamento

Qualquer endpoint novo é suspeito até prova de teste negativo. Preferir 404 a 403 quando enumeração for risco.

### R-02 Scope creep

Agentes não devem “já deixar pronto Kafka/K8s” sem gatilho. PRs que adicionem esses componentes na fase 1 devem ser rejeitados.

### R-09 / R-10 Legais

Sem licença clara, adoção OSS trava. Sem política de retenção, features de delete/export ficam ambíguas. Ambos endereçados em 2026-07-24: Apache-2.0 (D-01) e retenção soft-delete + purge configurável (D-03 / ADR-018 / B-047).

## Indicadores de risco emergente

- Outbox dead-letter crescendo
- Aumento de 403/401 anômalos
- Uso de memória Redis sem bound
- Tempo de PR arquitetural sem ADR

## Revisões

Revisar esta lista a cada wave do roadmap ou incidente P0/P1. Última revisão: Wave 7.
