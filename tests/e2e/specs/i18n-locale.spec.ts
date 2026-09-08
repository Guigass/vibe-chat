import { expect, test, type Page } from '@playwright/test';
import { AUTH_MODE, resetProfileLocale } from '../helpers/auth';

async function waitForDocumentLang(page: Page, lang: 'en' | 'pt-BR'): Promise<void> {
  await page.waitForFunction((expected) => document.documentElement.lang === expected, lang, {
    timeout: 20_000,
  });
}

/**
 * B-100: login, shell search and settings follow the selected locale.
 * Does not PUT `en` onto Alice before login — that leaked into later specs via
 * syncFromProfile (OPS-E2E-B100). The header control is the persist path.
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

    await Promise.all([
      waitForDocumentLang(page, 'en'),
      page.getByTestId('locale-select').selectOption('en'),
    ]);
    await expect(page.getByRole('heading', { name: /Conversations with depth/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /Sign in with Keycloak/i })).toBeVisible();

    if (AUTH_MODE === 'demo') {
      await page.getByRole('button', { name: /Explore the UI offline/i }).click();
    } else {
      await page.getByRole('button', { name: /^Alice$/i }).click();
    }
    await page.waitForURL(/\/app/);

    const headerSelect = page.locator('.shell__actions [data-testid="locale-select"]');
    await expect(headerSelect).toBeVisible();
    if ((await headerSelect.inputValue()) !== 'en') {
      await Promise.all([
        waitForDocumentLang(page, 'en'),
        headerSelect.selectOption('en'),
      ]);
    }
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('.shell__actions [data-testid="locale-select"]')).toHaveValue('en');
    await expect(page.getByLabel(/Search messages/i)).toBeVisible();

    await page.getByRole('button', { name: /Context panel/i }).click();
    await expect(page.getByRole('heading', { name: /Settings/i })).toBeVisible();
    await expect(page.locator('.shell__context').getByTestId('locale-select')).toHaveValue('en');
  });
});
