import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

test.describe(`forms (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Forms require the API (devauth|oidc)');

  test('alice publishes a form, submits, and bob approves', async ({ browser }) => {
    const title = `Formulário ${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');
    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    await alice.page.getByTestId('forms-nav').click();
    const panel = alice.page.getByTestId('form-panel');
    await panel.getByTestId('form-create').click();
    await panel.getByTestId('form-title').fill(title);
    await panel.getByTestId('form-field-label').fill('Motivo');
    await panel.getByRole('checkbox', { name: 'Bob' }).check();
    await panel.getByTestId('form-publish').click();
    await expect(panel.getByTestId('form-answer-motivo')).toBeVisible({ timeout: 15_000 });
    await panel.getByTestId('form-answer-motivo').fill('pedido e2e');
    await panel.getByTestId('form-submit').click();

    await bob.page.getByTestId('forms-nav').click();
    const bobPanel = bob.page.getByTestId('form-panel');
    await bobPanel.getByRole('button', { name: title }).click();
    await bobPanel.getByTestId('form-approve').click();
    await expect(bobPanel.getByText('Aprovado')).toBeVisible({ timeout: 15_000 });
  });
});
