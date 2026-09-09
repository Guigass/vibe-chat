import { expect, test } from '@playwright/test';
import { AUTH_MODE, resetProfileLocale } from '../helpers/auth';

/**
 * B-100: login, shell search and settings follow the selected locale.
 * Profile locale wins via syncFromProfile. Align Alice to `en` before the shell.
 */
test.describe(`i18n locale (${AUTH_MODE})`, () => {
  test.afterEach(async ({ page }) => {
    if (AUTH_MODE !== 'devauth') return;
    await resetProfileLocale(page.request, 'alice', 'pt-BR');
  });

  test('switches English across login, shell and settings', async ({ page }) => {
    test.skip(AUTH_MODE === 'oidc', 'DevAuth login chrome is the locale fixture');

    await page.goto('/login');
    await expect(page.getByRole('heading', { name: /Conversas com profundidade/i })).toBeVisible();

    await page.getByTestId('locale-select').selectOption('en');
    await expect(page.getByRole('heading', { name: /Conversations with depth/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Sign in with Keycloak/i })).toBeVisible();

    if (AUTH_MODE === 'devauth') {
      await resetProfileLocale(page.request, 'alice', 'en');
    }

    if (AUTH_MODE === 'demo') {
      await page.getByRole('button', { name: /Explore the UI offline/i }).click();
    } else {
      await page.getByRole('button', { name: /^Alice$/i }).click();
    }
    await page.waitForURL(/\/app/);
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByLabel(/Search messages/i)).toBeVisible();

    await page.getByRole('button', { name: /Settings|Configurações/i }).click();
    await expect(page.getByRole('heading', { name: /Settings/i })).toBeVisible();
    await expect(page.locator('.shell__context').getByTestId('locale-select')).toHaveValue('en');
  });
});
