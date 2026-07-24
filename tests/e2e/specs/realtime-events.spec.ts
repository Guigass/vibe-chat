import { expect, test } from '@playwright/test';
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from '../helpers/auth';

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

    const aliceBubble = alice.page.locator('article.vc-msg', { hasText: body }).first();
    await aliceBubble.getByRole('button', { name: /^Editar$/i }).click();
    await aliceBubble.getByLabel(/Editar mensagem/i).fill(edited);
    await aliceBubble.getByRole('button', { name: /^Salvar$/i }).click();

    await expect(alice.page.getByText(edited)).toBeVisible({ timeout: 15_000 });
    await expect(bob.page.getByText(edited)).toBeVisible({ timeout: 30_000 });
    await expect(bob.page.getByText(/editada/i).first()).toBeVisible({ timeout: 15_000 });

    const bobBubble = bob.page.locator('article.vc-msg', { hasText: edited }).first();
    await bobBubble.getByRole('button', { name: /Reagir com 👍/i }).click();

    await expect(
      alice.page.locator('article.vc-msg', { hasText: edited }).getByLabel(/Reação 👍/i),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: edited }).getByLabel(/Reação 👍/i),
    ).toBeVisible({ timeout: 15_000 });

    await alice.page
      .locator('article.vc-msg', { hasText: edited })
      .getByRole('button', { name: /^Apagar$/i })
      .click();

    await expect(
      alice.page.locator('article.vc-msg', { hasText: /Mensagem removida/i }).first(),
    ).toBeVisible({ timeout: 15_000 });
    await expect(
      bob.page.locator('article.vc-msg', { hasText: /Mensagem removida/i }).first(),
    ).toBeVisible({ timeout: 30_000 });

    await alice.context.close();
    await bob.context.close();
  });
});
