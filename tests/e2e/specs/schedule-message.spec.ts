import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession, selectChannelGeral } from '../helpers/auth';

/**
 * B-113 — schedule, edit and cancel from the composer. Draft stays until the API confirms.
 */
test.describe(`schedule message (${AUTH_MODE})`, () => {
  test('alice schedules, edits and cancels without losing the draft early', async ({ browser }) => {
    test.skip(AUTH_MODE === 'demo', 'Demo mode does not persist schedules.');

    const body = `e2e-agenda-${Date.now()}`;
    const edited = `${body}-editado`;
    const session = await openUserSession(browser, 'alice');
    await selectChannelGeral(session.page);

    const composer = session.page.locator('textarea').first();
    await composer.fill(body);
    await session.page.getByTestId('schedule-toggle').click();
    await session.page.getByTestId('schedule-at').fill('2027-06-01T09:00');
    await expect(composer).toHaveValue(body);

    await session.page.getByRole('button', { name: /Agendar envio|Schedule message/ }).click();
    await expect(composer).toHaveValue('', { timeout: 15_000 });

    await session.page.getByTestId('schedule-list').click();
    const item = session.page.locator('.schedule-panel__item', { hasText: body });
    await expect(item).toBeVisible({ timeout: 15_000 });

    await item.getByRole('button', { name: /Editar|Edit/ }).click();
    await item.locator('input').last().fill(edited);
    await item.getByRole('button', { name: /Salvar alteração|Save change/ }).click();
    await expect(session.page.locator('.schedule-panel__item', { hasText: edited })).toBeVisible({
      timeout: 15_000,
    });

    session.page.once('dialog', (dialog) => dialog.accept());
    await session.page
      .locator('.schedule-panel__item', { hasText: edited })
      .getByRole('button', { name: /Cancelar agendamento|Cancel schedule/ })
      .click();
    await expect(session.page.locator('.schedule-panel__item', { hasText: edited })).toHaveCount(0, {
      timeout: 15_000,
    });

    await session.context.close();
  });
});