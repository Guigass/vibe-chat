# B-115 — Templates e onboarding de workspace

> Wave 11 · Trilha B/D · Deps: B-106 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Criar estrutura, papéis e canais manualmente torna o primeiro uso demorado e
inconsistente.

## Escopo

- Templates built-in versionados para time, projeto, comunidade e incidentes.
- Preview antes de aplicar.
- Criar spaces, channels, descrições e policy defaults permitidos.
- Checklist de onboarding para owner/admin.
- Template customizado exportável/importável sem dados pessoais.
- Aplicação idempotente e com dry-run.

## Fora de escopo

- Copiar mensagens, membros ou secrets.
- Executar script arbitrário.
- Marketplace de templates com código.

## Contratos

Manifesto declarativo versionado `vibechat.workspace-template.v1`; endpoint
validate/preview/apply; audit por recurso criado.

## UX

Wizard com escolha, preview, conflitos, progresso e resumo. Permite pular e
retomar; não obriga template.

## Multi-tenant e authZ

Somente `workspace.admin`. Template não contém TenantId e todo recurso recebe o
tenant do contexto.

## Aceite

- [ ] Built-in cria estrutura prevista.
- [ ] Dry-run não persiste.
- [ ] Retry não duplica.
- [ ] Import rejeita campo/versão desconhecidos com erro útil.
- [ ] Nenhum membro/secret é exportado.

## Testes

Schema/contract tests; integração apply/retry/rollback; security; E2E do wizard.

## Riscos

Template virar linguagem de execução. Manter catálogo estritamente declarativo e
allowlist de recursos.

