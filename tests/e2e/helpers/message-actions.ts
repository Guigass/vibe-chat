import { expect, type Locator, type Page } from '@playwright/test';

/** Reveal B-163 hover toolbar then open the CDK "more" menu. */
export async function openMessageMoreMenu(bubble: Locator, page: Page): Promise<void> {
  await bubble.hover();
  const more = bubble.getByRole('button', { name: /^Mais opções$/i });
  await expect(more).toBeVisible();
  await more.click();
  await expect(page.getByRole('menu')).toBeVisible();
}

export async function clickMessageMenuItem(page: Page, name: RegExp | string): Promise<void> {
  await page.getByRole('menuitem', { name }).click();
}

/** Reveal toolbar and click a primary action (Responder / Reagir com …). */
export async function clickMessageToolbarButton(
  bubble: Locator,
  name: RegExp | string,
): Promise<void> {
  await bubble.hover();
  const button = bubble.getByRole('button', { name });
  await expect(button).toBeVisible();
  await button.click();
}
