# B-121 — Busca semântica e RAG autorizado

> Wave 12 · Trilha B/C/D/E · Deps: B-098, D-22 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

FTS não cobre bem perguntas conceituais e resumos entre fontes, mas indexação
semântica ingênua pode reintroduzir conteúdo revogado ou cross-tenant.

## Escopo

- Feature opt-in por processo e workspace.
- Provider-neutral embedding/completion ports.
- Indexar mensagens/pages permitidas em partições por tenant/workspace.
- Retrieval revalida ACL na consulta.
- Resposta com citações navegáveis e nível de confiança.
- Propagação de edit/delete/purge e rebuild.
- Budget, rate-limit, audit de uso e painel de lag.

## Fora de escopo

- Treinar modelo com dados do cliente.
- Responder sem fonte quando a pergunta depende do workspace.
- Index global cross-tenant.

## Contratos

Jobs assíncronos via outbox; índice é projeção descartável. API recebe query e
escopo, nunca TenantId. ADR escolhe storage/provider OSS ou Postgres conforme
benchmark e ADR-016.

## UX

Modo claramente rotulado como IA; citações obrigatórias; estado “sem evidência”
é válido. Usuário pode reportar resposta e abrir fontes.

## Multi-tenant e authZ

Tenant partition + ACL filter + revalidação da fonte antes de exibir trecho.
Conteúdo de DM/private só para membros atuais.

## Aceite

- [ ] Resultado nunca contém fonte inacessível.
- [ ] Delete/revoke deixa de recuperar conteúdo dentro do SLO definido.
- [ ] Toda afirmação workspace-grounded tem citação.
- [ ] Kill switch remove funcionalidade sem quebrar busca FTS.
- [ ] Budget e audit funcionam.

## Testes

Security adversarial cross-tenant/revocation, projection rebuild, fake provider,
load/latency e E2E de citações.

## Riscos

Embeddings retêm semântica de conteúdo apagado. Delete propagation, rebuild,
criptografia/isolamento e documentação de retenção são blockers.

