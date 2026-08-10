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

for (const { page } of [alice, bob]) {
  const channel = page.getByText(/^#?\s*geral$/i).first();
  if (await channel.isVisible().catch(() => false)) await channel.click().catch(() => undefined);
  await page.getByRole('heading', { name: /geral/i }).waitFor({ state: 'visible', timeout: 20000 });
}

const demoDm = bob.page.getByText(/^Demo$/i).first();
await demoDm.click();
await bob.page.waitForTimeout(800);

const composer = alice.page.locator('textarea').first();
const body = `debug-unread2-${Date.now()}`;
await composer.fill(body);
await alice.page.getByRole('button', { name: /^Enviar$/i }).click();
await alice.page.waitForTimeout(1500);

// Reload bob's page (fresh ChannelStore.load() -> refreshUnreads())
await bob.page.reload();
await bob.page.waitForURL(/\/app/);
await bob.page.waitForLoadState('networkidle').catch(() => undefined);
await bob.page.waitForTimeout(1500);

const geralItem = await bob.page.evaluate(() => {
  const buttons = Array.from(document.querySelectorAll('button.vc-channel'));
  const geral = buttons.find((b) => b.textContent?.toLowerCase().includes('geral'));
  return geral ? { text: geral.textContent?.trim(), html: geral.outerHTML } : { text: null };
});
console.log('AFTER RELOAD — BOB SIDEBAR GERAL ITEM:', JSON.stringify(geralItem, null, 2));

await alice.context.close();
await bob.context.close();
await browser.close();
