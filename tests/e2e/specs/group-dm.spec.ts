import { expect, test, type Page } from '@playwright/test';
import { AUTH_MODE, openUserSession } from '../helpers/auth';

test.describe(`group dm (${AUTH_MODE})`, () => {
  test('three sessions exchange a group DM message', async ({ browser }) => {
    test.setTimeout(120_000);
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

    const members = alice.page.locator('.vc-sidebar-nav__members');
    await expect(members.getByRole('button', { name: /Mensagem para Bob/i })).toBeVisible();
    await expect(members.getByRole('button', { name: /Mensagem para Demo/i })).toBeVisible();

    await alice.page.getByTestId('group-dm-picker-toggle').click();
    const picker = alice.page.getByTestId('group-dm-picker');
    await expect(picker).toBeVisible();

    await members.getByRole('button', { name: /Mensagem para Bob/i }).click();
    await members.getByRole('button', { name: /Mensagem para Demo/i }).click();
    await expect(picker.locator('.vc-sidebar-nav__chip', { hasText: 'Bob' })).toBeVisible();
    await expect(picker.locator('.vc-sidebar-nav__chip', { hasText: 'Demo' })).toBeVisible();

    const created = alice.page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' && response.url().includes('/group-dms'),
      { timeout: 15_000 },
    );
    await picker.getByRole('button', { name: /Abrir conversa/i }).click();
    if (AUTH_MODE !== 'demo') {
      const response = await created;
      expect(response.ok(), `POST /group-dms → ${response.status()}`).toBeTruthy();
    }

    await expect(alice.page.getByTestId('group-dm-header')).toBeVisible({ timeout: 15_000 });

    const composer = alice.page.locator('textarea').first();
    await composer.fill(uniqueBody);
    await alice.page.getByRole('button', { name: /^(Enviar|Send)$/i }).click();
    await expect(alice.page.getByText(uniqueBody)).toBeVisible({ timeout: 15_000 });

    if (AUTH_MODE !== 'demo') {
      // Título omite o próprio usuário: Bob vê "Alice, Demo"; Demo vê "Alice, Bob".
      await openGroupDmFromSidebar(bob.page, /Alice,\s*Demo|Demo,\s*Alice/);
      await openGroupDmFromSidebar(demo.page, /Alice,\s*Bob|Bob,\s*Alice/);
      await expect(bob.page.getByText(uniqueBody)).toBeVisible({ timeout: 20_000 });
      await expect(demo.page.getByText(uniqueBody)).toBeVisible({ timeout: 20_000 });
    }

    await alice.context.close();
    await bob.context.close();
    await demo.context.close();
  });
});

async function openGroupDmFromSidebar(page: Page, title: RegExp): Promise<void> {
  await page.reload();
  await page.waitForURL(/\/app/);
  const item = page.getByRole('button', { name: title }).first();
  await expect(item).toBeVisible({ timeout: 20_000 });
  await item.click();
}
