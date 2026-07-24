# ADR-014: Estratégia de testes

## Status: Accepted

## Contexto

Invariantes críticos (idempotência, sequência, RLS, membership) não podem depender só de QA manual. Agentes e humanos precisam de pirâmide de testes executável em CI.

## Decisão

Adotar a seguinte estratégia:

| Camada | Onde | Foco |
|--------|------|------|
| Unit | `tests/unit` | Domínio, seq/idempotency, parsers, policies |
| Integration | `tests/integration` | API + Postgres + Redis (Testcontainers) |
| Architecture | `tests/architecture` | Dependências entre módulos (NetArchTest) |
| Security | `tests/security` | Isolamento multi-tenant, authZ negativas |
| E2E | `tests/e2e` | Playwright: login → send → receive |
| Load | `tests/load` | k6 ou similar — smoke de fan-out (não gate inicial) |

Regras:

- Todo PR que toca Messaging deve manter testes de idempotência + seq verdes
- Testes de RLS são bloqueantes para release da fatia vertical
- E2E mínimos no caminho feliz OIDC (realm de test)
- Flaky tests são bug — quarentena com issue, não ignore silencioso

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Só E2E | Lentos/frágeis; feedback ruim |
| Só unit com mocks excessivos | Não pega RLS nem outbox real |
| QA manual apenas | Não escala com agentes paralelos |

## Consequências

- **+** Confiança para refatorar módulos
- **+** Contratos de segurança executáveis
- **−** CI precisa de Docker/Testcontainers
- **−** Manutenção de seed Keycloak para E2E
