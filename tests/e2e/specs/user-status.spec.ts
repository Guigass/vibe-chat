import { expect, test } from '@playwright/test';
import { API_BASE_URL, loginAs } from '../helpers/auth';

test.describe('user status (B-116)', () => {
  test('set, clear and expire from the profile editor', async ({ page }) => {
    await loginAs(page, 'alice');
    await page.getByTestId('user-status-open').click();
    const editor = page.getByTestId('user-status-editor');
    await expect(editor).toBeVisible();
    await editor.getByTestId('user-status-state-focus').click();
    await editor.getByTestId('user-status-text').fill('revisando o plano');
    await editor.getByTestId('user-status-save').click();
    await expect(editor).toBeHidden();
    await expect(page.getByTestId('user-status-open')).toContainText('revisando o plano');

    await page.getByTestId('user-status-open').click();
    await editor.getByTestId('user-status-clear').click();
    await editor.getByTestId('user-status-clear').click();
    await expect(editor).toBeHidden();
    await expect(page.getByTestId('user-status-open')).not.toContainText('revisando o plano');

    // Reload in CI takes longer than a 1.5s TTL, so the row is already purged
    // when the shell reads availability and the button stays on "Sem status".
    const expiresAt = new Date(Date.now() + 20_000);
    const set = await page.request.put(`${API_BASE_URL}/api/v1/me/status`, {
      headers: { 'X-Dev-User': 'alice', 'content-type': 'application/json' },
      data: { state: 'custom', emoji: '⏳', text: 'some em instantes', expiresAt: expiresAt.toISOString() },
    });
    expect(set.ok()).toBeTruthy();
    await page.reload();
    await expect(page.getByTestId('user-status-open')).toContainText('some em instantes');
    const remaining = expiresAt.getTime() - Date.now();
    await page.waitForTimeout(Math.max(remaining, 0) + 2_000);
    await page.reload();
    await expect(page.getByTestId('user-status-open')).not.toContainText('some em instantes');
  });
});
