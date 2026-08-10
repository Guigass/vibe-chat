# Governance cleanup

When the target already has documentation or agentic governance, Parts 1–3 must plan cleanup — not only new artifacts. Execution happens only in Part 4 with an explicit path+action allowlist.

## Scope

- Project docs and indexes
- Portable instructions (`AGENTS.md` and equivalents)
- Rules (portable or environment-specific)
- Skills (`.agents/skills/` and environment wrappers)
- Agents and routers
- Environment adapters that only mirror portable content

## Goals

1. Unify what can share one canonical source.
2. Remove redundancy across docs, rules, skills, agents, and environments.
3. Drop obsolete or unused artifacts with proven lack of value.
4. Refactor structure only when overlap, contradiction, or unjustified context cost is evidenced.

## Plan actions

Use these actions in documentation and governance plans:

| Action | Meaning |
| --- | --- |
| `preserve` | Keep as-is; still useful and current |
| `update` | Fix content without changing role |
| `unify` / `consolidate` | Merge duplicates into one canonical path; retire or thin the rest |
| `refactor` | Restructure ownership, activation, or layering without inventing new scope |
| `remove` | Delete obsolete artifact (requires specific path+action approval) |
| `do not create` | Rejected candidate; record why |

## Removal criteria (need evidence)

Propose `remove` only when at least one holds:

- Stale content with no maintainer and no current code match
- Duplicate of a chosen canonical source
- Vague always-on rule with no identifiable scope
- Skill or agent with no proven recurring use
- Environment adapter that only copies portable content

## Do not

- Clean up for aesthetics alone
- Erase useful historical context without a replacement pointer
- Remove or rewrite outside the approved allowlist
- Change application source, dependencies, CI, or deploy as “cleanup”

## Required outputs

When existing governance is found, the consolidated plan must include:

1. Unifications (sources merged and resulting canonical path)
2. Proposed removals (path + reason)
3. Refactors (what changes and why)
4. What remains preserved
