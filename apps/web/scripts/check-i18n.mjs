#!/usr/bin/env node
/**
 * B-100: fail CI when the English catalog is missing an extracted $localize id.
 */
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const enPath = join(root, 'public/locale/messages.en.json');
const outDir = mkdtempSync(join(tmpdir(), 'vc-i18n-'));

try {
  execFileSync(
    'npx',
    [
      'ng',
      'extract-i18n',
      '--format',
      'json',
      '--output-path',
      outDir,
      '--out-file',
      'messages.extracted.json',
    ],
    { cwd: root, stdio: 'inherit' },
  );

  const extracted = JSON.parse(readFileSync(join(outDir, 'messages.extracted.json'), 'utf8'));
  const en = JSON.parse(readFileSync(enPath, 'utf8'));
  const extractedIds = new Set(Object.keys(extracted.translations ?? extracted));
  const enIds = new Set(Object.keys(en.translations ?? {}));

  const missing = [...extractedIds].filter((id) => !enIds.has(id)).sort();
  if (missing.length) {
    console.error('B-100: English catalog is missing translations:');
    for (const id of missing) {
      console.error(`  - ${id}`);
    }
    process.exit(1);
  }

  const empty = [...enIds].filter((id) => {
    const value = en.translations[id];
    return typeof value !== 'string' || value.trim() === '' || value === id;
  });
  if (empty.length) {
    console.error('B-100: English catalog has empty or raw-key values:');
    for (const id of empty) {
      console.error(`  - ${id}`);
    }
    process.exit(1);
  }

  console.log(`B-100 i18n OK — ${extractedIds.size} ids, en catalog complete.`);
} finally {
  rmSync(outDir, { recursive: true, force: true });
}
