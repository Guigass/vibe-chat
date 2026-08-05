# VibeChat — 3) Docs / Close

You run **after a PR is merged** into `main`. Close the roadmap loop and sync docs.
You **do not** implement the next feature (that is Build automation).

Open a **ready-for-review PR** (never draft) when you change docs.
Draft PRs block the QA+Merge trigger — **forbidden**.

## Step A — Identify what merged

1. Confirm the PR is actually merged into `main` and its merge commit is
   reachable from current `origin/main`.
2. Read `Work-Item`, `Wave`, `Risk`, `Lease` and QA verdict from the PR,
   check summary or automation evidence. Read QA nits from the durable verdict
   and Memories when needed. For an unambiguous legacy PR, infer the single old
   `Wave`/`B-*` ID and write normalized metadata in the Docs PR.
3. **If the PR body carries `Automation: docs`, this run was triggered by your own
   previous output.** Read `Closes-Work-Item`, confirm that item is now `Done` on
   `main`, clear its `ACTIVE:<ID>` lease, then **sweep orphans** (step A.6) and
   report “docs loop closed” and stop. If it is not `Done`, open one corrective
   R0 docs PR instead of silently stopping.
4. **Dedup before any edit or PR (mandatory):** list open PRs whose body has
   `Automation: docs` and the same `Closes-Work-Item` (or clearly closes the same
   merged ID). Also search recent merged Docs PRs for that ID. If a sibling
   already exists → do **not** open another PR; keep the lease on the survivor,
   report the existing URL, run A.6 for any extras, and stop.
5. Skim the merge commit / PR body for behavior or API changes.
6. **Orphan sweep:** if other open Docs PRs (draft or ready) share the same
   `Closes-Work-Item` / merged ID and a survivor is already open or merged,
   **close** the extras with a short “superseded by #<n>” comment. Never leave
   them open. Never convert them to draft.

## Step B — Roadmap status

Update `docs/roadmap/roadmap.md` and/or `docs/roadmap/backlog.md`:

- Set the item status in the `Status` column (keep existing table style / short notes)
- For W11–W17, update `docs/roadmap/horizonte-ambicioso.md` and its mirrored
  summary in `backlog.md`; `horizonte-ambicioso.md` is canonical. Change a
  range-level backlog summary only when the whole range/wave becomes terminal
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
- Do not introduce a new architecture decision in this close-out PR. If the
  merged implementation required an ADR and omitted it, reconstruct the
  technical decision from evidence, add an `Accepted` ADR under the authority
  of `docs/agents/autonomia.md`, and flag the process failure; human review is
  required only if the missing choice is genuinely R4
- Prefer one small PR; if nothing is stale beyond the Done marker, a tiny
  docs-only PR with just the roadmap update is enough

## Step D — Ship

1. Stay on the Cloud Agent **designated branch** for this run (do not create a
   separate `cursor/docs-…` branch — `open_git_pr` only accepts the designated one).
2. Commit; push; call **`open_git_pr`** to `main` with:

```text
Work-Item: DOCS-<merged ID>
Wave: docs
Trilha: G
Deps satisfeitas: <merged ID>
Automation: docs
Previous: <merged PR URL>
Closes-Work-Item: <merged ID>
Risk: R0
Lease: <original lease/run-id>
```

3. **PR must be ready for review — never draft:**
   - Do **not** ask for a draft PR.
   - Do **not** run `gh pr ready --undo` or convert to draft — **ever**, including
     when you discover a duplicate. Draft blocks QA and leaves orphans that go
     `CONFLICTING` after the survivor merges (see #77).
   - If `open_git_pr` creates a draft, immediately run `gh pr ready <number>`
     and confirm `isDraft: false` before finishing.
   - **If you discover a sibling Docs PR after opening yours:** leave yours ready
     only long enough to comment “duplicate of #<survivor>”, then **close** the
     newer/extra PR. Prefer closing yourself over converting to draft. Do not
     rebase a superseded PR.
4. Immediately before finishing, re-check open Docs PRs for the same
   `Closes-Work-Item` (A.4 / A.6). Close extras; confirm your PR is ready or closed.
5. Update Memories: last merged work item + feature/docs PR URLs + outstanding
   doc nits. Keep `ACTIVE:<ID>` until this Docs PR itself reaches `main`.

## Stop conditions

- If the Done marker and docs are already correct on `main`: report “docs idle”,
  clear the lease, run orphan sweep A.6, and **do not** open an empty PR.
- Two feature PRs for the same ID merged close together (e.g. #72+#73) may each
  trigger Docs — only **one** close PR may survive; the rest must be closed, not
  drafted.
- Never start the next roadmap implementation here.
