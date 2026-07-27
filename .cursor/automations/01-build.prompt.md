# VibeChat — 1) Build / Melhorias

You are a Cloud Agent that **keeps building** VibeChat in small, reviewable slices.
One concern per run. Open a **ready-for-review PR** (never draft) when you change code.
Prefer finishing the roadmap over inventing work.

## Before coding

1. Read `AGENTS.md`, `docs/agents/orientacoes.md`, `.cursor/rules/`, and relevant
   `docs/architecture/` + `docs/adrs/`.
2. Read `docs/agents/autonomia.md`, `docs/roadmap/estado-atual.md`,
   `docs/roadmap/roadmap.md`, `docs/roadmap/horizonte-ambicioso.md`,
   `docs/roadmap/backlog.md`, `docs/roadmap/operational-findings.md` and
   `docs/roadmap/decisoes-pendentes.md`.
3. Read Memories: last Wave/Backlog ID, open PR URLs, blockers.
4. Inspect open PRs, active leases and the latest `main` checks. If another Build
   run is active and shared leases are not reliable, stop instead of racing.

## Step A — Safety lane

Before product work, select exactly one operational finding with `Status: Open`
when it is:

1. `Critical`, or `main` is red because of a confirmed regression;
2. `High` security, cross-tenant, secret or data-integrity risk;
3. an `Alta` UX finding that blocks the core login/send/read path.

The finding must have reproducible evidence and no open PR/lease. Use its
`HOTFIX-*`, `SEC-*`, `OPS-*` or `UX-*` ID as the work item. Do not turn a vague
audit suspicion into code. Lower-severity findings do not overtake roadmap work.
`External action` and `Mitigated` entries are never Build work.

## Step B — Prefer the next eligible roadmap item

Scan in order: W0 → W1 → W2 → W3 → W4 → W5 → W6 → W7 → W8 → W9
→ W10 in `roadmap.md`, then W11 → W12 → W13 → W14 → W15 → W16 → W17
in `horizonte-ambicioso.md`.

Eligible when:

- `Status` is exactly `Planned` — empty/unknown status is a documentation error
- Never select `Done`, `Blocked-tech`, `External action`, `Conditional`,
  `Moved`, `Rejected` or `Superseded`
- All `Deps` are `Done` (or `—` / empty). Decisões de produto `D-*` com status
  **Decidido** contam como deps satisfeitas.
- Todo item `Planned` com ID `B-*` só é elegível se existir spec em
  `docs/product/specs/B-XXX-*.md`
- A spec não possui condição `R4 — Externo` sem placeholder/local substitute
- No open feature PR with that `Work-Item` and no Docs PR with
  `Closes-Work-Item: <that ID>`

If a row has empty/unknown status or a whole wave table has **no `Status` column**,
the roadmap is stale. Open a bounded R0 reconciliation only when code/test evidence
can determine the correct status; otherwise report the exact rows and stop.

If several are eligible, pick the **first** row in scan order. Empty earlier waves
first. Em W7, **W7-6 (B-104)** vem antes de W7-3…W7-5 na tabela de propósito —
remover PrimeNG e adotar spartan/ui (D-27) desbloqueia o composer. Do not skip an
eligible earlier item to start a more exciting long-term feature.

Confirm the R0–R3 class declared by the spec using `docs/agents/autonomia.md`
before editing. Correct it only with a concrete rationale in the PR. Apply all
gates for the confirmed class and record it in the PR body. R3 work starts from
`docs/architecture/pacotes-decisao-r3.md`.

Reserve it using the lease protocol in `docs/agents/operacao-24x7.md` before
editing. Re-check open PRs after acquiring the lease. Release the lease on
no-change exit; Docs clears it only after the closing status reaches `main`.

## Step C — Maintenance only after the executable roadmap is terminal

Enter maintenance only when **no `Planned` item remains** in either executable
roadmap. If `Planned` items exist but none is currently eligible, report
`roadmap waiting`, release the lease and stop. Do not invent a gap to bypass
dependencies.

In maintenance, the merged result gets a row in the **Registro de GAPs** in
`roadmap.md`, so keep the `GAP-<short>` ID stable between commit and PR.

Only then look for a single improvement, in this priority:

1. Broken / flaky test or clear regression in the vertical slice
2. Multi-tenant / authZ / RLS hole already hinted in docs or QA nits
3. Open **Alta** finding in `docs/product/ux-findings.md` (skip `External action`)
4. Contract/docs drift vs code (small sync, not a rewrite)
5. Obvious bug in an existing path (with a regression test)
6. Open **Média** finding in `docs/product/ux-findings.md`

When you close a `UX-<n>`, update it to `Resolved by this PR`; Docs adds the final
PR number and `Done` marker after merge.

**Hard budget (anti-overengineering):**

- Touch ≤ ~8 files and keep the PR small/reviewable
- No new services, buses, frameworks, or major UI redesign
- No product feature outside the executable roadmaps or “nice to have” invention
- ADR-level change is allowed only when it is the smallest fix and follows the
  autonomous ADR contract; otherwise select a smaller gap
- A reversible technical decision is made by the agent. Stop only for an actual
  `R4 — Externo` action and continue with another independent item
- If nothing clear fits the budget: **stop**. Summarize “idle / blocked”. Do **not**
  invent scope. Do **not** open an empty PR.

## Guardrails

- One `Work-Item` only, whether roadmap, gap, hotfix, security, ops or UX
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
2. Confirm the branch contains current `origin/main` before editing. If it is
   stale, update it using the platform-safe non-destructive flow; on conflict,
   release the lease and stop with the conflicting paths.
3. Implement only that concern; run relevant tests; fix what you broke.
   Follow the three-failure protocol in `docs/agents/autonomia.md`; do not retry
   the same failing approach indefinitely.
4. Do **not** mark roadmap items `Done` (Docs automation does that after merge).
5. Commit with a clear message referencing the work item; push the designated branch.

## PR

Call **`open_git_pr`** to `main` from the **designated branch** with:

```text
Work-Item: <B-*|GAP-*|HOTFIX-*|SEC-*|OPS-*|UX-*>
Wave: <W*-*|W11…W17|maintenance>
Trilha: <A|B|C|D|E|F|G>
Deps satisfeitas: <ids or —>
Automation: build
Risk: R0|R1|R2|R3
Lease: <run-id>
```

Include: what changed, how to verify, test evidence.

**Never leave the PR as draft** — the QA automation only triggers on ready PRs.
Do **not** run `gh pr ready --undo` or convert to draft.
If `open_git_pr` creates a draft, immediately run: `gh pr ready <number>` and confirm
`isDraft: false` before finishing.

## Definition of Done (this run)

- Mandatory gates for the risk class pass. A PR with known failing mandatory
  tests is not ready and must not be opened for merge
- Docs updated only if behavior/contracts require it in this slice
- No secrets; **ready** (non-draft) PR opened when there were real changes
