---
name: repo-governance-audit
description: >-
  Universal entry point of the Agentic Repository Governance Kit for the
  repository open in the workspace. Offers three modes: full-audit (discovery,
  documentation and governance planning, approved implementation — Parts 1-4),
  update (incremental refresh of existing docs, rules, skills, and agents via
  the kit's RE_AUDIT drift delta), and install-only (just install or refresh
  this skill in the target repo). Use when the user asks to audit a
  repository, update or refresh project documentation/governance, install the
  governance kit skill, clean up redundant governance, or run repo discovery
  and agentic governance planning.
---

# Repository governance audit

Entry point for the [Agentic Repository Governance Kit](https://github.com/Guigass/agentic-repo-governance-kit). The open workspace is the TARGET repository. The kit is only an instruction source — never audit or modify the kit as the target.

Pinned kit version for raw URLs: `v1.4.1`.

If the user already stated the mode, IDE, or execution mode in the invocation, accept it and do not ask again.

## Phase 0 — Mode (stop and ask)

Before asking, scan the target once (read-only, cheap): look for `AGENTS.md`, `docs/`, `.cursor/`, `.claude/`, `.agents/`, and previous audit reports. Then stop and ask ONE question — what to do:

| Mode | When | What runs |
| --- | --- | --- |
| `full-audit` | No governance yet, or starting over | Full kit, Parts 1–4, all gates |
| `update` | Governance already exists and the code moved on | Drift delta → update plan → approved implementation, per [references/update-mode.md](references/update-mode.md) |
| `install-only` | Only make this skill available in the repo | Phase A, then stop |

Recommend a default based on the scan: existing governance found → `update`; none → `full-audit`.

## Phase A — Install or refresh

1. Check whether this skill is already installed in the target, at any known root from [references/install-targets.md](references/install-targets.md):
   - Installed and current: skip writing; record the path.
   - Installed but outdated: refresh the skill files only (overwrite rules in the reference).
   - Not installed: stop and ask which IDE to install into (`cursor` | `claude` | `codex` | `generic`), then install.
2. Write authorization is limited to those skill files only. Do not change application code.
3. Record in the conversation: chosen IDE + install path (Part 3 uses this as the approved target environment; mark other environments `not applicable` unless the user expands later).
4. In `install-only` mode: stop here and report what was installed or refreshed.

## Phase B — Execution mode (full-audit only)

Stop and ask: `multi-agent` or `step-by-step`.

Read [references/execution-modes.md](references/execution-modes.md) and follow the chosen mode.

`update` mode always runs step-by-step in the main agent: its scope is a bounded delta, so a subagent chain is unnecessary.

## Phase C — Run the chosen mode

Defaults: depth `STANDARD` (`LEAN` is the default for the drift delta in `update` mode); delivery language = user's language.

Load kit parts from the local kit clone when available; otherwise use pinned raw URLs under:

- English: `https://raw.githubusercontent.com/Guigass/agentic-repo-governance-kit/v1.4.1/en/`
- Portuguese: `https://raw.githubusercontent.com/Guigass/agentic-repo-governance-kit/v1.4.1/pt-BR/`

Always read Part 0 first (`00-HOW-TO-USE.md` / `00-COMO-USAR.md`), then the part for the current stage.

- `full-audit`: run Parts 1–4. If Part 1 finds existing docs, `AGENTS.md`, rules, skills, agents, or environment adapters, also read [references/governance-cleanup.md](references/governance-cleanup.md) and apply it through Parts 1–3: unify what can be unified, cut redundancy, mark obsolete removals, and refactor only with evidence. Cleanup is planned in those parts; Part 4 executes only approved path+action items (including `remove` / `unify` / `refactor`).
- `update`: follow [references/update-mode.md](references/update-mode.md).

### Safety (non-negotiable)

- After Phase A install, every mode is read-only until explicit human approval of the consolidated plan.
- Part 4 runs only with an unambiguous allowlist of files and actions.
- No commit, push, PR, deploy, dependency install, or private external access without separate authorization.
- Separate facts, inferences, items not found, and human validation points.
- Preserve pre-existing, unrelated working-tree changes.
- If this skill was invoked from inside the kit repository itself, refuse to treat the kit as the audit target unless the user explicitly overrides.

### After mode, install, and execution mode are known

Proceed immediately with Phase C. Do not ask for the bootstrap paste from the README — this skill replaces that entrypoint.
