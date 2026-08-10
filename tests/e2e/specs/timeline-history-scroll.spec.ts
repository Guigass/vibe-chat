import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * B-088 / W9-1: reader in history is not pulled down when a new message arrives.
 */
test.describe(`timeline history scroll (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Cross-browser delivery requires API (devauth|oidc)');

  test('bob reading history is not dragged when alice sends', async ({ browser }) => {
    const uniqueBody = `e2e-hist-${Date.now()}`;

    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');

    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    await bob.page.locator('.timeline').evaluate((el) => {
      const filler = document.createElement('div');
      filler.dataset.e2eFiller = '1';
      filler.style.cssText = 'flex:0 0 3000px;min-height:3000px;height:3000px;';
      filler.setAttribute('aria-hidden', 'true');
      el.prepend(filler);
      el.scrollTop = 0;
    });

    const topBefore = await bob.page.locator('.timeline').evaluate((el) => el.scrollTop);
    expect(topBefore).toBeLessThan(80);

    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeVisible();
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();

    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(uniqueBody)).toBeVisible({ timeout: 30_000 });

    const topAfter = await bob.page.locator('.timeline').evaluate((el) => el.scrollTop);
    expect(topAfter).toBeLessThan(120);

    await expect(bob.page.getByTestId('timeline-jump')).toBeVisible();
    await expect(bob.page.getByTestId('timeline-jump')).toContainText(/Ir para a mais recente/i);

    await alice.context.close();
    await bob.context.close();
  });
});
