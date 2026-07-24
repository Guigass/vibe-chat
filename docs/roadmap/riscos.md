# Riscos Conhecidos — VibeChat

## Matriz

| ID | Risco | Prob. | Impacto | Mitigação | Owner sugerido |
|----|-------|-------|---------|-----------|----------------|
| R-01 | Vazamento cross-tenant | M | Crítico | RLS + testes security + reviews | Security / Backend |
| R-02 | Scope creep (K8s, Kafka, ES) | A | Alto | ADRs 015–017; roadmap waves | Tech lead humano |
| R-03 | Dual-write mensagem/evento | M | Alto | Outbox obrigatório (ADR-010) | Backend |
| R-04 | Misconfig Keycloak em prod | M | Alto | Realm as code; checklist ops | Infra |
| R-05 | Secrets commitados | M | Crítico | .gitignore; scanning; env only | Todos |
| R-06 | SignalR scale sem backplane | M | Médio | Redis backplane doc + config | Backend / Infra |
| R-07 | Outbox lag sob carga | M | Médio | Métricas + replicas worker | Ops |
| R-08 | Dependência de AI vazar PII | B | Alto | Flag off; redaction; aceite admin | Security / Produto |
| R-09 | Licença/marca indefinidas | A | Alto | `decisoes-pendentes.md` | Legal / Founder |
| R-10 | Retenção legal indefinida | A | Alto | Não apagar em prod até política | Legal |
| R-11 | Agentes divergindo do glossário | A | Médio | orientacoes.md + glossário canônico | Agents lead |
| R-12 | Flaky E2E OIDC | M | Médio | Realm estável; retries controlados | QA |
| R-13 | Postgres como search esgota | B | Médio | ADR-016 gatilhos | Backend |
| R-14 | MinIO disco cheio | M | Alto | Alertas disco; retenção anexos | Ops |
| R-15 | Design genérico (clone Slack) | M | Médio | design-system.md enforce em review | Frontend |
| R-16 | Realtime degradado (só typing) | A | Alto | B-070 gap-fill + E2E dois usuários; métricas SignalR/outbox | Backend / Frontend |
| R-17 | Secrets/webhooks expostos a membros | M | Crítico | B-069 authZ admin-only; nunca logar tokens | Security |
| R-18 | PrimeNG sem tema VibeChat | M | Médio | Emenda ADR-002 + tokens; review visual | Frontend |

## Riscos técnicos detalhados

### R-01 Isolamento

Qualquer endpoint novo é suspeito até prova de teste negativo. Preferir 404 a 403 quando enumeração for risco.

### R-02 Scope creep

Agentes não devem “já deixar pronto Kafka/K8s” sem gatilho. PRs que adicionem esses componentes na fase 1 devem ser rejeitados.

### R-09 / R-10 Legais

Sem licença clara, adoção OSS trava. Sem política de retenção, features de delete/export ficam ambíguas.

## Indicadores de risco emergente

- Outbox dead-letter crescendo
- Aumento de 403/401 anômalos
- Uso de memória Redis sem bound
- Tempo de PR arquitetural sem ADR

## Revisões

Revisar esta lista a cada wave do roadmap ou incidente P0/P1.
