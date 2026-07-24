import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * B-072 / W6-3: scroll only inside the conversation timeline — not the document.
 * Runs in demo (local shell) as well as devauth/oidc.
 */
test.describe(`shell scroll container (${AUTH_MODE})`, () => {
  test('document does not scroll; timeline absorbs tall content; composer stays in view', async ({
    browser,
  }) => {
    const session = await openUserSession(browser, 'alice');
    const { page } = session;

    await selectChannelGeral(page);
    await expect(page.locator('.shell')).toBeVisible();
    await expect(page.locator('vc-composer')).toBeVisible();

    await page.locator('.timeline').evaluate((el) => {
      const filler = document.createElement('div');
      filler.dataset.e2eFiller = '1';
      filler.style.height = '3000px';
      filler.setAttribute('aria-hidden', 'true');
      el.appendChild(filler);
    });

    const metrics = await page.evaluate(() => {
      const timeline = document.querySelector('.timeline');
      const shell = document.querySelector('.shell');
      return {
        docOverflows: document.documentElement.scrollHeight > window.innerHeight + 2,
        bodyScrollTop: document.documentElement.scrollTop + document.body.scrollTop,
        timelineScrollable: !!timeline && timeline.scrollHeight > timeline.clientHeight + 2,
        timelineOverflowY: timeline ? getComputedStyle(timeline).overflowY : '',
        shellMaxHeight: shell ? getComputedStyle(shell).maxHeight : '',
      };
    });

    expect(metrics.timelineOverflowY === 'auto' || metrics.timelineOverflowY === 'scroll').toBe(
      true,
    );
    expect(metrics.timelineScrollable).toBe(true);
    expect(metrics.docOverflows).toBe(false);
    expect(metrics.bodyScrollTop).toBe(0);

    await expect(page.locator('vc-composer')).toBeInViewport();
    await expect(page.locator('.shell__header')).toBeInViewport();

    await session.context.close();
  });
});
