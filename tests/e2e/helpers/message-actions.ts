import { expect, type Locator, type Page } from "@playwright/test";

/** Reveal B-163 hover toolbar then open the CDK "more" menu. */
export async function openMessageMoreMenu(
  bubble: Locator,
  page: Page,
): Promise<void> {
  await bubble.evaluate((element) =>
    element.scrollIntoView({ block: "center" }),
  );
  await page.waitForTimeout(50);
  await bubble.hover();
  const actions = bubble.getByRole("button", { name: /^Ações da mensagem$/i });
  await expect(actions).toBeVisible();
  await actions.click();
  await expect(page.getByRole("menu")).toBeVisible();
}

export async function clickMessageMenuItem(
  page: Page,
  name: RegExp | string,
): Promise<void> {
  await page.getByRole("menuitem", { name }).click();
}

/** Reveal toolbar and click a primary action (Responder / Reagir com …). */
export async function clickMessageToolbarButton(
  bubble: Locator,
  name: RegExp | string,
): Promise<void> {
  await bubble.evaluate((element) =>
    element.scrollIntoView({ block: "center" }),
  );
  await bubble.hover();
  const inline = bubble.getByRole("button", { name });
  if (await inline.isVisible().catch(() => false)) {
    await inline.click();
    return;
  }

  const actions = bubble.getByRole("button", { name: /^Ações da mensagem$/i });
  await expect(actions).toBeVisible();
  await actions.click();

  const page = bubble.page();
  await expect(page.getByRole("menu")).toBeVisible();
  const menuItem = page.getByRole("menuitem", { name });
  if (await menuItem.isVisible().catch(() => false)) {
    await menuItem.click();
    return;
  }
  const menuButton = page.getByRole("button", { name }).last();
  await expect(menuButton).toBeVisible();
  await menuButton.click();
}
