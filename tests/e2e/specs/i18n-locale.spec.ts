import { expect, test, type Page } from '@playwright/test';
import { AUTH_MODE, resetProfileLocale } from '../helpers/auth';

/**
 * B-100: login, shell search and settings follow the selected locale.
 * Profile locale wins via syncFromProfile (may reload). Align Alice to `en`
 * before the shell, then poll until html lang and the header select agree.
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

    const pendingMe = page.waitForResponse(
      (response) => {
        const url = new URL(response.url());
        return url.pathname === '/api/v1/me' && response.request().method() === 'GET';
      },
      { timeout: 15_000 },
    );

    if (AUTH_MODE === 'demo') {
      await page.getByRole('button', { name: /Explore the UI offline/i }).click();
    } else {
      await page.getByRole('button', { name: /^Alice$/i }).click();
    }
    await page.waitForURL(/\/app/);
    const me = await pendingMe.catch(() => null);
    if (me) {
      const body = (await me.json().catch(() => null)) as { locale?: string | null } | null;
      if (body?.locale && body.locale !== 'en') {
        await page.waitForFunction(
          (locale) => document.documentElement.lang === locale,
          body.locale,
        );
      }
    }

    await settleEnglishShell(page);
    await expect(page.getByLabel(/Search messages/i)).toBeVisible();

    await page.getByRole('button', { name: /Context panel/i }).click();
    await expect(page.getByRole('heading', { name: /Settings/i })).toBeVisible();
    await expect(page.locator('.shell__context').getByTestId('locale-select')).toHaveValue('en');
  });
});

async function settleEnglishShell(page: Page): Promise<void> {
  const headerSelect = page.locator('.shell__actions [data-testid="locale-select"]');
  await expect(headerSelect).toBeVisible();
  await expect
    .poll(
      async () => {
        const lang = await page.locator('html').getAttribute('lang');
        const value = await headerSelect.inputValue().catch(() => '');
        if (lang === 'en' && value === 'en') {
          return true;
        }
        if (value !== 'en') {
          await headerSelect.selectOption('en').catch(() => undefined);
        }
        return false;
      },
      { timeout: 20_000 },
    )
    .toBe(true);
}
