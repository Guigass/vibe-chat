# B-136 — SDK e contract-test kit

> Wave 15 · Trilha B/C/E/G · Deps: B-066, B-139 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Integrações implementam autenticação, assinatura, retry e schemas de formas
inconsistentes.

## Escopo

- SDK oficial inicial TypeScript e .NET, ambos OSS.
- Cliente API gerado/curado, webhook verifier e idempotency helpers.
- Contract-test container/runner com payloads sintéticos.
- Semantic versioning e compatibility matrix.
- Exemplos de bot, slash, event consumer e connector.
- Supply-chain provenance/publicação reproduzível.

## Fora de escopo

- SDK para todas as linguagens.
- Esconder HTTP/contratos atrás de abstração proprietária.
- Embutir credential.

## Contratos

SDK deriva OpenAPI/JSON Schema; comportamento manual mínimo e testado contra
runtime suportado.

## UX

Developer docs mostram instalação, first message e falhas comuns; exemplos não
contêm domínio/secret real.

## Multi-tenant e authZ

SDK não aceita “bypass tenant”; token determina scope. Test kit inclui casos
401/403/cross-tenant.

## Aceite

- [ ] Quickstart executa contra sandbox.
- [ ] Webhook verifier rejeita replay/assinatura inválida.
- [ ] Matrix de versão é testada.
- [ ] Pacotes têm license/provenance.
- [ ] SDK não loga token.

## Testes

Contract tests cross-version, package install, signature vectors, integration e
secret scan.

## Riscos

SDK virar contrato paralelo. Gerar de fontes e tratar HTTP/schema como canônico.

