# Automações Cloud — VibeChat

Pipeline contínuo econômico em 2 etapas, com watchdog diário. Os prompts
canônicos estão nesta pasta; copie o conteúdo para
[cursor.com/automations](https://cursor.com/automations) (a API só lê metadados).

| # | Arquivo | Automação (dashboard) | Trigger sugerido | Ferramentas |
|---|---------|----------------------|------------------|-------------|
| 1 | `01-build.prompt.md` | **Build / Melhorias** | A cada 12 h; concorrência 1 | Open PR, Memories |
| 2 | `02-qa-merge.prompt.md` | **QA + Merge** | Somente PR opened; 1 run por PR | Comment, Approve/Merge, Memories |
| 3 | `03-docs.prompt.md` | **Docs / Close** | **Inativa**; Build fecha roadmap/docs no mesmo PR | — |
| 4 | `04-ux-review.prompt.md` | **UX Review** | **Inativa** durante construção do roadmap | — |
| 5 | `05-security-review.prompt.md` | **VibeChat Security Review** | **Inativa**; CI + revisão humana final | — |
| 6 | `06-watchdog-recovery.prompt.md` | **Watchdog / Recovery** | Diário às 09:00 BRT | Read, Memories, recovery PR |

A automação 5 substitui o template genérico: usa EF Core/Npgsql/Angular, as regras
RLS do projeto e não depende de caminhos internos da Cursor. O check preferido é
`VibeChat Security Review`; o QA ainda aceita temporariamente o nome legado.

A automação 4 **roda a interface de verdade** (`task ux:stack`), navega pelo percurso
de `docs/product/ux-review-checklist.md` e registra o que observou em
`docs/product/ux-findings.md`. Ela não corrige nada — quem corrige é o Build, que
consome UX `Alta` do caminho principal pela safety lane e os demais no modo
manutenção.

## Novas automações adicionadas na revisão

- **Security Review:** substitui o template genérico quebrado e aplica as
  superfícies reais do VibeChat, com veredito amarrado ao `head_sha`.
- **Watchdog / Recovery:** observa `main`, PRs, checks, leases e fechamento Docs;
  só abre recovery PR quando a causa é determinística.

## Automações avaliadas e não criadas

| Ideia | Decisão |
|-------|---------|
| Atualizador autônomo de dependências | Não duplicar B-076/Dependabot-Renovate; PRs gerados passam pelo QA normal |
| Release manager/tag automático | Política existe, mas só automatizar depois dos gates 1.0 e de existir artefato publicável; não publicar externamente por inferência |
| Discovery de novas features | Não automatizar enquanto W7–W17 estiverem abertos; evitar scope creep |
| Performance bot permanente | B-146 define workload/SLO primeiro; antes disso benchmark genérico gera ruído |

## Fluxo

```text
[1 Build] implementa + testa + atualiza roadmap/docs → PR ready
     ↓
GitHub CI executa os gates determinísticos
     ↓
[2 QA] lê CI + revisão focada → squash merge + limpa lease
     ↓
(12 h) → [1 Build] de novo

[6 Watchdog] (diário) → main/checks/leases → relatório ou recovery PR
```

Security, Docs e UX permanecem disponíveis para execução manual, mas não rodam
automaticamente durante a construção do roadmap. Ao final do roadmap, uma rodada
humana cobre segurança profunda, UX e testes exploratórios.

O contrato de autoridade, classes R0–R4, gates e prevenção de loops está em
[`docs/agents/autonomia.md`](../../docs/agents/autonomia.md). Build consome
Waves 7–10 em `roadmap.md` e depois Waves 11–17 em
`horizonte-ambicioso.md`; escolhas técnicas reversíveis não pausam o pipeline.
Cadência, lease, concorrência, watchdog e recuperação estão no
[`runbook 24/7`](../../docs/agents/operacao-24x7.md).
Integridade de specs, riscos, IDs e links segue o contrato em
[`qualidade-documental.md`](../../docs/roadmap/qualidade-documental.md);
o checker executável permanece registrado como `OPS-DOC-CHECKER`.

> **Draft = pipeline parado.** Build e Docs devem abrir PR **ready for review**.
> Se `open_git_pr` criar draft, o agente roda `gh pr ready <n>` antes de encerrar.
> **Proibido** `gh pr ready --undo` / converter para draft — inclusive para
> “pausar” duplicata. Duplicata Docs = **fechar** o extra (`superseded by #n`),
> nunca deixar draft/`CONFLICTING` (incidente #77 após corrida #72+#73 → #76/#77).

## Configuração no dashboard

1. **Build** — habilitar a cada 12 h como “continua construindo”. Desabilitar a
   antiga `VibeChat — Implement` e usar concorrência 1. O mesmo PR implementa,
   atualiza docs necessárias e marca o item `Done`.
2. **QA** — disparar somente em **PR opened**, nunca também em PR pushed. Confiar
   no CI sem repetir as suítes. **Permitir comment + approve/merge**. No GitHub:
   - exigir CI verde para merge;
   - habilitar **Allow auto-merge**;
   - exigir uma aprovação independente quando a política do repositório permitir.
3. **Docs** — manter inativa; o Build fecha roadmap/docs no mesmo PR.
4. **Security** — manter inativa durante o roadmap; CI permanece obrigatório e a
   revisão profunda acontece na rodada humana final.
5. **UX** — manter inativa durante o roadmap.
6. **Watchdog** — schedule diário às 09:00 BRT; escrita apenas para recovery PR,
   nunca push em `main`.
7. **Bugbot nativo** — manter desativado para não duplicar CI + QA.

IDs atuais (conferidos em 2026-07-25 via API de automações):

| Nome no dashboard | ID | Prompt |
|-------------------|-----|--------|
| VibeChat — 1) Build / Melhorias | `24ef1c58-87a9-11f1-b532-320a589b8025` | `01-build.prompt.md`; PR **ready** (nunca draft) |
| VibeChat — 2) QA + Merge | `5589b810-87a9-11f1-b532-320a589b8025` | `02-qa-merge.prompt.md` |
| VibeChat — 3) Docs / Close | `7de10419-87a9-11f1-b532-320a589b8025` | `03-docs.prompt.md`; PR **ready** (nunca draft) |
| VibeChat — 4) UX Review | (criar no dashboard) | `04-ux-review.prompt.md`; PR **ready** (nunca draft) |
| VibeChat — 5) Security Review | (criar no dashboard) | `05-security-review.prompt.md`; required check |
| VibeChat — 6) Watchdog / Recovery | (criar no dashboard) | `06-watchdog-recovery.prompt.md` |
| Security Reviewer legado | `80bb325d-87d6-11f1-b532-320a589b8025` | desabilitar após validar a automação 5 |

