import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const automationFiles = [
  "00-readiness.prompt.md",
  "01-build.prompt.md",
  "02-qa-merge.prompt.md",
  "03-docs.prompt.md",
  "04-ux-review.prompt.md",
  "05-security-review.prompt.md",
  "06-watchdog-recovery.prompt.md",
  "07-harness-retrospective.prompt.md",
  "08-pr-repair.prompt.md"
];

function findRepositoryRoot(start) {
  let current = path.resolve(start);
  while (true) {
    if (fs.existsSync(path.join(current, ".cursor", "hooks.json")) &&
        fs.existsSync(path.join(current, "AGENTS.md"))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error("Could not locate the VibeChat repository root.");
    }
    current = parent;
  }
}

function readText(root, relativePath, errors) {
  const absolutePath = path.join(root, relativePath);
  if (!fs.existsSync(absolutePath)) {
    errors.push(`${relativePath}: missing file`);
    return "";
  }
  const text = fs.readFileSync(absolutePath, "utf8");
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

export function validateHarness(start = process.cwd()) {
  const errors = [];
  const root = findRepositoryRoot(start);

  const hooksText = readText(root, ".cursor/hooks.json", errors);
  let hooks;
  try {
    hooks = JSON.parse(hooksText);
  } catch (error) {
    errors.push(`.cursor/hooks.json: invalid JSON (${error.message})`);
  }

  const expectedHooks = {
    beforeShellExecution: "node .cursor/hooks/guard-shell.mjs",
    stop: "node .cursor/hooks/stop-check.mjs"
  };
  for (const [event, command] of Object.entries(expectedHooks)) {
    const commands = hooks?.hooks?.[event]?.map((item) => item.command) ?? [];
    if (!commands.includes(command)) {
      errors.push(`.cursor/hooks.json: ${event} must call "${command}"`);
    }
  }

  const environmentText = readText(root, ".cursor/environment.json", errors);
  let environment;
  try {
    environment = JSON.parse(environmentText);
  } catch (error) {
    errors.push(`.cursor/environment.json: invalid JSON (${error.message})`);
  }

  const setupCommand = "bash infra/scripts/agent-setup.sh";
  if (environment?.install !== setupCommand) {
    errors.push(`.cursor/environment.json: install must be "${setupCommand}"`);
  }
  if (environment?.start !== setupCommand) {
    errors.push(`.cursor/environment.json: start must restart Docker with "${setupCommand}"`);
  }
  if (!Array.isArray(environment?.terminals)) {
    errors.push(".cursor/environment.json: terminals must be an array");
  }

  const readme = readText(root, ".cursor/automations/README.md", errors);
  for (const automationFile of automationFiles) {
    const relativePath = `.cursor/automations/${automationFile}`;
    const prompt = readText(root, relativePath, errors);
    if (!prompt) {
      continue;
    }
    if (!prompt.includes("docs/agents/loop-engineering.md")) {
      errors.push(`${relativePath}: missing loop-engineering contract`);
    }
    if (!prompt.includes("RUN_RESULT")) {
      errors.push(`${relativePath}: missing RUN_RESULT output`);
    }
    if (!prompt.includes("Stop reason:")) {
      errors.push(`${relativePath}: missing canonical Stop reason field`);
    }
    if (!readme.includes(`\`${automationFile}\``)) {
      errors.push(`.cursor/automations/README.md: does not list ${automationFile}`);
    }
  }

  for (const relativePath of [
    "docs/agents/loop-engineering.md",
    "docs/agents/go-live-2026-08-05.md",
    ".cursor/hooks/guard-shell.mjs",
    ".cursor/hooks/stop-check.mjs",
    ".cursor/hooks/guard-shell.test.mjs"
  ]) {
    readText(root, relativePath, errors);
  }

  const agents = readText(root, "AGENTS.md", errors);
  if (!agents.includes("docs/agents/loop-engineering.md")) {
    errors.push("AGENTS.md: missing loop-engineering entrypoint");
  }

  return {
    root,
    errors,
    automationCount: automationFiles.length,
    hookCount: Object.keys(expectedHooks).length
  };
}

function main() {
  try {
    const result = validateHarness();
    if (result.errors.length > 0) {
      process.stderr.write("HARNESS-CHECK FAIL\n");
      for (const error of result.errors) {
        process.stderr.write(`- ${error}\n`);
      }
      process.exitCode = 1;
      return;
    }

    process.stdout.write(
      `HARNESS-CHECK PASS automations=${result.automationCount} hooks=${result.hookCount}\n`
    );
  } catch (error) {
    process.stderr.write(`HARNESS-CHECK FAIL\n- ${error.message}\n`);
    process.exitCode = 1;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
