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
    // Edit mode replaces the <p> body; locate the textarea on the page (not via hasText).
    const editBox = alice.page.getByRole('textbox', { name: /Editar mensagem/i });
    await expect(editBox).toBeVisible({ timeout: 5_000 });
    await editBox.fill(edited);
    await alice.page.getByRole('button', { name: /^Salvar$/i }).click();

    await expect(alice.page.getByText(edited)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(edited)).toBeVisible({ timeout: 30_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: edited }).getByText(/editada/i),
    ).toBeVisible({ timeout: 15_000 });

    const bobBubble = bob.page.locator('article.vc-msg', { hasText: edited }).last();
    await clickMessageToolbarButton(bobBubble, /Reagir com 👍/i);

    await expect(
      alice.page.locator('article.vc-msg', { hasText: edited }).getByLabel(/Reação 👍/i),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: edited }).getByLabel(/Reação 👍/i),
    ).toBeVisible({ timeout: 15_000 });

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
