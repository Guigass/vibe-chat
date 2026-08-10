import { fileURLToPath } from "node:url";
import path from "node:path";

const rules = [
  {
    id: "GIT_FORCE_PUSH",
    pattern: /\bgit\s+push\b[^\r\n]*(?:--force(?:-with-lease)?|-f(?:\s|$))/i,
    message: "Force push is forbidden for autonomous runs."
  },
  {
    id: "GIT_RESET_HARD",
    pattern: /\bgit\s+reset\b[^\r\n]*--hard\b/i,
    message: "git reset --hard can discard user work."
  },
  {
    id: "GIT_CLEAN_FORCE",
    pattern: /\bgit\s+clean\b[^\r\n]*-(?:[a-z]*f[a-z]*|[a-z]*x[a-z]*)\b/i,
    message: "Forced git clean can remove untracked user files."
  },
  {
    id: "GIT_DISCARD_PATH",
    pattern: /\bgit\s+(?:checkout|restore)\b[^\r\n]*(?:--\s+|--worktree\b)/i,
    message: "Discarding paths requires explicit human authorization."
  },
  {
    id: "GIT_DELETE_BRANCH",
    pattern: /\bgit\s+branch\b[^\r\n]*-D(?:\s|$)/i,
    message: "Force-deleting a branch can remove recovery evidence."
  },
  {
    id: "GIT_PUSH_MAIN",
    pattern: /\bgit\s+push\b[^\r\n]*\s(?:origin|upstream)\s+(?:HEAD:)?(?:refs\/heads\/)?main(?:\s|$)/i,
    message: "Push to main is forbidden; use a PR from the designated branch."
  },
  {
    id: "DOCKER_DELETE_VOLUMES",
    pattern: /\bdocker\s+compose\b[^\r\n]*\bdown\b[^\r\n]*(?:-v\b|--volumes\b)|\bdocker\s+volume\s+(?:rm|prune)\b/i,
    message: "Deleting Docker volumes can destroy local data."
  },
  {
    id: "BROAD_RM_RF",
    pattern: /(?:^|[;&|]\s*)rm\s+[^\r\n]*(?:-[a-z]*r[a-z]*f|-+[a-z]*f[a-z]*r)\b/i,
    message: "rm -rf is blocked; use a bounded, reviewable cleanup command."
  },
  {
    id: "POWERSHELL_RECURSIVE_FORCE",
    pattern: /\bRemove-Item\b[^\r\n]*(?=.*-(?:Recurse|r)\b)(?=.*-(?:Force|fo)\b)/i,
    message: "Recursive forced deletion is blocked for autonomous runs."
  },
  {
    id: "DIRECT_SQL_DESTRUCTIVE",
    pattern: /\bpsql\b[^\r\n]*(?:DROP\s+(?:DATABASE|SCHEMA|TABLE)|TRUNCATE\s+)/i,
    message: "Direct destructive SQL is forbidden; use a reviewed migration."
  }
];

export function evaluateCommand(command) {
  const normalized = typeof command === "string" ? command.trim() : "";
  if (!normalized) {
    return {
      allowed: false,
      ruleId: "INVALID_INPUT",
      message: "Hook input did not contain a shell command."
    };
  }

  for (const rule of rules) {
    if (rule.pattern.test(normalized)) {
      return {
        allowed: false,
        ruleId: rule.id,
        message: rule.message
      };
    }
  }

  return { allowed: true };
}

function stripBom(text) {
  return typeof text === "string" && text.charCodeAt(0) === 0xfeff
    ? text.slice(1)
    : text;
}

async function readStdin() {
  let input = "";
  for await (const chunk of process.stdin) {
    input += chunk;
  }
  return stripBom(input);
}

async function main() {
  try {
    const raw = await readStdin();
    const payload = JSON.parse(raw.trim() || "{}");
    const verdict = evaluateCommand(payload.command);

    if (verdict.allowed) {
      process.stdout.write(JSON.stringify({
        continue: true,
        permission: "allow"
      }));
      return;
    }

    const explanation = `[${verdict.ruleId}] ${verdict.message}`;
    process.stdout.write(JSON.stringify({
      continue: true,
      permission: "deny",
      user_message: explanation,
      agent_message: `${explanation} Escolha uma alternativa não destrutiva ou pare com SAFETY_GATE.`
    }));
  } catch (error) {
    const explanation = `Cursor hook failed closed: ${error instanceof Error ? error.message : String(error)}`;
    process.stdout.write(JSON.stringify({
      continue: true,
      permission: "deny",
      user_message: explanation,
      agent_message: "O hook não conseguiu validar o comando. Pare com TOOLING_BLOCKED."
    }));
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
