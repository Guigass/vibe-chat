import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const layoutDir = dirname(fileURLToPath(import.meta.url));
const scss = readFileSync(join(layoutDir, 'shell.page.scss'), 'utf8');

describe('shell header title (OPS-E2E-B098)', () => {
  it('keeps a non-zero min-width so #geral stays visible next to search', () => {
    const blocks = [...scss.matchAll(/\.shell__title\s*\{([^}]+)\}/g)].map((match) => match[1]);
    expect(blocks.length).toBeGreaterThan(0);
    expect(blocks[0]).toMatch(/min-width:\s*6\.5rem/);
    expect(blocks[0]).not.toMatch(/min-width:\s*0/);
  });

  it('lets the search field shrink instead of crushing the title', () => {
    const block = scss.match(/\.shell__search\s*\{([^}]+)\}/)?.[1] ?? '';
    expect(block).toMatch(/flex:\s*0 1/);
    expect(block).toMatch(/min-width:\s*10rem/);
  });
});
