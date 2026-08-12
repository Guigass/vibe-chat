import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const webRoot = join(dirname(fileURLToPath(import.meta.url)), '../../../..');

describe('service worker reverse-proxy routing', () => {
  const config = JSON.parse(
    readFileSync(join(webRoot, 'ngsw-config.json'), 'utf8'),
  ) as { navigationUrls: string[] };

  it('does not replace same-origin backend routes with the Angular shell', () => {
    for (const path of [
      '/api/**',
      '/hubs/**',
      '/auth/**',
      '/realms/**',
      '/admin/master/**',
      '/files/**',
      '/grafana/**',
    ]) {
      expect(config.navigationUrls).toContain(`!${path}`);
    }
  });
});
