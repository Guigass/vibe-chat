import { expect, test, type Page } from "@playwright/test";
import {
  AUTH_MODE,
  openDirectMessage,
  openUserSession,
  selectChannelGeral,
} from "../helpers/auth";
import {
  clickMessageMenuItem,
  openMessageMoreMenu,
} from "../helpers/message-actions";

async function removeGeneratedPins(page: Page): Promise<void> {
  const pinBar = page.getByTestId("pin-bar");
  if (!(await pinBar.isVisible())) return;
  await pinBar.click();
  const generated = page
    .locator(".pins-panel__item")
    .filter({ hasText: "pin-anchor-" });
  while ((await generated.count()) > 0) {
    const before = await generated.count();
    await generated
      .first()
      .getByRole("button", { name: /^Desafixar$/i })
      .click();
    await expect(generated).toHaveCount(before - 1);
  }
  await page.getByRole("button", { name: /^Fechar painel$/i }).click();
}

const tallTimelineCss = `
  .timeline__list::before,
  .timeline__list::after {
    content: '';
    display: block;
    flex: 0 0 1400px;
    min-height: 1400px;
  }
`;

async function sendMessage(page: Page, body: string) {
  const composer = page.locator("vc-composer textarea").first();
  await composer.fill(body);
  await page.getByRole("button", { name: /^Enviar$/i }).click();
  const bubble = page
    .locator("article[data-message-id]")
    .filter({ hasText: body })
    .last();
  await expect(bubble).toBeVisible();
  return bubble;
}

test.describe(`stable timeline anchoring (${AUTH_MODE})`, () => {
  test("opening a conversation without unread divider settles at the latest content", async ({
    browser,
  }) => {
    const session = await openUserSession(browser, "alice");
    const { page } = session;
    await selectChannelGeral(page);

    await openDirectMessage(page, "Bob");
    await page.addStyleTag({ content: tallTimelineCss });

    await selectChannelGeral(page);
    await expect
      .poll(() =>
        page
          .locator(".timeline")
          .evaluate(
            (element) =>
              element.scrollHeight - element.scrollTop - element.clientHeight,
          ),
      )
      .toBeLessThan(5);

    await session.context.close();
  });

  test("jumping to a pin centers the rendered target and leaves scrolling usable", async ({
    browser,
  }) => {
    test.skip(
      AUTH_MODE === "demo",
      "Pin persistence requires API (devauth|oidc)",
    );
    const session = await openUserSession(browser, "alice");
    const { page } = session;
    await selectChannelGeral(page);
    await removeGeneratedPins(page);
    const body = `pin-anchor-${Date.now()}`;
    const bubble = await sendMessage(page, body);

    await openMessageMoreMenu(bubble, page);
    await clickMessageMenuItem(page, /^Fixar$/i);
    await expect(page.getByTestId("pin-bar")).toBeVisible();
    await page.addStyleTag({ content: tallTimelineCss });
    await page.locator(".timeline").evaluate((element) => {
      element.scrollTop = element.scrollHeight;
    });

    await page.getByTestId("pin-bar").click();
    await page
      .getByRole("button", { name: /^Ir até$/i })
      .first()
      .click();
    await expect(page.locator(".shell__context")).toHaveCount(0);
    await expect(bubble).toBeInViewport();
    await expect(bubble).toHaveClass(/vc-msg--highlight/);

    const scrollMetrics = await page
      .locator(".timeline")
      .evaluate((element) => {
        const anchored = element.scrollTop;
        element.scrollTop = element.scrollHeight;
        const bottom = element.scrollTop;
        element.scrollTop = 0;
        const top = element.scrollTop;
        return { anchored, bottom, top };
      });
    expect(scrollMetrics.anchored).toBeGreaterThan(0);
    expect(scrollMetrics.bottom).toBeGreaterThan(scrollMetrics.anchored);
    expect(scrollMetrics.top).toBe(0);

    await page.getByTestId("pin-bar").click();
    await page
      .locator(".pins-panel__item")
      .filter({ hasText: body })
      .getByRole("button", { name: /^Desafixar$/i })
      .click();
    await session.context.close();
  });
});
