# B-127 — Conectores bidirecionais

> Wave 13 · Trilha B/C/D/E · Deps: B-108, B-125 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Webhooks genéricos não oferecem sincronização segura de issues, calendário e
eventos com ferramentas externas.

## Escopo

- Framework de connector com OAuth2/token scoped e secret store.
- Primeiro catálogo: GitHub/GitLab issue events e calendário via padrão aberto;
  adapters adicionais seguem o mesmo contrato.
- Inbound verify/dedupe; outbound retry/backoff/circuit breaker.
- Mapping explícito para channel/task/incident.
- Health, reconnect, rotate e revoke.
- Egress allowlist e SSRF protections.

## Fora de escopo

- Credenciais reais no repositório.
- Suportar toda API de cada fornecedor.
- Impersonar usuário sem consentimento.

## Contratos

Connector manifest/capabilities; external identity mapping; cursor/checkpoint e
delivery log redigido. Provider específico isolado em adapter.

## UX

Admin escolhe connector, scopes e destino; OAuth/secret state é mascarado.
Erros mostram ação de recuperação sem expor payload.

## Multi-tenant e authZ

Config e credentials por tenant/workspace. Inbound resolve tenant por endpoint
opaco/assinatura, nunca por payload.

## Aceite

- [ ] Evento repetido não duplica efeito.
- [ ] Revogar encerra chamadas.
- [ ] URL privada/metadata é bloqueada.
- [ ] Scopes mínimos são exibidos.
- [ ] Falha externa não bloqueia outbox geral.

## Testes

Fake servers, SSRF redirects/DNS rebinding, OAuth/token rotation, idempotência,
cross-tenant e E2E sandbox/mocks.

## Riscos

Dependência de APIs mutáveis e exfiltração. Contract tests, version pin,
circuit breaker e adapters isolados.

