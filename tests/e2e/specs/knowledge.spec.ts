import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

test.describe(`knowledge (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Knowledge editor needs the API (devauth|oidc)');

  test('alice saves a page and bob sees a version conflict', async ({ browser }) => {
    const collection = `e2e-col-${Date.now()}`;
    const title = `e2e-page-${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');
    await selectChannelGeral(alice.page);
    await alice.page.getByTestId('knowledge-nav').click();
    const panel = alice.page.getByTestId('knowledge-panel');
    await expect(panel).toBeVisible();
    await panel.getByTestId('knowledge-collection-title').fill(collection);
    await panel.getByTestId('knowledge-create-collection').click();
    await expect(panel.getByRole('button', { name: collection })).toBeVisible();
    await panel.getByRole('button', { name: collection }).click();
    await panel.getByTestId('knowledge-page-title').fill(title);
    await panel.getByTestId('knowledge-create-page').click();
    await panel.getByTestId('knowledge-body').fill('versão da alice');
    await panel.getByTestId('knowledge-save').click();
    await expect(panel.getByTestId('knowledge-preview')).toContainText('versão da alice');

    const bob = await openUserSession(browser, 'bob');
    await selectChannelGeral(bob.page);
    await bob.page.getByTestId('knowledge-nav').click();
    const bobPanel = bob.page.getByTestId('knowledge-panel');
    await bobPanel.getByRole('button', { name: collection }).click();
    await bobPanel.getByRole('button', { name: title }).click();
    await bobPanel.getByTestId('knowledge-body').fill('versão antiga');

    await panel.getByTestId('knowledge-body').fill('versão nova da alice');
    await panel.getByTestId('knowledge-save').click();
    await expect(panel.getByTestId('knowledge-preview')).toContainText('versão nova da alice');

    await bobPanel.getByTestId('knowledge-save').click();
    await expect(bobPanel.getByTestId('knowledge-conflict')).toBeVisible();
    await expect(panel.getByTestId('knowledge-preview')).toContainText('versão nova da alice');
  });
});
