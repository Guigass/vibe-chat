import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * B-086: rascunho persistente no cliente (F5 preserva texto).
 */
test.describe(`persistent draft (${AUTH_MODE})`, () => {
  test('typing then reload restores composer text', async ({ browser }) => {
    const draftBody = `e2e-draft-${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');

    await selectChannelGeral(alice.page);

    const composer = alice.page.locator('vc-composer textarea').first();
    await expect(composer).toBeVisible();
    await composer.fill(draftBody);

    // Debounce de gravação (400 ms) + margem.
    await alice.page.waitForTimeout(600);

    await alice.page.reload();
    await alice.page.waitForURL(/\/app/);
    await selectChannelGeral(alice.page);

    const restored = alice.page.locator('vc-composer textarea').first();
    await expect(restored).toBeVisible();
    await expect(restored).toHaveValue(draftBody, { timeout: 15_000 });

    await alice.context.close();
  });
});
