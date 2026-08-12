import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const webRoot = join(dirname(fileURLToPath(import.meta.url)), '../../../..');

describe('web nginx cache headers (B-165)', () => {
  const conf = readFileSync(join(webRoot, 'nginx.conf'), 'utf8');

  it('disables long cache for shell, SW manifest and version.json', () => {
    for (const path of ['/index.html', '/ngsw.json', '/version.json']) {
      expect(conf).toContain(`location = ${path}`);
    }
    expect(conf).toMatch(
      /location = \/index\.html[\s\S]*?Cache-Control "no-cache, no-store, must-revalidate"/,
    );
    expect(conf).toMatch(
      /location = \/ngsw\.json[\s\S]*?Cache-Control "no-cache, no-store, must-revalidate"/,
    );
    expect(conf).toMatch(
      /location = \/version\.json[\s\S]*?Cache-Control "no-cache, no-store, must-revalidate"/,
    );
  });

  it('keeps immutable cache for content-hashed assets', () => {
    expect(conf).toMatch(/max-age=31536000, immutable/);
    expect(conf).toContain('location ~* \\.(js|css|mjs)$ {');
  });

  it('lets Keycloak provide the CSP for its own consoles', () => {
    const keycloakLocation = conf.match(/location \^~ \/auth\/ \{[\s\S]*?\n    \}/)?.[0];

    expect(keycloakLocation).toBeDefined();
    expect(keycloakLocation).toContain('location ^~ /auth/');
    expect(keycloakLocation).toContain('add_header X-Content-Type-Options');
    expect(keycloakLocation).not.toContain('security-headers.conf');
    expect(keycloakLocation).not.toContain('Content-Security-Policy');
  });
});
