import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { openUserSession, selectChannelGeral, resetProfileLocale } from '../helpers/auth';
import { openMessageMoreMenu, clickMessageMenuItem } from '../helpers/message-actions';

async function scan(page: Page, info: TestInfo, name: string) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  await info.attach(`${name}-axe`, { body: JSON.stringify(results, null, 2), contentType: 'application/json' });
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot });
  await info.attach(`${name}-screenshot`, { path: screenshot, contentType: 'image/png' });
  expect.soft(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => ({ id: v.id, nodes: v.nodes.map((n) => ({ target: n.target, summary: n.failureSummary })) }))).toEqual([]);
  expect(await page.locator('[tabindex]').evaluateAll((els) => els.filter((el) => Number(el.getAttribute('tabindex')) > 0).length)).toBe(0);
  const smallTargets = await page.locator('button, [role="button"]').evaluateAll((els) => els.flatMap((el) => {
    const rect = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    return rect.width && rect.height && style.visibility !== 'hidden' && (rect.width < 24 || rect.height < 24)
      ? [{ label: el.getAttribute('aria-label') ?? el.textContent, width: rect.width, height: rect.height }] : [];
  }));
  expect.soft(smallTargets).toEqual([]);
}

for (const theme of ['light', 'dark'] as const) {
  test(`B-103 axe login, shell, thread and admin — ${theme}`, async ({ browser, page }, info) => {
    test.setTimeout(180_000);
    await page.addInitScript((value) => localStorage.setItem('vc.theme', value), theme);
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await scan(page, info, `${theme}-login`);
    const session = await openUserSession(browser, 'demo');
    try {
      await session.page.evaluate((value) => {
        localStorage.setItem('vc.theme', value);
        document.documentElement.dataset.theme = value;
      }, theme);
      await selectChannelGeral(session.page);
      const body = `a11y-${theme}-${Date.now()}`;
      await session.page.locator('vc-composer textarea').fill(body);
      await session.page.locator('vc-composer textarea').press('Enter');
      const bubble = session.page.locator('vc-message-bubble').filter({ hasText: body }).last();
      await expect(bubble).toBeVisible();
      await expect(bubble.locator('article')).toHaveAttribute('data-status', 'persisted');
      await bubble.locator('article').focus();
      await expect(bubble.getByRole('button', { name: 'Ações da mensagem', exact: true })).toBeVisible();
      await scan(session.page, info, `${theme}-shell`);
      await session.page.getByRole('button', { name: 'Inserir emoji', exact: true }).click();
      await expect(session.page.getByRole('dialog', { name: 'Seletor de emoji' })).toBeVisible();
      await expect(session.page.getByRole('searchbox', { name: 'Buscar emoji', exact: true })).toBeFocused();
      await scan(session.page, info, `${theme}-emoji`);
      await session.page.keyboard.press('ArrowDown');
      await expect(session.page.locator('.emoji-picker__emoji').first()).toBeFocused();
      await session.page.keyboard.press('ArrowRight');
      await expect(session.page.locator('.emoji-picker__emoji').nth(1)).toBeFocused();
      await session.page.keyboard.press('Escape');
      await openMessageMoreMenu(bubble, session.page);
      await clickMessageMenuItem(session.page, 'Encaminhar');
      await expect(session.page.getByRole('dialog', { name: 'Encaminhar mensagem' })).toBeVisible();
      await scan(session.page, info, `${theme}-forward`);
      await session.page.keyboard.press('Escape');
      await openMessageMoreMenu(bubble, session.page);
      await clickMessageMenuItem(session.page, /thread/i);
      await expect(session.page.locator('vc-thread-panel')).toBeVisible();
      await scan(session.page, info, `${theme}-thread`);
      await session.page.goto('/admin');
      await expect(session.page.locator('.admin-shell')).toBeVisible();
      await expect(session.page.locator('vc-admin-overview')).toBeVisible();
      await scan(session.page, info, `${theme}-admin`);
    } finally { await session.context.close(); }
  });
}

test('B-103 keyboard login, skip link, send and palette focus restore', async ({ page, request }) => {
  await resetProfileLocale(request, 'alice');
  await page.goto('/login');
  for (let i = 0; i < 20 && !(await page.getByRole('button', { name: 'Alice', exact: true }).evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab');
  }
  await expect(page.getByRole('button', { name: 'Alice', exact: true })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('vc-composer textarea')).toBeVisible();
  // A fresh document proves that the skip link is first in sequential focus order.
  await page.reload();
  await expect(page.locator('vc-composer textarea')).toBeVisible();
  await expect(page.getByRole('heading', { name: /geral/i })).toBeVisible();
  await expect(page.locator('.timeline__loading')).toHaveCount(0);
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Ir para a conversa' });
  await expect(skip).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#conversation')).toBeFocused();
  for (let i = 0; i < 1000 && !(await page.locator('vc-composer textarea').evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab');
  }
  const composer = page.locator('vc-composer textarea');
  await expect(composer).toBeFocused();
  const body = `keyboard-a11y-${Date.now()}`;
  await page.keyboard.type(body);
  await expect(composer).toHaveValue(body);
  await page.keyboard.press('Enter');
  await expect(page.getByText(body, { exact: true })).toBeVisible();
  await page.keyboard.press('Control+k');
  const dialog = page.getByTestId('command-palette');
  await expect(dialog).toBeVisible();
  await expect(page.getByTestId('command-palette-query')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Tab');
  expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(composer).toBeFocused();
  await page.emulateMedia({ reducedMotion: 'reduce', forcedColors: 'active' });
  expect(await composer.evaluate((el) => getComputedStyle(el).outlineStyle)).not.toBe('none');
  expect(await composer.evaluate((el) => getComputedStyle(el).transitionDuration)).toBe('0s');
});
