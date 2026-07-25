import { defineConfig, devices } from '@playwright/test';

/**
 * VibeChat E2E
 *
 * Auth modes (E2E_AUTH_MODE):
 * - demo (default): UI "Explorar demo local" — good for smoke without Keycloak/API
 * - devauth: inject X-Dev-User on API/hub requests (API must run in Development)
 * - oidc: real Keycloak login (alice/bob + Demo123!)
 *
 * Env:
 * - WEB_BASE_URL (default http://localhost:4200)
 * - API_BASE_URL (default http://localhost:5080)
 * - E2E_AUTH_MODE
 */
const baseURL = process.env.WEB_BASE_URL ?? 'http://localhost:4200';

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  timeout: 90_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
  // CI (W7-1): API + Web are started by infra/scripts/ci-e2e.sh before Playwright.
});
