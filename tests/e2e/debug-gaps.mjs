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

const channel = alice.page.getByText(/^#?\s*geral$/i).first();
if (await channel.isVisible().catch(() => false)) await channel.click().catch(() => undefined);
await alice.page.getByRole('heading', { name: /geral/i }).waitFor({ state: 'visible', timeout: 20000 });
await alice.page.waitForTimeout(1000);

const gapInfo = await alice.page.evaluate(() => {
  const items = Array.from(document.querySelectorAll('.timeline__item, .timeline__day, .timeline__unread'));
  const out = [];
  let prevBottom = null;
  for (const el of items) {
    const rect = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    out.push({
      tag: el.className,
      top: Math.round(rect.top),
      bottom: Math.round(rect.bottom),
      gapFromPrev: prevBottom !== null ? Math.round(rect.top - prevBottom) : null,
      marginTop: cs.marginTop,
      text: el.textContent?.trim().slice(0, 30),
    });
    prevBottom = rect.bottom;
  }
  return out;
});
console.log(JSON.stringify(gapInfo, null, 2));

await alice.context.close();
await browser.close();
