import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';

/**
 * Fatia vertical E2E: duas sessões no #geral.
 *
 * - demo: ambos entram via "Explorar demo local"; alice envia (assert local).
 *   Recebimento cruzado é skipado — demo UI não sincroniza entre browsers.
 * - devauth / oidc: alice envia e bob deve ver a mensagem (API + SignalR).
 */
test.describe(`two sessions chat (${AUTH_MODE})`, () => {
  test('alice and bob join #geral; alice sends; bob receives when backend available', async ({
    browser,
  }) => {
    const uniqueBody = `e2e-ping-${Date.now()}`;

    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');

    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    // Composer textarea
    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeVisible();
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();

    // Alice always sees her optimistic/local message — exactly one bubble (BUG-001).
    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });
    await expect(alice.page.getByText(uniqueBody)).toHaveCount(1);

    if (AUTH_MODE === 'demo') {
      test.info().annotations.push({
        type: 'note',
        description:
          'Demo mode is local-only. Cross-browser delivery requires E2E_AUTH_MODE=devauth|oidc with API running.',
      });
      // Soft check: bob at least has #geral shell
      await expect(bob.page.getByRole('heading', { name: /#?\s*geral/i })).toBeVisible();
    } else {
      await expect(bob.page.getByText(uniqueBody)).toBeVisible({ timeout: 30_000 });
    }

    await alice.context.close();
    await bob.context.close();
  });
});
