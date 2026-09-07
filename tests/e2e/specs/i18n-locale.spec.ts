import { expect, test } from '@playwright/test';
import { API_BASE_URL, AUTH_MODE } from '../helpers/auth';

/**
 * B-100: login, shell search and settings follow the selected locale.
 */
test.describe(`i18n locale (${AUTH_MODE})`, () => {
  test('switches English across login, shell and settings', async ({ page }) => {
    test.skip(AUTH_MODE === 'oidc', 'DevAuth login chrome is the locale fixture');

    await page.goto('/login');
    await expect(page.getByRole('heading', { name: /Conversas com profundidade/i })).toBeVisible();

    await page.getByTestId('locale-select').selectOption('en');
    await expect(page.getByRole('heading', { name: /Conversations with depth/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Sign in with Keycloak/i })).toBeVisible();

    // Earlier specs persist pt-BR on Alice via syncFromProfile; align server before shell load.
    if (AUTH_MODE === 'devauth') {
      await page.request.put(`${API_BASE_URL}/api/v1/me`, {
        headers: { 'X-Dev-User': 'alice' },
        data: { locale: 'en' },
      });
    }

    if (AUTH_MODE === 'demo') {
      await page.getByRole('button', { name: /Explore the UI offline/i }).click();
    } else {
      await page.getByRole('button', { name: /^Alice$/i }).click();
    }
    await page.waitForURL(/\/app/);
    await expect(page.getByLabel(/Search messages/i)).toBeVisible();

    await page.getByRole('button', { name: /Context panel/i }).click();
    await expect(page.getByRole('heading', { name: /Settings/i })).toBeVisible();
    await expect(page.getByTestId('locale-select').last()).toHaveValue('en');
  });
});
