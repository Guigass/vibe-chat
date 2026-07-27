# Requisitos Comuns — Waves 11–17

Toda spec de longo prazo incorpora este documento. Se houver conflito, a regra
mais segura prevalece; exceção precisa estar explícita na spec e em ADR.

Defaults R3: [`architecture/pacotes-decisao-r3.md`](../../architecture/pacotes-decisao-r3.md).
Convenções futuras de contratos/flags:
[`architecture/catalogo-evolucao-contratos.md`](../../architecture/catalogo-evolucao-contratos.md).

## Contratos e dados

- API sob `/api/v1` até política de versionamento de B-139 entrar.
- IDs opacos; tempo em UTC; paginação por cursor estável.
- Toda entidade de negócio tem `TenantId` e, quando aplicável, `WorkspaceId`.
- Mutation aceita idempotency key quando retry pode duplicar efeito.
- Evento durável usa outbox; consumidor é idempotente.
- Migration é forward-compatible com rolling upgrade quando B-144 se aplicar.
- Delete, purge, legal hold e export cobrem projeções derivadas.
- Derivados seguem provenance e separação de
  [`estado/outbox/audit/projeções`](../../architecture/estado-eventos-auditoria-projecoes.md).
- Principals e memberships seguem o
  [`modelo canônico de identidade`](../../architecture/modelo-identidade-principals.md).
- Realtime/offline segue o
  [`protocolo de sincronização`](../../architecture/protocolo-sync-realtime.md).
- Dados e anexos seguem a
  [`matriz de lifecycle`](../../security/ciclo-vida-dados.md) e o
  [`pipeline canônico`](../../architecture/pipeline-anexos.md).

## Segurança

- Tenant vem do contexto autenticado, nunca do body.
- AuthZ no servidor; esconder botão não é controle.
- RLS e teste cross-tenant para tabela nova.
- Secret só hash/secret store; resposta mostra máscara.
- URL externa aplica proteção SSRF, egress policy, timeout e redirects seguros.
- Conteúdo externo é não confiável e sanitizado.
- Ação sensível gera audit sem PII/secret desnecessário.

## UX

- Estados loading, empty, partial, error, offline e permission denied.
- Keyboard, foco, screen reader, contraste e reduced motion.
- `pt-BR` e `en`.
- Mobile/responsive mesmo antes do client nativo.
- Operação destrutiva pede confirmação e explica consequência.
- Feature opcional indisponível degrada sem quebrar chat.

## Operação

- Métricas, logs estruturados e traces com correlation/tenant seguro.
- Health/readiness quando houver dependência nova.
- Limites, quotas, timeout, retry/backoff e circuit breaker.
- Feature flag off por default para R3.
- Configuração no catálogo `.env`/admin com source efetiva.
- Runbook de falha, rollback e capacity assumptions.

## Testes mínimos

- unitários de regra/invariante;
- integração de persistência e evento;
- security negativo cross-tenant/authZ;
- E2E do caminho principal e failure state;
- architecture/contract tests quando fronteira mudar;
- load/resilience proporcional para worker, fan-out ou serviço novo.

## Definition of Done

Além do aceite individual:

- contrato, glossário, threat model e ops sincronizados;
- nenhuma dependência proprietária obrigatória;
- licença/provenance verificadas;
- `task verify` ou conjunto equivalente verde;
- evidência anexada ao PR;
- sem follow-up necessário para tornar o caminho principal seguro.
- release/compatibilidade alinhadas a
  [`operations/release-versionamento-suporte.md`](../../operations/release-versionamento-suporte.md).

