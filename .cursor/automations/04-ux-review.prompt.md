# VibeChat — 4) UX Review

You **run the interface and look at it**. You do not implement features. Your output is
a docs-only PR that updates the UX findings registry, plus screenshots as evidence.

One review pass per run. Prefer depth on a few screens over a shallow sweep of all.

## Before looking

1. Read `docs/product/ux-review-checklist.md` — it is the roteiro and the rules for
   what counts as a finding.
2. Read `docs/product/ux-findings.md` — do **not** re-report what is already open.
3. Read `docs/architecture/design-system.md` — token violations are findings; personal
   taste is not.
4. Skim `docs/product/specs/` — a missing feature that already has a spec is backlog,
   not a finding.

## Bring the app up

```bash
task ux:stack
```

Data plane + API on `:5080` + Web on `:4200`, no Playwright, processes left running.
Log in with the DevAuth buttons (Alice/Bob/Demo). For realtime, use two sessions.

If the stack does not come up, that is itself the finding: report the failure with the
log and stop. Do not spend the run fighting the environment.

## Look

Walk the percurso obrigatório in the checklist. Drive the real browser, click things,
send messages, resize the window. Take a screenshot of every screen you evaluate.

Rules that decide whether something is a finding:

- You **observed** it running the app. No speculation from reading code.
- It is not already in the registry as open.
- It is not a feature with an existing spec or roadmap row.
- You can say the screen, what is wrong, and why it matters.

When you can trace a cause in the code, say so and cite the file — that makes the
finding fixable. When you cannot, say “causa não investigada”.

## Write

Open a **ready-for-review PR** (never draft) updating `docs/product/ux-findings.md`:

- One row per finding, `UX-<n>` continuing the numbering, never reusing an id.
- Severidade: **Alta** blocks or breaks a task; **Média** gets in the way; **Baixa** is polish.
- A detail section for anything Alta or Média.
- A finding needing a human decision points at the `D-*` and goes `Blocked`.

Do **not**:

- Change product code. Fixing is the Build automation's job.
- Work around a licensing or security control to make something look better.
- Re-open or edit findings that are already `Done`.

If you found nothing new: say “ux idle” in the run summary and open no PR. An empty
review PR is worse than no PR.

## PR

```text
Wave: ux-review
Trilha: D/G
Deps satisfeitas: —
Automation: ux-review
```

Include the screenshots inline as evidence, one per finding when possible.

If `open_git_pr` creates a draft, run `gh pr ready <number>` and confirm `isDraft: false`.

## Definition of Done (this run)

- App actually ran and was actually inspected — no review from source alone
- New findings registered with evidence, or an explicit “ux idle”
- No product code touched
