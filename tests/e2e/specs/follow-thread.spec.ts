import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';
import { clickMessageToolbarButton } from '../helpers/message-actions';
import { sendButtonName } from '../helpers/ui-copy';

test.describe(`follow thread (${AUTH_MODE})`, () => {
  test('alice replies mentioning bob; bob sees the thread in followed list', async ({ browser }) => {
    const parentBody = `e2e-follow-parent-${Date.now()}`;
    const replyBody = `e2e-follow-reply-${Date.now()}`;

    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');

    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    const aliceComposer = alice.page.locator('textarea').first();
    await aliceComposer.fill(parentBody);
    await alice.page.getByRole('button', { name: sendButtonName }).click();
    await expect(alice.page.getByText(parentBody)).toBeVisible({ timeout: 15_000 });

    if (AUTH_MODE === 'demo') {
      test.info().annotations.push({
        type: 'note',
        description: 'Demo mode is local-only; skip follow-thread assert.',
      });
      await alice.context.close();
      await bob.context.close();
      return;
    }

    const aliceBubble = alice.page.locator('article.vc-msg', { hasText: parentBody }).first();
    await clickMessageToolbarButton(aliceBubble, /^(Abrir thread|Open thread|\d+ resposta)/i);
    await expect(alice.page.locator('vc-thread-panel')).toBeVisible({ timeout: 15_000 });

    await expect(bob.page.getByText(parentBody)).toBeVisible({ timeout: 30_000 });
    const bobBubble = bob.page.locator('article.vc-msg', { hasText: parentBody }).first();
    await clickMessageToolbarButton(bobBubble, /^(Abrir thread|Open thread|\d+ resposta)/i);
    const bobThread = bob.page.locator('vc-thread-panel');
    await expect(bobThread).toBeVisible({ timeout: 15_000 });
    await bobThread.locator('textarea').first().fill(replyBody);
    await bobThread.getByRole('button', { name: /^(Responder|Reply)$/i }).click();
    await expect(bobThread.getByText(replyBody)).toBeVisible({ timeout: 15_000 });
    await bob.page.keyboard.press('Escape');

    await bob.page.getByTestId('followed-threads-nav').click();
    const followed = bob.page.locator('vc-followed-threads-panel');
    await expect(followed).toBeVisible({ timeout: 15_000 });
    await expect(followed.getByText(parentBody.slice(0, 20))).toBeVisible({ timeout: 15_000 });

    await alice.context.close();
    await bob.context.close();
  });
});