O ID `294fd0db-8796-…`, listado aqui antes, não existe mais.

**Obrigatório após editar `.prompt.md`:** colar de novo o texto no dashboard
[cursor.com/automations](https://cursor.com/automations) — a API **não** sincroniza o
prompt sozinha. Prompt antigo no dashboard = comportamento antigo (ex.: draft).

## Estado conhecido — ações externas ao repositório

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

**Aprovação autônoma (bot PRs):** GitHub proíbe auto-aprovação — o autor
(`app/cursor`) não pode aprovar o próprio PR. O job CI **QA Approve** usa
`github-actions[bot]` (identidade distinta) após checks verdes. Requer no org/repo:
**Settings → Actions → General → Allow GitHub Actions to create and approve pull
requests**.

**Ação restante:** conceder `pull-requests: write` ao token Cursor (MCP COMMENT já
funciona; `gh pr review` via CLI ainda 403). Colar prompt atualizado no dashboard.

### 2. `privacy_guard` do Security Reviewer legado falha 100% das vezes

O manifesto do template dispara 10 subagentes; os 2 de `privacy_guard` exigem ler
`internal-docs/docs/privacy/code-data-taxonomy.md`, um caminho interno da Cursor que
não existe aqui. Os dois abortam em todo run — 20% do fan-out queimado, cobertura de
privacidade efetivamente zero.

**Ação:** substituir pelo `05-security-review.prompt.md`, que usa os documentos e
o stack reais. Depois de um PR piloto com check conclusivo, desabilitar o legado.

### 3. Janela de corrida entre Security Reviewer e merge

No PR #38 o Security Reviewer terminou 04:14:43Z e o QA mergeou 04:15:16Z — 33 s de
folga. O template proíbe postar em PR já mergeado, então um finding CRITICAL que
chegasse atrasado seria descartado em silêncio, depois do merge.

**Ação:** tornar o check `VibeChat Security Review` obrigatório na branch
protection de `main`. Durante a migração, manter também o nome legado se necessário.

### 4. `open_git_pr` ainda cria draft

Confirmado no run que abriu o #37. A recuperação com `gh pr ready` funciona e está
nos prompts 01 e 03 — manter até o default da tool mudar.

## Limite anti-overengineering

- **1** Wave/Backlog ID **ou** **1** melhoria pequena por run.
- Sem mudança arquitetural silenciosa. Dependências/serviços novos exigem ADR
  autônomo; Kafka, OpenSearch e K8s continuam dependentes dos gatilhos medidos
  dos ADRs 015–017.
- Se não houver item elegível: só lacunas P0 (bug/teste/doc/segurança) com orçamento
  pequeno quando todo o roadmap estiver terminal. Regressões Critical/High usam
  a safety lane — ver `01-build.prompt.md`.
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
