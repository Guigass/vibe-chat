import { expect, test } from '@playwright/test';
import { AUTH_MODE, openUserSession } from '../helpers/auth';

test.describe(`group dm (${AUTH_MODE})`, () => {
  test('three sessions exchange a group DM message', async ({ browser }) => {
    const uniqueBody = `e2e-gdm-${Date.now()}`;
    const alice = await openUserSession(browser, 'alice');
    const bob = await openUserSession(browser, 'bob');
    const demo = await openUserSession(browser, 'demo');

    if (AUTH_MODE === 'demo') {
      test.info().annotations.push({
        type: 'note',
        description: 'Demo UI is local-only; group DM API needs DevAuth.',
      });
    }

    await alice.page.getByTestId('group-dm-picker-toggle').click();
    await alice.page.getByRole('button', { name: /@?\s*Bob/i }).first().click();
    await alice.page.getByRole('button', { name: /@?\s*Demo/i }).first().click();
    await alice.page.getByTestId('group-dm-open').click();

    await expect(alice.page.getByTestId('group-dm-header')).toBeVisible({ timeout: 15_000 });

    const composer = alice.page.locator('textarea').first();
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });

    if (AUTH_MODE !== 'demo') {
      await bob.page.getByText(/Bob,\s*Demo|Demo,\s*Bob/i).first().click().catch(() => undefined);
      await demo.page.getByText(/Alice,\s*Bob|Bob,\s*Alice/i).first().click().catch(() => undefined);
      await expect(bob.page.getByText(uniqueBody)).toBeVisible({ timeout: 30_000 });
      await expect(demo.page.getByText(uniqueBody)).toBeVisible({ timeout: 30_000 });
    }

    await alice.context.close();
    await bob.context.close();
    await demo.context.close();
  });
});
