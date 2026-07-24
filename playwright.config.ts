import { defineConfig, devices } from '@playwright/test';

/**
 * Root Playwright config — same settings as tests/e2e/playwright.config.ts.
 * Prefer: `npm test --prefix tests/e2e` or `task test:e2e`.
 */
const baseURL = process.env.WEB_BASE_URL ?? 'http://localhost:4200';

export default defineConfig({
  testDir: './tests/e2e/specs',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'tests/e2e/playwright-report' }]],
  timeout: 90_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
});
