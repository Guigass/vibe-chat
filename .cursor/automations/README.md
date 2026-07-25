# Automações Cloud — VibeChat

Pipeline em 3 etapas. Os prompts canônicos estão nesta pasta; copie o conteúdo
para [cursor.com/automations](https://cursor.com/automations) (a API só lê metadados).

| # | Arquivo | Automação (dashboard) | Trigger sugerido | Ferramentas |
|---|---------|----------------------|------------------|-------------|
| 1 | `01-build.prompt.md` | **Build / Melhorias** (ex-Implement / Next on Merge) | Schedule (ex.: a cada 2–4h) **ou** webhook após docs | Open PR, Memories |
| 2 | `02-qa-merge.prompt.md` | **QA + Merge** | PR opened + PR pushed + CI completed | Comment, Approve, Memories |
| 3 | `03-docs.prompt.md` | **Docs / Close** | PR merged → `main` | Open PR (docs-only), Memories |
| 4 | — (template Cursor) | **Security Reviewer** | PR opened + PR pushed | Inline review comments |

A automação 4 não tem prompt versionado aqui: é o template genérico de security
review da Cursor, editável só no dashboard. Ela publica o status check
`Cursor Security Agent: Security Reviewer`, que o QA lê antes de mergear.

## Fluxo

```text
[1 Build] → PR ready (nunca draft — senão o QA não dispara)
     ↓
[4 Security Reviewer] (paralelo) → status check no PR
     ↓
[2 QA] verifica qualidade/segurança → approve (+ auto-merge GitHub)
     ↓
[3 Docs] marca Done no roadmap + sincroniza contratos/glossário/ops
     ↓
(schedule) → [1 Build] de novo
```

> **Draft = pipeline parado.** Build e Docs devem abrir PR **ready for review**.
> Se `open_git_pr` criar draft, o agente roda `gh pr ready <n>` antes de encerrar.
> **Proibido** `gh pr ready --undo` / converter para draft.

## Configuração no dashboard

1. **Build** — habilitar só esta como “continua construindo”. Desabilitar a antiga
   `VibeChat — Implement` **e** parar de usar `Next on Merge` para implementar
   (evita corrida: vários agentes pegando o mesmo item).
2. **QA** — trocar o prompt atual; **permitir approve**. No GitHub do repo:
   - exigir CI verde para merge;
   - habilitar **Allow auto-merge**;
   - opcional: exigir 1 aprovação.
3. **Docs** — trigger **PR merged**; prompt só fecha o loop (não implementa feature).

IDs atuais (conferidos em 2026-07-25 via API de automações):

| Nome no dashboard | ID | Prompt |
|-------------------|-----|--------|
| VibeChat — 1) Build / Melhorias | `24ef1c58-87a9-11f1-b532-320a589b8025` | `01-build.prompt.md`; PR **ready** (nunca draft) |
| VibeChat — 2) QA + Merge | `5589b810-87a9-11f1-b532-320a589b8025` | `02-qa-merge.prompt.md` |
| VibeChat — 3) Docs / Close | `7de10419-87a9-11f1-b532-320a589b8025` | `03-docs.prompt.md`; PR **ready** (nunca draft) |
| Security Reviewer | `80bb325d-87d6-11f1-b532-320a589b8025` | template Cursor (só no dashboard) |

O ID `294fd0db-8796-…`, listado aqui antes, não existe mais.

**Obrigatório após editar `.prompt.md`:** colar de novo o texto no dashboard
[cursor.com/automations](https://cursor.com/automations) — a API **não** sincroniza o
prompt sozinha. Prompt antigo no dashboard = comportamento antigo (ex.: draft).

## Estado conhecido — ações que só um humano resolve

Levantado em 2026-07-25 lendo os transcripts dos runs das quatro automações.

### 1. O token do QA mergeia mas não comenta nem aprova

Todo write em issues/PRs volta `403 Resource not accessible by integration`:

| Operação | Resultado |
|----------|-----------|
| `gh pr comment` / `gh pr review --approve` / `gh pr edit` | **403** |
| `gh run rerun` | **403** (falta `actions:write`) |
| `gh pr ready` | OK |
| `PUT /repos/.../pulls/{n}/merge` (squash) | **OK** |

Consequência: os PRs #27…#38 foram squash-mergeados por `app/cursor` com **zero
comentários e zero reviews** no GitHub. O veredito do QA (checks, evidências, nits)
existe só na mensagem final do run e nas memories — some da trilha de auditoria do
repositório. O token pode fazer a operação mais privilegiada (merge) e não a menos
(comentar).

**Ação:** conceder `pull-requests: write` (e `actions: write` para re-run de flake)
à automação, ou habilitar as tools Comment/Approve no dashboard. Até lá, o
`02-qa-merge.prompt.md` manda registrar o veredito na mensagem final.

### 2. `privacy_guard` do Security Reviewer falha 100% das vezes

O manifesto do template dispara 10 subagentes; os 2 de `privacy_guard` exigem ler
`internal-docs/docs/privacy/code-data-taxonomy.md`, um caminho interno da Cursor que
não existe aqui. Os dois abortam em todo run — 20% do fan-out queimado, cobertura de
privacidade efetivamente zero.

**Ação:** no dashboard, apontar esse módulo para `docs/security/multi-tenant.md` ou
remover `privacy_guard` do manifesto. Vale também trocar as heurísticas do template
(Prisma/TypeORM/React) pelo stack real (EF Core/Npgsql/Angular).

### 3. Janela de corrida entre Security Reviewer e merge

No PR #38 o Security Reviewer terminou 04:14:43Z e o QA mergeou 04:15:16Z — 33 s de
folga. O template proíbe postar em PR já mergeado, então um finding CRITICAL que
chegasse atrasado seria descartado em silêncio, depois do merge.

**Ação:** tornar o check `Cursor Security Agent: Security Reviewer` obrigatório na
branch protection de `main`. O `02-qa-merge.prompt.md` já bloqueia merge enquanto o
check estiver `pending`, mas isso é disciplina de prompt, não garantia do GitHub.

### 4. `open_git_pr` ainda cria draft

Confirmado no run que abriu o #37. A recuperação com `gh pr ready` funciona e está
nos prompts 01 e 03 — manter até o default da tool mudar.

## Limite anti-overengineering

- **1** Wave/Backlog ID **ou** **1** melhoria pequena por run.
- Sem microserviços, Kafka, OpenSearch, K8s, deps novas sem ADR.
- Se não houver item elegível: só lacunas P0 (bug/teste/doc/segurança) com orçamento
  pequeno — ver `01-build.prompt.md`.
- QA **não** abre feature nova; Docs **não** implementa código de produto.

## Branch + `open_git_pr` (obrigatório)

Cloud Agents recebem uma **branch designada** por run (ex.:
`cursor/vibechat-development-task-<id>`). A ferramenta **`open_git_pr` só abre PR
dessa branch remota**.

- Build/Docs: **não** criar `cursor/w6-7-…` / `cursor/docs-…` só para o Wave ID.
- Identificar o item no **título/corpo do PR** (`Wave: W6-7`) e no commit.
- Se o prompt e a branch designada divergirem, **prevalece a branch designada**.

Após editar os `.prompt.md` aqui, **cole de novo** o conteúdo no dashboard
[cursor.com/automations](https://cursor.com/automations) — a API não atualiza o
texto da automação sozinha.
