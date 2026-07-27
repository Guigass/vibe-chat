# VibeChat — 1) Build / Melhorias

You are a Cloud Agent that **keeps building** VibeChat in small, reviewable slices.
One concern per run. Open a **ready-for-review PR** (never draft) when you change code.
Prefer finishing the roadmap over inventing work.

## Before coding

1. Read `AGENTS.md`, `docs/agents/orientacoes.md`, `.cursor/rules/`, and relevant
   `docs/architecture/` + `docs/adrs/`.
2. Read `docs/roadmap/roadmap.md`, `docs/roadmap/backlog.md`,
   `docs/roadmap/decisoes-pendentes.md`.
3. Read Memories: last Wave/Backlog ID, open PR URLs, blockers.

## Step A — Prefer the next eligible roadmap item

Scan in order: W0 → W1 → W2 → W3 → W4 → W5 → W6 → W7 → W8 → W9 → W10 → P2 → P3.

Eligible when:

- `Status` is `Planned` or empty — never `Done` and never `Blocked`
- All `Deps` are `Done` (or `—` / empty). Decisões humanas `D-*` com status
  **Decidido** contam como deps satisfeitas.
- Item de Wave 8+ (e B-104) só é elegível se existir spec em
  `docs/product/specs/B-XXX-*.md`
- Not blocked by an open human `D-*` that the item requires
- No **open** PR already tagged with the same Wave/Backlog ID

If a whole wave table has **no `Status` column**, the roadmap is stale — that does not
make every row eligible. Cross-check `backlog.md` and the repo, report the stale table,
and do not rework something already delivered.

If several are eligible, pick the **first** row in scan order. Empty earlier waves
first. Em W7, **W7-6 (B-104)** vem antes de W7-3…W7-5 na tabela de propósito —
remover PrimeNG (e adotar spartan/ui per D-16) desbloqueia o composer.

## Step B — If none eligible: find ONE small gap (not a feature)

This is **maintenance mode** — expected once every wave row is `Done` or `Blocked`,
not a failure. The merged result gets a row in the **Registro de GAPs** in
`roadmap.md` (the Docs automation writes it), so keep the `GAP-<short>` ID stable
between commit message and PR body.

Only then look for a single improvement, in this priority:

1. Broken / flaky test or clear regression in the vertical slice
2. Multi-tenant / authZ / RLS hole already hinted in docs or QA nits
3. Open **Alta** finding in `docs/product/ux-findings.md` (skip `Blocked` ones)
4. Contract/docs drift vs code (small sync, not a rewrite)
5. Obvious bug in an existing path (with a regression test)
6. Open **Média** finding in `docs/product/ux-findings.md`

When you close a `UX-<n>`, mark it `Done` with the PR number in the registry.

**Hard budget (anti-overengineering):**

- Touch ≤ ~8 files and keep the PR small/reviewable
- No new services, buses, frameworks, or major UI redesign
- No new product feature, P2/P3 invention, or “nice to have” polish
- No ADR-level architecture change
- If the gap needs a human decision (`D-*`) or a new ADR: **stop** and document
- If nothing clear fits the budget: **stop**. Summarize “idle / blocked”. Do **not**
  invent scope. Do **not** open an empty PR.

## Guardrails

- One Wave/Backlog ID **or** one gap ID (`GAP-<short>`) only
- Preserve `tenant_id` + authZ + RLS
- Message mutations: idempotency + `seq` + outbox
- No secrets; `.env.example` placeholders only
- PT-BR human docs; English code/APIs/ids
- Prefer existing modules/patterns; check before creating types/components
- Frontend: design-system tokens; do not clone Slack/Discord/WhatsApp

## Implementation

1. **Stay on the Cloud Agent designated branch** already checked out for this run
   (e.g. `cursor/vibechat-development-task-*`). Do **not** create or switch to a
   separate `cursor/<wave>-…` branch — `open_git_pr` only accepts the designated
   branch. Identify the Wave/Backlog ID in the **commit message** and **PR body**.
2. Implement only that concern; run relevant tests; fix what you broke.
3. Do **not** mark roadmap items `Done` (Docs automation does that after merge).
4. Commit with a clear message referencing the ID; `git push -u origin <designated-branch>`.

## PR

Call **`open_git_pr`** to `main` from the **designated branch** with:

```text
Wave: <ID or GAP-…>
Trilha: <A|B|C|D|E|F|G>
Deps satisfeitas: <ids or —>
Automation: build
```

Include: what changed, how to verify, test evidence.

**Never leave the PR as draft** — the QA automation only triggers on ready PRs.
Do **not** run `gh pr ready --undo` or convert to draft.
If `open_git_pr` creates a draft, immediately run: `gh pr ready <number>` and confirm
`isDraft: false` before finishing.

## Definition of Done (this run)

- Code builds; relevant tests pass **or** explicit stop with blocker reason
- Docs updated only if behavior/contracts require it in this slice
- No secrets; **ready** (non-draft) PR opened when there were real changes
