import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const layoutDir = dirname(fileURLToPath(import.meta.url));
const scss = readFileSync(join(layoutDir, 'shell.page.scss'), 'utf8');
const html = readFileSync(join(layoutDir, 'shell.page.html'), 'utf8');

describe('shell header title (OPS-E2E-B098)', () => {
  it('keeps a non-zero min-width so #geral stays visible next to search', () => {
    const blocks = [...scss.matchAll(/\.shell__title\s*\{([^}]+)\}/g)].map((match) => match[1]);
    expect(blocks.length).toBeGreaterThan(0);
    expect(blocks[0]).toMatch(/min-width:\s*6\.5rem/);
    expect(blocks[0]).not.toMatch(/min-width:\s*0/);
  });

  it('keeps DM add out of the title so the header matches channel pages', () => {
    const titleStart = html.indexOf('class="shell__title"');
    const titleEnd = html.indexOf('class="shell__search"');
    const titleBlock = html.slice(titleStart, titleEnd);
    const actionsStart = html.indexOf('class="shell__actions"');
    const actionsBlock = html.slice(actionsStart);
    expect(titleBlock).not.toContain('group-dm-add-toggle');
    expect(titleBlock).not.toContain('shell__group-actions');
    expect(actionsBlock).toContain('group-dm-add-toggle');
    expect(actionsBlock).toContain('shell__dm-popover');
  });

  it('filters addable people and confirms leave without native dialogs', () => {
    const ts = readFileSync(join(layoutDir, 'shell.page.ts'), 'utf8');
    expect(ts).not.toContain('window.prompt');
    expect(ts).not.toContain('window.confirm');
    expect(html).toContain('vc-group-dm-add-search');
    expect(html).toContain('group-dm-rename-form');
    expect(html).toContain('group-dm-leave-confirm');
  });

  it('lets the search field shrink instead of crushing the title', () => {
    const block = scss.match(/\.shell__search\s*\{([^}]+)\}/)?.[1] ?? '';
    expect(block).toMatch(/flex:\s*0 1/);
    expect(block).toMatch(/min-width:\s*10rem/);
  });
});
