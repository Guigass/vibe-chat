import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

test.describe(`incidents (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Incidents require the API (devauth|oidc)');

  test('demo declares, mitigates and resolves an incident room', async ({ browser }) => {
    const title = `Incidente ${Date.now()}`;
    const demo = await openUserSession(browser, 'demo');
    await selectChannelGeral(demo.page);

    await demo.page.getByTestId('incident-declare').click();
    await demo.page.getByTestId('incident-title').fill(title);
    await demo.page.getByTestId('incident-create').click();

    const banner = demo.page.getByTestId('incident-banner');
    await expect(banner).toBeVisible({ timeout: 15_000 });
    await expect(banner.getByTestId('incident-status')).toHaveText('Declarado');
    await banner.getByTestId('incident-mitigate').click();
    await expect(banner.getByTestId('incident-status')).toHaveText('Mitigando', { timeout: 15_000 });
    await banner.getByTestId('incident-resolve').click();
    await banner.getByTestId('incident-resolve-confirm').click();
    await expect(banner.getByTestId('incident-status')).toHaveText('Resolvido', { timeout: 15_000 });
  });
});
