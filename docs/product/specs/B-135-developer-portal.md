# B-135 — Developer portal

> Wave 15 · Trilha B/D/G · Deps: B-109, B-110 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

APIs, tokens, webhooks e eventos não formam uma experiência única para quem cria
integrações.

## Escopo

- Portal admin/developer com OpenAPI, eventos, capabilities e exemplos.
- Criar/rotacionar/revogar integration credentials.
- Sandbox tenant/dev e webhook inspector com payload sintético.
- Usage, rate limits, errors e audit.
- Changelog/deprecation.
- Download de schemas e contract kit.

## Fora de escopo

- Expor secret novamente.
- Console SQL/admin.
- Billing de API.

## Contratos

Documentação gerada das fontes canônicas; credentials com hash, prefixo e scopes;
test calls claramente sintéticos.

## UX

Quickstart sem secret em URL, copy-once, warning de rotação e explorer que não
persiste token no browser além da sessão.

## Multi-tenant e authZ

`integration.manage` e `integration.read`; cada credential vinculada a tenant,
workspace e channel scopes.

## Aceite

- [ ] OpenAPI/schema correspondem ao runtime.
- [ ] Secret aparece uma vez.
- [ ] Revogação é imediata.
- [ ] Explorer respeita scopes.
- [ ] Usage não cruza tenant.

## Testes

Doc drift/contract, token lifecycle, security scopes, browser storage e E2E
quickstart.

## Riscos

Portal facilitar vazamento de token. Copy-once, CSP, no-store e redaction.

