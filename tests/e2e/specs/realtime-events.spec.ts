import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';
import {
  clickMessageMenuItem,
  clickMessageToolbarButton,
  openMessageMoreMenu,
} from '../helpers/message-actions';

/**
 * B-070: MessageCreated / edit / delete / reactions cross-session without reload.
 */
test.describe(`realtime events (${AUTH_MODE})`, () => {
  test.skip(AUTH_MODE === 'demo', 'Cross-browser realtime requires API (devauth|oidc)');

  test('bob sees alice create/edit/delete/react without reload', async ({ browser }) => {
    const stamp = Date.now();
    const body = `e2e-rt-${stamp}`;
    const edited = `${body}-edited`;

    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');

    await selectChannelGeral(alice.page);
    await selectChannelGeral(bob.page);

    const composer = alice.page.locator('textarea').first();
    await expect(composer).toBeVisible();
    await composer.fill(body);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();

    await expect(alice.page.getByText(body)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(body)).toBeVisible({ timeout: 30_000 });

    const aliceBubble = alice.page.locator('article.vc-msg', { hasText: body }).last();
    await openMessageMoreMenu(aliceBubble, alice.page);
    await clickMessageMenuItem(alice.page, /^Editar$/i);
    // B-173: edit mode loads body into the channel composer (not an inline bubble textarea).
    const editBox = alice.page.locator('vc-composer textarea').first();
    await expect(editBox).toBeVisible({ timeout: 5_000 });
    await expect(alice.page.getByText(/Editando mensagem/i)).toBeVisible();
    await editBox.fill(edited);
    await alice.page.getByRole('button', { name: /^Salvar$/i }).click();

    await expect(alice.page.getByText(edited)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(edited)).toBeVisible({ timeout: 30_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: edited }).getByLabel(/^Editada$/i),
    ).toBeVisible({ timeout: 15_000 });
    await expect(alice.page.getByRole('button', { name: /^Enviar$/i })).toBeVisible();

    const editedMessageId = await alice.page
      .locator('article.vc-msg', { hasText: edited })
      .last()
      .getAttribute('data-message-id');
    expect(editedMessageId).toBeTruthy();

    const bobBubble = bob.page.locator(`article.vc-msg[data-message-id="${editedMessageId}"]`);
    await bobBubble.scrollIntoViewIfNeeded();
    await clickMessageToolbarButton(bobBubble, /Reagir com 👍/i);

    await expect(bobBubble.getByLabel(/Reação 👍/i)).toBeVisible({ timeout: 15_000 });
    await expect(
      alice.page.locator(`article.vc-msg[data-message-id="${editedMessageId}"]`).getByLabel(/Reação 👍/i),
    ).toBeVisible({ timeout: 30_000 });

    const aliceEditedBubble = alice.page.locator('article.vc-msg', { hasText: edited }).last();
    await openMessageMoreMenu(aliceEditedBubble, alice.page);
    await clickMessageMenuItem(alice.page, /^Apagar$/i);

    await expect(
      alice.page.locator('article.vc-msg', { hasText: /Mensagem removida/i }).last(),
    ).toBeVisible({ timeout: 15_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: /Mensagem removida/i }).last(),
    ).toBeVisible({ timeout: 30_000 });

    await alice.context.close();
    await bob.context.close();
  });
});
