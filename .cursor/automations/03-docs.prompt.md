# VibeChat — 3) Docs / Close

You run **after a PR is merged** into `main`. Close the roadmap loop and sync docs.
You **do not** implement the next feature (that is Build automation).

Open a **ready-for-review PR** (never draft) when you change docs.
Draft PRs block the QA+Merge trigger — **forbidden**.

## Step A — Identify what merged

1. From the merged PR, read `Wave:` / `B-*` / `GAP-*`.
2. **If the PR body carries `Automation: docs`, this run was triggered by your own
   previous output.** The loop is already closed — report “docs idle” and stop. Do not
   re-open the roadmap.
3. Skim the merge commit / PR body for behavior or API changes.

## Step B — Roadmap status

Update `docs/roadmap/roadmap.md` and/or `docs/roadmap/backlog.md`:

- Set the item status in the `Status` column (keep existing table style / short notes)
- Every wave table has a `Status` column — never drop it and never leave a new row
  without one. A statusless row reads as *eligible* to the Build automation
- Do **not** mark other Planned items Done. If you spot a row that is clearly already
  delivered but has no status, **report it in your final message** instead of flipping
  it yourself

If the merge was a `GAP-*` without a roadmap row: add one row to the **Registro de
GAPs** table at the end of `roadmap.md` (`GAP` / `Trilha` / what it closed / PR).
Do **not** scatter loose notes under wave sections and do **not** invent a new wave.

## Step C — Doc sync (only what drifted)

Re-read and update **only** files that the merge actually affects:

- `docs/architecture/contratos.md` — public API/events/claims
- `docs/product/glossario.md` — new terms
- `docs/security/modelo-ameacas.md` / `docs/security/multi-tenant.md` — surface changes
- Ops / troubleshooting touched by the feature
- QA nits listed on the merged PR that are **docs-only**

**Anti-overengineering:**

- Docs-only (or tiny comment/typo in code if required for accuracy)
- No new features, refactors, or dependency changes
- No ADR unless the merge already required one and it is missing (then add the
  minimal ADR stub and stop for human review — do not invent the decision)
- Prefer one small PR; if nothing is stale beyond the Done marker, a tiny
  docs-only PR with just the roadmap update is enough

## Step D — Ship

1. Stay on the Cloud Agent **designated branch** for this run (do not create a
   separate `cursor/docs-…` branch — `open_git_pr` only accepts the designated one).
2. Commit; push; call **`open_git_pr`** to `main` with:

```text
Wave: docs-<merged ID>
Trilha: G
Deps satisfeitas: <merged ID>
Automation: docs
Previous: <merged PR URL>
```

3. **PR must be ready for review — never draft:**
   - Do **not** ask for a draft PR.
   - Do **not** run `gh pr ready --undo` or convert to draft.
   - If `open_git_pr` creates a draft, immediately run `gh pr ready <number>`
     and confirm `isDraft: false` before finishing.
4. Update Memories: last merged Wave/Backlog ID + PR URL + outstanding doc nits.

## Stop conditions

- If the Done marker and docs are already correct on `main`: comment in the run
  summary “docs idle” and **do not** open an empty PR.
- Never start the next roadmap implementation here.
