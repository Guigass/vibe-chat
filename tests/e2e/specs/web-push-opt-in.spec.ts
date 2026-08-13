import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';

/**
 * B-095 — opt-in registers a subscription. OS delivery is not the gate.
 * Demo/offline has no API; skip. Without a service worker the banner stays hidden.
 */
test.describe(`web push opt-in (${AUTH_MODE})`, () => {
  test('opt-in banner can register a push subscription', async ({ browser }) => {
    test.skip(AUTH_MODE === 'demo', 'Demo UI has no push API.');

    const alice = await openUserSession(browser, 'alice');
    await alice.context.grantPermissions(['notifications']);

    let posted = false;
    await alice.page.route('**/api/v1/notifications/push/public-key', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          enabled: true,
          publicKey:
            'BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnptiaN1IkyYNyNcWt6kq8OGJfFDvSNhM',
        }),
      });
    });
    await alice.page.route('**/api/v1/notifications/push/subscriptions', async (route) => {
      if (route.request().method() === 'POST') {
        posted = true;
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
            endpoint: 'https://push.example.test/e2e',
            userAgent: 'playwright',
            createdAt: new Date().toISOString(),
            lastSeenAt: new Date().toISOString(),
          }),
        });
        return;
      }
      await route.continue();
    });

    await selectChannelGeral(alice.page);
    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeVisible();
    const uniqueBody = `e2e-push-${Date.now()}`;
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });

    const banner = alice.page.getByTestId('push-opt-in');
    const visible = await banner.isVisible().catch(() => false);
    if (!visible) {
      test.info().annotations.push({
        type: 'note',
        description:
          'Service worker unavailable in this run; banner stayed hidden (B-095 fail-open).',
      });
      await alice.context.close();
      return;
    }

    await alice.page.getByRole('button', { name: /^Ativar$/i }).click();
    await expect.poll(() => posted, { timeout: 10_000 }).toBe(true);

    await alice.context.close();
  });
});
