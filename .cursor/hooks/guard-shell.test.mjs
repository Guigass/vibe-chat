import test from "node:test";
import assert from "node:assert/strict";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { evaluateCommand } from "./guard-shell.mjs";

const hooksDirectory = path.dirname(fileURLToPath(import.meta.url));

const denied = [
  "git push --force origin feature",
  "git reset --hard HEAD~1",
  "git clean -fdx",
  "git checkout -- apps/web",
  "git restore --worktree .",
  "git branch -D old-work",
  "git push origin main",
  "docker compose down -v",
  "docker volume prune",
  "rm -rf build",
  "Remove-Item build -Recurse -Force",
  "psql -c 'DROP SCHEMA public CASCADE'"
];

const allowed = [
  "git push origin cursor/vibechat-task",
  "git reset --soft HEAD~1",
  "git clean -n",
  "docker compose stop",
  "Remove-Item file.tmp",
  "dotnet ef database update",
  "task test"
];

test("blocks destructive autonomous shell commands", () => {
  for (const command of denied) {
    assert.equal(evaluateCommand(command).allowed, false, command);
  }
});

test("allows bounded development commands", () => {
  for (const command of allowed) {
    assert.equal(evaluateCommand(command).allowed, true, command);
  }
});

test("fails closed when command is missing", () => {
  assert.equal(evaluateCommand("").allowed, false);
  assert.equal(evaluateCommand(undefined).allowed, false);
});

test("beforeShellExecution CLI returns valid deny JSON", () => {
  const result = spawnSync(
    process.execPath,
    [path.join(hooksDirectory, "guard-shell.mjs")],
    {
      input: JSON.stringify({ command: "git push origin main" }),
      encoding: "utf8"
    }
  );

  assert.equal(result.status, 0, result.stderr);
  const response = JSON.parse(result.stdout);
  assert.equal(response.permission, "deny");
  assert.match(response.agent_message, /GIT_PUSH_MAIN/);
});

test("beforeShellExecution accepts UTF-8 BOM on stdin", () => {
  const result = spawnSync(
    process.execPath,
    [path.join(hooksDirectory, "guard-shell.mjs")],
    {
      input: `\uFEFF${JSON.stringify({ command: "task agent:check" })}`,
      encoding: "utf8"
    }
  );

  assert.equal(result.status, 0, result.stderr);
  const response = JSON.parse(result.stdout);
  assert.equal(response.permission, "allow");
});

test("stop hook is silent when the harness is valid", () => {
  const result = spawnSync(
    process.execPath,
    [path.join(hooksDirectory, "stop-check.mjs")],
    {
      input: JSON.stringify({
        conversation_id: "harness-test",
        generation_id: "valid"
      }),
      encoding: "utf8",
      cwd: path.resolve(hooksDirectory, "..", "..")
    }
  );

  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), {});
});

test("stop hook accepts UTF-8 BOM on stdin", () => {
  const result = spawnSync(
    process.execPath,
    [path.join(hooksDirectory, "stop-check.mjs")],
    {
      input: `\uFEFF${JSON.stringify({
        conversation_id: "harness-test",
        generation_id: "bom"
      })}`,
      encoding: "utf8",
      cwd: path.resolve(hooksDirectory, "..", "..")
    }
  );

  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), {});
});
