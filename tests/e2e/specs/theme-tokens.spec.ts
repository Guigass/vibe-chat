import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * BUG-007: production builds must not defer the global stylesheet via
 * `media="print" onload="this.media='all'"` — CSP blocks that inline handler
 * and leaves every `--vc-*` token empty (broken layout / "dark mode").
 */
test.describe(`theme design tokens (${AUTH_MODE})`, () => {
  test('applies --vc-* tokens after load without DevTools media=all hack', async ({ browser }) => {
    const session = await openUserSession(browser, 'alice');
    const { page } = session;

    await selectChannelGeral(page);

    const tokens = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      const link = document.querySelector<HTMLLinkElement>('link[rel="stylesheet"][href*="styles"]');
      return {
        ink: root.getPropertyValue('--vc-ink').trim(),
        brand: root.getPropertyValue('--vc-brand').trim(),
        surface: root.getPropertyValue('--vc-surface').trim(),
        dataTheme: document.documentElement.getAttribute('data-theme'),
        stylesheetMedia: link?.media ?? null,
        stylesheetHref: link?.getAttribute('href') ?? null,
      };
    });

    expect(tokens.ink, 'expected --vc-ink after load (CSP must not block stylesheet)').not.toBe('');
    expect(tokens.brand).not.toBe('');
    expect(tokens.surface).not.toBe('');
    expect(tokens.dataTheme === 'light' || tokens.dataTheme === 'dark').toBeTruthy();
    // When a hashed styles bundle exists it must already be media=all (not stuck on print).
    if (tokens.stylesheetHref) {
      expect(tokens.stylesheetMedia === 'all' || tokens.stylesheetMedia === '').toBeTruthy();
    }

    await page.getByRole('button', { name: /Ativar tema (escuro|claro)/ }).click();
    const afterToggle = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      return {
        ink: root.getPropertyValue('--vc-ink').trim(),
        dataTheme: document.documentElement.getAttribute('data-theme'),
      };
    });
    expect(afterToggle.ink).not.toBe('');
    expect(afterToggle.dataTheme).not.toBe(tokens.dataTheme);

    await session.context.close();
  });
});
