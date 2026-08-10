import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';
import { clickMessageToolbarButton } from '../helpers/message-actions';

/**
 * B-084 — Bob cites Alice; Alice sees the quote without F5 (devauth/oidc).
 */
test.describe(`reply citing (${AUTH_MODE})`, () => {
  test('bob replies citing alice; alice sees quote via hub', async ({ browser }) => {
    const parentBody = `e2e-cite-parent-${Date.now()}`;
    const replyBody = `e2e-cite-reply-${Date.now()}`;

    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');

    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    const aliceComposer = alice.page.locator('textarea').first();
    await aliceComposer.fill(parentBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
    await expect(alice.page.getByText(parentBody)).toBeVisible({ timeout: 15_000 });

    if (AUTH_MODE === 'demo') {
      test.info().annotations.push({
        type: 'note',
        description: 'Demo mode is local-only; skip cross-session cite assert.',
      });
      await alice.context.close();
      await bob.context.close();
      return;
    }

    await expect(bob.page.getByText(parentBody)).toBeVisible({ timeout: 30_000 });

    const parentBubble = bob.page.locator('article.vc-msg', { hasText: parentBody }).first();
    await clickMessageToolbarButton(parentBubble, /^Responder$/i);
    await expect(bob.page.getByText(/Respondendo a/i)).toBeVisible();

    const bobComposer = bob.page.locator('textarea').first();
    await bobComposer.fill(replyBody);
    await bob.page.getByRole('button', { name: /^Enviar$/i }).click();

    await expect(bob.page.getByText(replyBody)).toBeVisible({ timeout: 15_000 });
    await expect(alice.page.getByText(replyBody)).toBeVisible({ timeout: 30_000 });

    const aliceReply = alice.page.locator('article.vc-msg', { hasText: replyBody }).first();
    await expect(aliceReply.locator('.vc-msg__quote')).toBeVisible();
    await expect(aliceReply.locator('.vc-msg__quote')).toContainText(parentBody.slice(0, 20));

    await alice.context.close();
    await bob.context.close();
  });
});
