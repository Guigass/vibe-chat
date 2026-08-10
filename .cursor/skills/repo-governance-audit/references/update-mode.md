# Update mode

Incremental refresh of a repository that already has documentation or agentic governance (produced by this kit or not). The goal is to bring existing artifacts back in sync with the code — not to re-run a full audit and not to create a new governance layer.

Update mode maps to the kit's `RE_AUDIT` diagnosis mode (Part 1), followed by delta-scoped planning (Parts 2–3 rules) and approved implementation (Part 4). Every gate and safety rule from `SKILL.md` applies unchanged.

## When to refuse update mode

If the drift delta ends up covering most existing artifacts, or no trustworthy baseline exists (artifacts too stale to diff against), stop and recommend `full-audit` instead.

## Step 1 — Baseline inventory (read-only)

- Locate existing governance: `AGENTS.md`, project docs and indexes, rules, skills (`.agents/skills/` and environment wrappers), agents, routers, and environment adapters.
- Locate previous audit outputs (diagnosis, plans, final report) when they exist in the repo.
- Record per artifact: path, role, last meaningful change (read-only git history when available), and the code area it covers.

## Step 2 — Drift delta (read-only)

Run Part 1 with `RE_AUDIT` enabled, scoped to drift detection. Default depth `LEAN` unless the user asks for more.

Compare the current code state against each baseline artifact and produce a `DRIFT_DELTA`:

- new modules, flows, or dependencies with no documentation/governance coverage;
- documented behavior that no longer exists or has changed;
- contradictions between artifacts and code, or between artifacts;
- artifacts that remain accurate (candidates for `preserve`).

Separate observed facts, evidence-based inferences, items not found, and human validation points.

**Stop** and present the delta. No writes.

## Step 3 — Update plan (read-only)

Using Parts 2 and 3 as the planning rules, plan only the delta:

- one row per artifact with an action from [governance-cleanup.md](governance-cleanup.md): `preserve` | `update` | `unify` / `consolidate` | `refactor` | `remove` | `do not create`;
- new artifacts only when the delta proves an uncovered P0/P1 risk — justify each one;
- keep the approved target environment recorded in Phase A; mark others `not applicable` unless the user expands.

Consolidate into an exact path+action allowlist (`UPDATE_PLAN`). **Stop for explicit human approval.** A plan is not authorization.

## Step 4 — Approved implementation

Only after unambiguous approval, run Part 4 restricted to the approved allowlist: write only approved files, validate, review the diff, and report what was updated, what was preserved, executed validations, and remaining human validation points.
