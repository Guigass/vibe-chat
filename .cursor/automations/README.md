# Automações Cloud — VibeChat

Pipeline em 3 etapas. Os prompts canônicos estão nesta pasta; copie o conteúdo
para [cursor.com/automations](https://cursor.com/automations) (a API só lê metadados).

| # | Arquivo | Automação (dashboard) | Trigger sugerido | Ferramentas |
|---|---------|----------------------|------------------|-------------|
| 1 | `01-build.prompt.md` | **Build / Melhorias** (ex-Implement / Next on Merge) | Schedule (ex.: a cada 2–4h) **ou** webhook após docs | Open PR, Memories |
| 2 | `02-qa-merge.prompt.md` | **QA + Merge** | PR opened + PR pushed + CI completed | Comment, Approve, Memories |
| 3 | `03-docs.prompt.md` | **Docs / Close** | PR merged → `main` | Open PR (docs-only), Memories |

## Fluxo

```text
[1 Build] → draft PR
     ↓
[2 QA] verifica qualidade/segurança → approve (+ auto-merge GitHub)
     ↓
[3 Docs] marca Done no roadmap + sincroniza contratos/glossário/ops
     ↓
(schedule) → [1 Build] de novo
```

## Configuração no dashboard

1. **Build** — habilitar só esta como “continua construindo”. Desabilitar a antiga
   `VibeChat — Implement` **e** parar de usar `Next on Merge` para implementar
   (evita corrida: vários agentes pegando o mesmo item).
2. **QA** — trocar o prompt atual; **permitir approve**. No GitHub do repo:
   - exigir CI verde para merge;
   - habilitar **Allow auto-merge**;
   - opcional: exigir 1 aprovação.
3. **Docs** — trigger **PR merged**; prompt só fecha o loop (não implementa feature).

IDs atuais (referência):

| Nome atual | ID | Ação |
|------------|-----|------|
| VibeChat — Implement | `b70c8134-8795-11f1-b532-320a589b8025` | Desabilitada — reutilizar como **Build** ou criar nova |
| VibeChat — QA / Review | `294fd0db-8796-11f1-b532-320a589b8025` | Atualizar prompt → **QA + Merge** |
| VibeChat — Next on Merge | `75495d53-8796-11f1-b532-320a589b8025` | Atualizar prompt → **Docs / Close** (sem implementar) |

## Limite anti-overengineering

- **1** Wave/Backlog ID **ou** **1** melhoria pequena por run.
- Sem microserviços, Kafka, OpenSearch, K8s, deps novas sem ADR.
- Se não houver item elegível: só lacunas P0 (bug/teste/doc/segurança) com orçamento
  pequeno — ver `01-build.prompt.md`.
- QA **não** abre feature nova; Docs **não** implementa código de produto.
