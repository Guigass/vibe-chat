import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';
import { clickMessageMenuItem, openMessageMoreMenu } from '../helpers/message-actions';
import { sendButtonName } from '../helpers/ui-copy';

test.describe(`commitments (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Conversion requires the API (devauth|oidc)');

  test('alice converts a message into a decision and opens the source', async ({ browser }) => {
    const body = `e2e-decision-${Date.now()}`;
    const title = `Decisão ${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');
    await selectChannelGeral(alice.page);

    const composer = alice.page.locator('textarea').first();
    await composer.fill(body);
    await alice.page.getByRole('button', { name: sendButtonName }).click();
    const bubble = alice.page.locator('article.vc-msg', { hasText: body }).last();
    await expect(bubble).toBeVisible({ timeout: 15_000 });

    await openMessageMoreMenu(bubble, alice.page);
    await clickMessageMenuItem(alice.page, /^Decisão$/);
    const dialog = alice.page.getByTestId('commitment-dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByTestId('commitment-title').fill(title);
    await dialog.getByRole('button', { name: /^Criar$/ }).click();
    await expect(dialog).toBeHidden();

    await alice.page.getByTestId('commitments-nav').click();
    const panel = alice.page.getByTestId('commitment-panel');
    await expect(panel.getByText(title)).toBeVisible();
    await panel.getByRole('button', { name: title }).click();
    await panel.getByRole('button', { name: /Abrir origem/ }).click();
    await expect(alice.page.locator('article.vc-msg', { hasText: body }).last()).toBeVisible();
  });
});
