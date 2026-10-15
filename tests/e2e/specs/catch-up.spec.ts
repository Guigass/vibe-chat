import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

test.describe(`catch-up (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Catch-up needs the API (devauth|oidc)');

  test('alice opens catch-up and keeps the deterministic empty or listed state', async ({ browser }) => {
    const alice = await openUserSession(browser, 'alice');
    await selectChannelGeral(alice.page);
    await alice.page.getByTestId('catch-up-open').click();
    await expect(alice.page.getByTestId('catch-up-page')).toBeVisible();
    await expect(alice.page.getByRole('heading', { level: 1 })).toBeVisible();
    const generate = alice.page.getByTestId('catch-up-generate');
    await expect(generate).toBeEnabled();
    await generate.click();
    await expect(alice.page.getByTestId('catch-up-ai')).toBeVisible();
    const empty = alice.page.getByTestId('catch-up-empty');
    const item = alice.page.getByTestId('catch-up-item');
    await expect(empty.or(item.first())).toBeVisible();
  });
});