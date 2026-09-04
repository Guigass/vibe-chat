import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * B-099: paleta de comandos e navegação só pelo teclado.
 * DevAuth: #geral + DMs Bob/Demo. Demo offline: três canais seed.
 */
test.describe(`command palette (${AUTH_MODE})`, () => {
  test('navigates three destinations by keyboard and sends a message', async ({
    browser,
  }) => {
    const hops =
      AUTH_MODE === 'demo'
        ? [
            { query: 'ger', heading: /geral/i },
            { query: 'design', heading: /design-system/i },
            { query: 'inc', heading: /incidentes/i },
          ]
        : [
            { query: 'ger', heading: /geral/i },
            { query: 'bob', heading: /bob/i },
            { query: 'demo', heading: /demo/i },
          ];

    const alice = await openUserSession(browser, 'alice');
    await selectChannelGeral(alice.page);

    for (const hop of hops) {
      // Dispatch on window so Chromium's omnibox Ctrl+K does not steal focus.
      await alice.page.evaluate(() => {
        window.dispatchEvent(
          new KeyboardEvent('keydown', {
            key: 'k',
            ctrlKey: true,
            bubbles: true,
            cancelable: true,
          }),
        );
      });
      const palette = alice.page.getByTestId('command-palette');
      await expect(palette).toBeVisible();
      const query = alice.page.getByTestId('command-palette-query');
      await expect(query).toBeFocused();
      await query.fill(hop.query);
      await expect(palette.getByRole('option').first()).toBeVisible();
      await alice.page.keyboard.press('Enter');
      await expect(palette).toHaveCount(0);
      await expect(alice.page.getByRole('heading', { name: hop.heading })).toBeVisible();
    }

    const body = `e2e-palette-${Date.now()}`;
    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeFocused();
    await composer.fill(body);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
    await expect(alice.page.getByText(body)).toBeVisible({ timeout: 15_000 });

    await composer.click();
    await composer.fill('ainda digitando');
    await alice.page.keyboard.press('?');
    await expect(alice.page.getByTestId('shortcut-sheet')).toHaveCount(0);

    await alice.page.locator('.timeline__list, vc-timeline').first().click();
    await alice.page.keyboard.press('?');
    await expect(alice.page.getByTestId('shortcut-sheet')).toBeVisible();
    await expect(alice.page.getByRole('heading', { name: /atalhos/i })).toBeVisible();

    await alice.page.keyboard.press('Escape');
    await expect(alice.page.getByTestId('shortcut-sheet')).toHaveCount(0);

    await alice.context.close();
  });
});
