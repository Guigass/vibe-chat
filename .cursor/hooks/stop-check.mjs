import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { createHash } from "node:crypto";
import { validateHarness } from "./validate-harness.mjs";

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

function statePath(payload) {
  const key = [
    payload.conversation_id ?? "unknown-conversation",
    payload.generation_id ?? payload.raw_digest ?? "unknown-generation"
  ].join(":");
  const digest = createHash("sha256").update(key).digest("hex").slice(0, 20);
  return path.join(os.tmpdir(), `vibechat-harness-stop-${digest}.json`);
}

async function main() {
  let raw = "";
  let payload = {};
  const errors = [];

  try {
    raw = await readStdin();
    payload = raw.trim() ? JSON.parse(raw) : {};
  } catch (error) {
    errors.push(`invalid hook input: ${error instanceof Error ? error.message : String(error)}`);
    payload = {
      raw_digest: createHash("sha256").update(raw).digest("hex").slice(0, 20)
    };
  }

  try {
    const result = validateHarness();
    errors.push(...result.errors);
  } catch (error) {
    errors.push(`harness validator failed: ${error instanceof Error ? error.message : String(error)}`);
  }

  const state = statePath(payload);
  if (errors.length === 0) {
    if (fs.existsSync(state)) {
      fs.rmSync(state, { force: true });
    }
    process.stdout.write("{}");
    return;
  }

  let followups = 0;
  if (fs.existsSync(state)) {
    try {
      followups = JSON.parse(fs.readFileSync(state, "utf8")).followups ?? 0;
    } catch {
      followups = 1;
    }
  }

  if (followups >= 1) {
    process.stdout.write("{}");
    return;
  }

  try {
    fs.writeFileSync(state, JSON.stringify({ followups: 1 }), "utf8");
    const summary = errors.slice(0, 5).join("; ");
    process.stdout.write(JSON.stringify({
      followup_message:
        `O checker determinístico do harness falhou: ${summary}. ` +
        "Execute `task agent:check`, corrija apenas o drift relacionado e tente encerrar novamente. " +
        "Se não houver progresso, termine com TOOLING_BLOCKED; este hook não repetirá o follow-up."
    }));
  } catch (error) {
    process.stdout.write(JSON.stringify({
      followup_message:
        `O checker do harness falhou fechado e não conseguiu persistir seu circuit breaker: ` +
        `${error instanceof Error ? error.message : String(error)}. ` +
        "Encerre com TOOLING_BLOCKED; não repita automaticamente."
    }));
  }
}

await main();
