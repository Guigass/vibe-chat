import { expect, test, type Page } from '@playwright/test';
import { openUserSession, selectChannelGeral } from '../helpers/auth';
import { clickMessageToolbarButton } from '../helpers/message-actions';
import { sendButtonName } from '../helpers/ui-copy';

const viewports = [
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'mobile', width: 390, height: 844 },
] as const;

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => {
    const root = document.documentElement;
    const body = document.body;
    return Math.max(root.scrollWidth, body.scrollWidth) - root.clientWidth;
  });
  expect(overflow).toBeLessThanOrEqual(1);
}

async function expectBoxInside(page: Page, selector: string): Promise<void> {
  const inside = await page.locator(selector).first().evaluate((element) => {
    const rect = element.getBoundingClientRect();
    const view = document.documentElement.clientWidth;
    return rect.width > 0 && rect.left >= -1 && rect.right <= view + 1;
  });
  expect(inside).toBe(true);
}

for (const viewport of viewports) {
  test(`layout login ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('.login-hero__brand')).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await expectBoxInside(page, '.login-hero__content');
  });

  test(`layout shell conversation ${viewport.name}`, async ({ browser }) => {
    const session = await openUserSession(browser, 'alice');
    const page = session.page;
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.reload();
    await selectChannelGeral(page);
    await expect(page.locator('.shell__header h1')).toBeVisible();
    await expect(page.locator('vc-composer textarea').first()).toBeVisible();
    await expect(page.locator('#vc-search')).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await expectBoxInside(page, '.shell__header');

    if (viewport.name === 'mobile') {
      const send = page.getByRole('button', { name: sendButtonName }).first();
      const box = await send.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.height).toBeGreaterThanOrEqual(40);
      expect(box!.width).toBeGreaterThanOrEqual(40);
    }

    const showSidebar = page.getByRole('button', { name: /Mostrar barra/i });
    if (await showSidebar.isVisible().catch(() => false)) {
      await showSidebar.click();
      await expect(page.locator('vc-channel-list')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.getByRole('button', { name: /Esconder barra/i }).click();
    } else {
      await expect(page.locator('vc-channel-list')).toBeVisible();
      await expectNoHorizontalOverflow(page);
    }

    await page.locator('#vc-search').click();
    await page.locator('#vc-search').fill('ola');
    await expect(page.locator('.shell__search-panel')).toBeVisible();
    await expectBoxInside(page, '.shell__search-panel');
    await expectNoHorizontalOverflow(page);
    await page.keyboard.press('Escape');

    await page.getByRole('button', { name: /^Configurações$/ }).click();
    await expect(page.getByTestId('prefs-panel')).toBeVisible();
    await expect(page.getByTestId('appearance-panel')).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.getByRole('button', { name: /Fechar painel/i }).click();

    if (viewport.name === 'mobile') {
      await page.getByRole('button', { name: /Mostrar barra/i }).click();
    }
    await page.getByTestId('own-profile-open').click();
    await expect(page.getByTestId('member-profile-drawer')).toBeVisible();
    await expectBoxInside(page, '.profile-drawer');
    await expectNoHorizontalOverflow(page);

    await session.context.close();
  });

  test(`layout thread and admin ${viewport.name}`, async ({ browser }) => {
    test.setTimeout(120_000);
    const session = await openUserSession(browser, 'demo');
    const page = session.page;
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.reload();
    await selectChannelGeral(page);

    const body = `layout-frame-${viewport.name}-${Date.now()}`;
    await page.locator('vc-composer textarea').first().fill(body);
    await page.getByRole('button', { name: sendButtonName }).click();
    const bubble = page.locator('article[data-message-id][data-status="persisted"]').filter({ hasText: body }).last();
    await expect(bubble).toBeVisible();
    await clickMessageToolbarButton(bubble, /Abrir thread/i);
    await expect(page.locator('vc-thread-panel')).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await expectBoxInside(page, 'vc-thread-panel');

    for (const path of [
      '/admin/overview',
      '/admin/developer',
      '/admin/capacity',
      '/admin/plugins',
      '/compliance',
      '/dev/design-system',
    ]) {
      await page.goto(path);
      await expect(page.locator('h1, h2').first()).toBeVisible();
      await expectNoHorizontalOverflow(page);
    }

    await session.context.close();
  });
}
