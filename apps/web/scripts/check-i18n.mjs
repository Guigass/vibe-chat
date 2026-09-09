#!/usr/bin/env node
/**
 * B-100: fail CI when a locale catalog is missing an extracted $localize id.
 */
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const localeDir = join(root, 'public/locale');
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
  const extractedIds = new Set(Object.keys(extracted.translations ?? extracted));
  const catalogs = readdirSync(localeDir)
    .filter((name) => /^messages\.[^.]+\.json$/.test(name))
    .sort();

  if (!catalogs.length) {
    console.error('B-100: no locale catalogs found in public/locale.');
    process.exit(1);
  }

  let failed = false;
  for (const file of catalogs) {
    const catalog = JSON.parse(readFileSync(join(localeDir, file), 'utf8'));
    const ids = new Set(Object.keys(catalog.translations ?? {}));
    const missing = [...extractedIds].filter((id) => !ids.has(id)).sort();
    if (missing.length) {
      failed = true;
      console.error(`B-100: ${file} is missing translations:`);
      for (const id of missing) {
        console.error(`  - ${id}`);
      }
    }

    const empty = [...ids].filter((id) => {
      const value = catalog.translations[id];
      return typeof value !== 'string' || value.trim() === '' || value === id;
    });
    if (empty.length) {
      failed = true;
      console.error(`B-100: ${file} has empty or raw-key values:`);
      for (const id of empty) {
        console.error(`  - ${id}`);
      }
    }
  }

  if (failed) {
    process.exit(1);
  }

  console.log(`B-100 i18n OK — ${extractedIds.size} ids, ${catalogs.length} catalogs complete.`);
} finally {
  rmSync(outDir, { recursive: true, force: true });
}
