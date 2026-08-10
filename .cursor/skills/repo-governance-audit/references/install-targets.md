# Install targets

Install **one** copy of this skill per run. Do not duplicate the same skill under multiple roots.

| User choice | Install path in the target repository |
| --- | --- |
| `cursor` | `.cursor/skills/repo-governance-audit/` |
| `claude` | `.claude/skills/repo-governance-audit/` |
| `codex` | `.agents/skills/repo-governance-audit/` |
| `generic` | `.agents/skills/repo-governance-audit/` |

`codex` and `generic` share the portable path. Still record the chosen label (`codex` vs `generic`) for Part 3 environment targeting.

## What to write

Copy or recreate the full skill directory:

- `SKILL.md`
- `references/install-targets.md`
- `references/execution-modes.md`
- `references/governance-cleanup.md`
- `references/update-mode.md`

Source of truth (in order):

1. The skill directory currently loaded / being executed (self-copy).
2. Local kit clone: `.agents/skills/repo-governance-audit/`.
3. Pinned raw URLs under `https://raw.githubusercontent.com/Guigass/agentic-repo-governance-kit/v1.4.1/.agents/skills/repo-governance-audit/`.

## Overwrite rules

- Before asking where to install, check every known root in the target: an existing install under any of them counts as found.
- If the destination does not exist: create it and write the files.
- If it already exists with the same skill name and the content matches the source: skip writing and record it as already current.
- If it already exists with the same skill name but outdated content: update the skill files to match the source (overwrite skill content only).
- If unsure the existing directory is this skill: ask before overwriting unrelated content.
- Never write outside the chosen skill directory during Phase A.
