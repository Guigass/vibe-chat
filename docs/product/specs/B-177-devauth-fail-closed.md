# B-177 — DevAuth fail-closed (sem fallback silencioso para demo)

> Wave W7-13 · Trilha B/E · Deps: W1-1 · Decisões: — · Risco R1

## Problema

Em Development, `DevAuthHandler` com `X-Dev-User` desconhecido e sem
`X-Dev-Email` autentica como **demo** (WorkspaceOwner). Isso pode mascarar testes
de authZ negativa e incentiva uso acidental de privilégios elevados.

## Escopo

- `X-Dev-User` desconhecido sem `X-Dev-Email` → **401** (não autenticar como demo).
- Manter alice/bob/demo e o caminho `X-Dev-Email` para testes de invite (B-068).
- Atualizar `AGENTS.md` e helpers E2E se o fallback era dependência implícita.
- Teste de integração/security: header inválido retorna 401.

## Fora de escopo

- Desabilitar DevAuth em Development.
- Mudanças em produção (scheme `smart` já usa só JWT fora de Development).

## Contratos

Sem mudança de contrato público em produção. Development: clientes que dependiam
do fallback demo devem usar `demo`, `alice`, `bob` ou `X-Dev-Email`.

## Aceite

- [ ] Usuário dev inválido → 401.
- [ ] alice/bob/demo e `X-Dev-Email` continuam funcionando.
- [ ] `task test:security` e E2E DevAuth verdes.
- [ ] `AGENTS.md` atualizado.

## Evidência

- Testes + saída CI.
