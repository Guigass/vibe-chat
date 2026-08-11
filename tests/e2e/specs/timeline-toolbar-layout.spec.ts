import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  AUTH_MODE,
  openUserSession,
  selectChannelGeral,
} from "../helpers/auth";

async function sendMessage(page: Page, body: string): Promise<Locator> {
  const composer = page.locator("vc-composer textarea").first();
  await composer.fill(body);
  await page.getByRole("button", { name: /^Enviar$/i }).click();
  const bubble = page
    .locator('article[data-message-id][data-status="persisted"]')
    .filter({ hasText: body })
    .last();
  await expect(bubble).toBeVisible();
  return bubble;
}

async function expectMessageMenuStable(
  page: Page,
  bubble: Locator,
): Promise<void> {
  await bubble.scrollIntoViewIfNeeded();
  const before = await bubble.evaluate((article) => {
    const rect = article.getBoundingClientRect();
    const timeline = article.closest<HTMLElement>(".timeline");
    return {
      x: rect.x,
      y: rect.y,
      width: rect.width,
      height: rect.height,
      scrollTop: timeline?.scrollTop ?? 0,
    };
  });

  await bubble.hover();
  const toolbar = bubble.getByTestId("msg-toolbar");
  await expect(toolbar).toHaveCSS("opacity", "1");

  const result = await bubble.evaluate((article) => {
    const toolbarElement =
      article.querySelector<HTMLElement>(".vc-msg__toolbar");
    if (!toolbarElement)
      return { failures: ["toolbar-missing"], geometry: null };
    const toolbarRect = toolbarElement.getBoundingClientRect();
    const bodyRect = article
      .querySelector<HTMLElement>(".vc-msg__body")
      ?.getBoundingClientRect();
    const timelineRect = article
      .closest<HTMLElement>(".timeline")
      ?.getBoundingClientRect();
    const overlaps = (candidate: Element) => {
      const rect = candidate.getBoundingClientRect();
      return (
        toolbarRect.left < rect.right - 1 &&
        toolbarRect.right > rect.left + 1 &&
        toolbarRect.top < rect.bottom - 1 &&
        toolbarRect.bottom > rect.top + 1
      );
    };

    const failures: string[] = [];
    const mine = article.classList.contains("vc-msg--mine");
    if (
      timelineRect &&
      (toolbarRect.left < timelineRect.left ||
        toolbarRect.right > timelineRect.right)
    ) {
      failures.push("outside-timeline");
    }
    if (
      bodyRect &&
      ((mine
        ? Math.abs(toolbarRect.right - bodyRect.left)
        : Math.abs(toolbarRect.left - bodyRect.right)) > 2 ||
        toolbarRect.top < bodyRect.top ||
        toolbarRect.bottom > bodyRect.bottom)
    ) {
      failures.push("detached-from-body-edge");
    }
    for (const readable of article.querySelectorAll(
      ".vc-msg__meta, .vc-msg__content, .vc-msg__reactions, .vc-msg__group-time",
    )) {
      if (overlaps(readable)) failures.push("readable-content");
    }
    for (const otherBody of document.querySelectorAll(
      "article[data-message-id] .vc-msg__body",
    )) {
      if (!article.contains(otherBody) && overlaps(otherBody))
        failures.push("neighbor-body");
    }
    const rect = article.getBoundingClientRect();
    const timeline = article.closest<HTMLElement>(".timeline");
    return {
      failures,
      geometry: {
        x: rect.x,
        y: rect.y,
        width: rect.width,
        height: rect.height,
        scrollTop: timeline?.scrollTop ?? 0,
      },
    };
  });

  expect(result.failures).toEqual([]);
  expect(result.geometry).not.toBeNull();
  expect(result.geometry!.x).toBeCloseTo(before.x, 0);
  expect(result.geometry!.y).toBeCloseTo(before.y, 0);
  expect(result.geometry!.width).toBeCloseTo(before.width, 0);
  expect(result.geometry!.height).toBeCloseTo(before.height, 0);
  expect(result.geometry!.scrollTop).toBeCloseTo(before.scrollTop, 0);

  const trigger = bubble.getByRole("button", { name: "Ações da mensagem" });
  await expect(page.locator(".vc-msg-menu")).toHaveCount(0);
  await trigger.click();
  const menu = page.locator(".vc-msg-menu").last();
  await expect(menu).toBeVisible();
  const reactionStrip = menu.locator(".vc-msg-menu__reactions");
  await expect(reactionStrip.locator("button")).toHaveCount(9);
  const reactionStripLayout = await reactionStrip.evaluate((strip) => {
    const rect = strip.getBoundingClientRect();
    const buttons = strip.querySelectorAll("button");
    const first = buttons[0].getBoundingClientRect();
    const last = buttons[buttons.length - 1].getBoundingClientRect();
    return { leftGap: first.left - rect.left, rightGap: rect.right - last.right };
  });
  expect(reactionStripLayout.leftGap).toBeLessThanOrEqual(8);
  expect(reactionStripLayout.rightGap).toBeLessThanOrEqual(8);
  await expect
    .poll(async () =>
      menu.evaluate((element) => {
        const rect = element.getBoundingClientRect();
        return (
          rect.left >= 0 &&
          rect.right <= window.innerWidth &&
          rect.top >= 0 &&
          rect.bottom <= window.innerHeight
        );
      }),
    )
    .toBe(true);
  const menuRect = await menu.evaluate((element) => {
    const rect = element.getBoundingClientRect();
    return {
      left: rect.left,
      right: rect.right,
      top: rect.top,
      bottom: rect.bottom,
      viewportWidth: window.innerWidth,
      viewportHeight: window.innerHeight,
    };
  });
  expect(menuRect.left).toBeGreaterThanOrEqual(0);
  expect(menuRect.right).toBeLessThanOrEqual(menuRect.viewportWidth);
  expect(menuRect.top).toBeGreaterThanOrEqual(0);
  expect(menuRect.bottom).toBeLessThanOrEqual(menuRect.viewportHeight);

  const bubbleSide = await bubble.evaluate((article) => {
    const body = article.querySelector<HTMLElement>(".vc-msg__body")!;
    const rect = body.getBoundingClientRect();
    return {
      mine: article.classList.contains("vc-msg--mine"),
      left: rect.left,
      right: rect.right,
    };
  });
  const preferredSideRoom = bubbleSide.mine
    ? bubbleSide.left
    : menuRect.viewportWidth - bubbleSide.right;
  if (preferredSideRoom >= menuRect.right - menuRect.left + 4) {
    if (bubbleSide.mine) {
      expect(menuRect.right).toBeLessThanOrEqual(bubbleSide.left);
    } else {
      expect(menuRect.left).toBeGreaterThanOrEqual(bubbleSide.right);
    }
  }

  const triggerRect = await trigger.boundingBox();
  const visibleMenuRect = await menu.boundingBox();
  expect(triggerRect).not.toBeNull();
  expect(visibleMenuRect).not.toBeNull();
  await page.mouse.move(
    triggerRect!.x + triggerRect!.width / 2,
    triggerRect!.y + triggerRect!.height / 2,
  );
  await page.mouse.move(
    visibleMenuRect!.x + visibleMenuRect!.width / 2,
    visibleMenuRect!.y + 24,
    { steps: 6 },
  );
  await expect(menu).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".vc-msg-menu")).toHaveCount(0);
}

