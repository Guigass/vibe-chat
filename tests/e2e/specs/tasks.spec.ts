import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';
import { clickMessageMenuItem, openMessageMoreMenu } from '../helpers/message-actions';
import { sendButtonName } from '../helpers/ui-copy';

test.describe(`tasks (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Task creation requires the API (devauth|oidc)');

  test('alice creates a task from a message and completes it', async ({ browser }) => {
    const body = `e2e-task-${Date.now()}`;
    const title = `Tarefa ${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');
    await selectChannelGeral(alice.page);

    const composer = alice.page.locator('textarea').first();
    await composer.fill(body);
    await alice.page.getByRole('button', { name: sendButtonName }).click();
    const bubble = alice.page.locator('article.vc-msg', { hasText: body }).last();
    await expect(bubble).toBeVisible({ timeout: 15_000 });

    await openMessageMoreMenu(bubble, alice.page);
    await clickMessageMenuItem(alice.page, /^Tarefa$/);
    const dialog = alice.page.getByTestId('task-dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByTestId('task-title').fill(title);
    await dialog.getByRole('button', { name: /^Criar$/ }).click();
    await expect(dialog).toBeHidden();

    await alice.page.getByTestId('tasks-nav').click();
    const panel = alice.page.getByTestId('task-panel');
    const row = panel.locator('li', { hasText: title });
    await expect(row).toBeVisible();
    await row.getByTestId('task-complete').click();
    await expect(row).toBeHidden();
  });
});
