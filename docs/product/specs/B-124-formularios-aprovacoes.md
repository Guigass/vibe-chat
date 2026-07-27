# B-124 — Formulários e aprovações

> Wave 12 · Trilha B/C/D · Deps: B-123 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Solicitações repetíveis chegam como texto livre, dificultando validação,
aprovação e rastreabilidade.

## Escopo

- Form builder com text, number, choice, date, user e attachment.
- Schema versionado e formulário publicado/arquivado.
- Submission imutável com versões de decisão.
- Aprovação single-step ou N-of-M.
- Resultado pode criar task/message por automação.
- Audit e export.

## Fora de escopo

- Fórmulas/scripting arbitrário.
- Workflow visual completo, reservado a B-125.
- Formulário público anônimo.

## Contratos

Manifesto `form.schema.v1`; validation server-side; submission idempotente;
approval decision assinada pelo actor autenticado, sem pretensão de assinatura
digital legal.

## UX

Builder acessível, preview, validação inline, saved draft e timeline de decisão.
Explica quem pode ver respostas.

## Multi-tenant e authZ

Permissões create/publish/submit/review. Reviewer só vê submissions do escopo;
anexos seguem Files/ACL.

## Aceite

- [ ] Schema publicado não muda submissions antigas.
- [ ] Retry não duplica submission/approval.
- [ ] Usuário não aprova como outro actor.
- [ ] Export e retention cobrem respostas.
- [ ] Campo oculto não é aceito por mass assignment.

## Testes

Schema/property tests, authZ, versioning, idempotência, attachments e E2E
builder→submit→approve.

## Riscos

PII estruturada e mass assignment. Classificação, minimização e allowlist de
campos são obrigatórias.

