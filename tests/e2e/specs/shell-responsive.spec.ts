import { expect, test, type Page } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * UX-003: narrow viewports auto-collapse the sidebar into an overlay rail
 * so the timeline/composer keep usable width.
 */
async function expectCollapsedNarrowShell(page: Page): Promise<void> {
  const shell = page.locator('.shell');
  await expect(shell).toBeVisible();
  await expect(shell).toHaveClass(/shell--sidebar-collapsed/);
  await expect(shell).toHaveClass(/shell--narrow/);
  await expect(page.locator('.shell__sidebar')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Abrir barra lateral' })).toBeVisible();
  await expect(page.locator('vc-composer')).toBeInViewport();
  await expect(page.locator('.shell__header')).toBeInViewport();
}

test.describe(`shell responsive sidebar (${AUTH_MODE})`, () => {
  for (const width of [320, 360, 400] as const) {
    test(`auto-collapses at ${width}px and restores overlay controls`, async ({ browser }) => {
      const session = await openUserSession(browser, 'alice');
      const { page } = session;

      await page.setViewportSize({ width, height: 720 });
      await selectChannelGeral(page);
      await expectCollapsedNarrowShell(page);

      await page.getByRole('button', { name: 'Abrir barra lateral' }).click();
      await expect(page.locator('.shell')).not.toHaveClass(/shell--sidebar-collapsed/);
      await expect(page.locator('.shell__sidebar')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Fechar barra lateral' })).toBeVisible();

      await page.keyboard.press('Escape');
      await expectCollapsedNarrowShell(page);

      await page.getByRole('button', { name: 'Abrir barra lateral' }).click();
      await page.getByRole('button', { name: 'Fechar barra lateral' }).click();
      await expectCollapsedNarrowShell(page);

      await session.context.close();
    });
  }

  test('desktop width keeps the sidebar rail open', async ({ browser }) => {
    const session = await openUserSession(browser, 'alice');
    const { page } = session;

    await page.setViewportSize({ width: 1280, height: 800 });
    await selectChannelGeral(page);

    const shell = page.locator('.shell');
    await expect(shell).toBeVisible();
    await expect(shell).not.toHaveClass(/shell--sidebar-collapsed/);
    await expect(shell).not.toHaveClass(/shell--narrow/);
    await expect(page.locator('.shell__sidebar')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Abrir barra lateral' })).toHaveCount(0);

    await session.context.close();
  });
});
