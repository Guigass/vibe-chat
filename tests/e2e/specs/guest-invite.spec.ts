import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

test.describe(`guest invite (${AUTH_MODE})`, () => {
  test('admin generates a one-time channel invite link', async ({ browser }) => {
    test.setTimeout(90_000);
    const demo = await openUserSession(browser, 'demo');
    await selectChannelGeral(demo.page);

    await demo.page.getByTestId('guest-invite-toggle').click();
    const panel = demo.page.getByTestId('guest-invite');
    await expect(panel).toBeVisible();
    await panel.getByRole('button', { name: /Gerar link|Create link/i }).click();
    await expect(panel.getByText(/Copie este link|Copy this link/i)).toBeVisible();
    await expect(panel.locator('#vc-guest-invite-url')).toHaveValue(/\/invite\//);

    await demo.context.close();
  });
});
