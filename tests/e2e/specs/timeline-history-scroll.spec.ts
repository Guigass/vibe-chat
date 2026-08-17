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

    const bobTimeline = bob.page.locator('.timeline');
    await bob.page.addStyleTag({
      content: `
        .timeline__list::before {
          content: '';
          display: block;
          flex: 0 0 3000px;
          min-height: 3000px;
          height: 3000px;
        }
      `,
    });
    await bob.page.evaluate(
      () => new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))),
    );
    // Real user input: wheel cancels TimelineScrollAnchorController (B-088); scroll
    // releases nearBottom. Stay below NEAR_TOP (120) so we don't trigger load-older.
    await bobTimeline.hover();
    await bob.page.mouse.wheel(0, -1800);
    await bobTimeline.evaluate((el) => {
      const max = Math.max(0, el.scrollHeight - el.clientHeight);
      if (el.scrollHeight - el.scrollTop - el.clientHeight < 400) {
        el.scrollTop = max > 800 ? 400 : 0;
      }
    });
    await bobTimeline.dispatchEvent('wheel', { deltaY: -400 });
    await bobTimeline.dispatchEvent('scroll');

    await expect
      .poll(() =>
        bobTimeline.evaluate((el) => el.scrollHeight - el.scrollTop - el.clientHeight),
      )
      .toBeGreaterThan(400);
    await expect(bob.page.getByTestId('timeline-jump')).toBeVisible();

    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeVisible();
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();

    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(uniqueBody)).toBeVisible({ timeout: 30_000 });

    const distanceAfter = await bobTimeline.evaluate(
      (el) => el.scrollHeight - el.scrollTop - el.clientHeight,
    );
    expect(distanceAfter).toBeGreaterThan(400);

    await expect(bob.page.getByTestId('timeline-jump')).toBeVisible();
    await expect(bob.page.getByTestId('timeline-jump')).toContainText(/Ir para a mais recente/i);

    await alice.context.close();
    await bob.context.close();
  });
});