test.describe(`timeline toolbar layout (${AUTH_MODE})`, () => {
  test("long and grouped Demo messages use a stable in-bubble trigger and popover", async ({
    browser,
  }) => {
    const session = await openUserSession(browser, "demo");
    const { page } = session;
    await selectChannelGeral(page);

    const suffix = Date.now();
    const multiline = `toolbar-long-${suffix} — Lorem Ipsum is simply dummy text of the printing and typesetting industry. This message is intentionally long enough to verify that the bubble grows, wraps comfortably and keeps its actions attached to the same corner without choosing a side from mine/theirs state.`;
    const short = `tb-${suffix}`;
    const multilineBubble = await sendMessage(page, multiline);
    const desktopWidth = await multilineBubble
      .locator(".vc-msg__body")
      .evaluate((element) => element.getBoundingClientRect().width);
    expect(desktopWidth).toBeGreaterThan(640);

    await expectMessageMenuStable(page, multilineBubble);
    await multilineBubble.hover();
    await multilineBubble
      .getByRole("button", { name: "Ações da mensagem" })
      .click();
    const quickReaction = page
      .getByRole("button", { name: "Reagir com 👍" })
      .last();
    await expect(quickReaction).toBeVisible();
    await quickReaction.click();
    await expect(multilineBubble.locator(".vc-msg__reactions")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.locator(".vc-msg-menu")).toHaveCount(0);

    const shortBubble = await sendMessage(page, short);
    const groupedStack = page
      .locator(".timeline__stack")
      .filter({ hasText: multiline })
      .filter({ hasText: short });
    await expect(groupedStack).toBeVisible();
    const bodies = await Promise.all(
      [multilineBubble, shortBubble].map((bubble) =>
        bubble.locator(".vc-msg__body").evaluate((body) => {
          const rect = body.getBoundingClientRect();
          return {
            left: rect.left,
            right: rect.right,
            background: getComputedStyle(body).backgroundColor,
          };
        }),
      ),
    );
    const timelineBounds = await page
      .locator(".timeline")
      .evaluate((timeline) => {
        const rect = timeline.getBoundingClientRect();
        return { left: rect.left, right: rect.right };
      });
    expect(bodies[0].right).toBeCloseTo(bodies[1].right, 0);
    expect(bodies[0].left).toBeGreaterThan(timelineBounds.left + 24);
    expect(timelineBounds.right - bodies[0].right).toBeLessThanOrEqual(24);
    expect(bodies.every((body) => body.background !== "rgba(0, 0, 0, 0)")).toBe(
      true,
    );

    await expectMessageMenuStable(page, multilineBubble);
    await expectMessageMenuStable(page, shortBubble);

    const aliceSession = await openUserSession(browser, "alice");
    await selectChannelGeral(aliceSession.page);
    const receivedText = `toolbar-theirs-${suffix}`;
    await sendMessage(aliceSession.page, receivedText);
    const receivedBubble = page
      .locator('article[data-message-id][data-status="persisted"]')
      .filter({ hasText: receivedText })
      .last();
    await expect(receivedBubble).toBeVisible();
    await expectMessageMenuStable(page, receivedBubble);

    await page.setViewportSize({ width: 900, height: 720 });
    await expectMessageMenuStable(page, multilineBubble);
    await expectMessageMenuStable(page, shortBubble);
    await expectMessageMenuStable(page, receivedBubble);

    await page.setViewportSize({ width: 390, height: 720 });
    await expectMessageMenuStable(page, multilineBubble);
    await expectMessageMenuStable(page, shortBubble);
    await expectMessageMenuStable(page, receivedBubble);
    await aliceSession.context.close();
    await session.context.close();
  });
});
