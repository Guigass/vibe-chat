import { chromium } from '@playwright/test';

const WEB = process.env.WEB_BASE_URL ?? 'http://localhost:4200';

async function openUser(browser, user) {
  const context = await browser.newContext();
  await context.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const headers = { ...request.headers(), 'X-Dev-User': user };
    const isApi = url.pathname.startsWith('/api/') && !url.pathname.startsWith('/api/v1/');
    const isHub = url.pathname.startsWith('/hubs/');
    if (isApi) {
      url.pathname = url.pathname.replace(/^\/api\//, '/api/v1/');
      await route.continue({ url: url.toString(), headers });
      return;
    }
    if (isHub || url.pathname.startsWith('/api/v1/')) {
      await route.continue({ headers });
      return;
    }
    await route.continue();
  });
  const page = await context.newPage();
  await page.addInitScript((name) => {
    localStorage.setItem('vc.dev-auth', name);
    localStorage.removeItem('vc.demo-auth');
  }, user);
  await page.goto(WEB + '/app');
  await page.waitForURL(/\/app/);
  await page.waitForLoadState('networkidle').catch(() => undefined);
  return { context, page };
}

const browser = await chromium.launch();
const alice = await openUser(browser, 'alice');
const bob = await openUser(browser, 'bob');

// Select #geral on both
for (const { page } of [alice, bob]) {
  const channel = page.getByText(/^#?\s*geral$/i).first();
  if (await channel.isVisible().catch(() => false)) await channel.click().catch(() => undefined);
  await page.getByRole('heading', { name: /geral/i }).waitFor({ state: 'visible', timeout: 20000 });
}

await alice.page.waitForTimeout(1000);

// Send 3 quick messages from alice to trigger grouping
for (let i = 0; i < 3; i++) {
  const composer = alice.page.locator('textarea').first();
  await composer.fill(`debug-msg-${i}-${Date.now()}`);
  await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
  await alice.page.waitForTimeout(400);
}

await alice.page.waitForTimeout(1500);
await alice.page.screenshot({ path: 'tests/e2e/debug-alice-grouping.png', fullPage: false });

// Inspect hover-time positioning for a grouped message (alice, mine)
const hoverInfo = await alice.page.evaluate(() => {
  const bubbles = Array.from(document.querySelectorAll('vc-message-bubble'));
  const results = [];
  for (const b of bubbles.slice(-3)) {
    const article = b.querySelector('article');
    const hover = b.querySelector('.vc-msg__hover-time');
    const rect = article?.getBoundingClientRect();
    const hrect = hover?.getBoundingClientRect();
    results.push({
      hasHover: !!hover,
      articleRect: rect ? { left: rect.left, right: rect.right, width: rect.width } : null,
      hoverRect: hrect ? { left: hrect.left, right: hrect.right, width: hrect.width } : null,
      classes: article?.className,
    });
  }
  return results;
});
console.log('HOVER INFO (alice, mine, grouped)', JSON.stringify(hoverInfo, null, 2));

// Now check unread badge on bob's side (bob did not send anything to himself; bob should see unread if he switches channel)
// Switch bob to another channel then back to see if unread badge appears when alice sends more.
const channels = await bob.page.locator('[class*="vc-channel"]').allTextContents();
console.log('BOB CHANNEL LIST', channels);

// find a channel that's not 'geral' to switch bob to
const otherChannelBtn = bob.page.locator('button:has-text("design-system"), button:has-text("incidentes")').first();
if (await otherChannelBtn.isVisible().catch(() => false)) {
  await otherChannelBtn.click();
  await bob.page.waitForTimeout(500);

  // alice sends another message while bob is on a different channel
  const composer = alice.page.locator('textarea').first();
  await composer.fill(`debug-unread-${Date.now()}`);
  await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
  await alice.page.waitForTimeout(2000);

  const badgeInfo = await bob.page.evaluate(() => {
    const items = Array.from(document.querySelectorAll('[class*="vc-channel"]'));
    return items.map((el) => ({ text: el.textContent?.trim(), hasBadge: !!el.querySelector('vc-badge') }));
  });
  console.log('BOB BADGE INFO AFTER ALICE MSG', JSON.stringify(badgeInfo, null, 2));
} else {
  console.log('No other channel button found for bob to switch to');
}

await bob.page.screenshot({ path: 'tests/e2e/debug-bob-sidebar.png', fullPage: false });

await alice.context.close();
await bob.context.close();
await browser.close();
