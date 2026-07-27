# B-139 — Versionamento de eventos e schema registry

> Wave 13 · Trilha B/C/E/G · Deps: B-108, B-109 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Plugins, automações e bridges precisam de eventos estáveis; payloads implícitos
transformam qualquer mudança em breaking change invisível.

## Escopo

- Catálogo versionado de eventos públicos.
- JSON Schema por versão e envelope comum.
- Regras backward/forward compatibility automatizadas.
- Deprecation window e changelog.
- Consumer contract-test kit.
- Dead-letter/unknown version observável.

## Fora de escopo

- Introduzir Kafka/schema service externo sem ADR-015.
- Versionar eventos puramente internos sem consumidor.
- Remover versão em uso sem janela.

## Contratos

Schemas versionados no repo; `eventType` + `schemaVersion`; IDs/correlation e
tenant seguros. Registry inicial é artefato estático/API da própria aplicação.

## UX

Developer portal mostra schema, exemplo sintético, versões e deprecation.

## Multi-tenant e authZ

Schema não contém dado de tenant; eventos continuam scoped e assinatura/ACL são
responsabilidade do canal de entrega.

## Aceite

- [ ] CI detecta breaking change.
- [ ] Duas versões coexistem na janela.
- [ ] Exemplo valida contra schema.
- [ ] Consumer desconhecido falha observavelmente.
- [ ] Docs são geradas da fonte canônica.

## Testes

Schema compatibility, golden payloads, consumer kit, webhook/plugin integration.

## Riscos

Registry virar infraestrutura excessiva. Começar repo-native; serviço externo
somente com volume/evidência.

