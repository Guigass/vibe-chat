import type { Browser, BrowserContext, Page } from '@playwright/test';

export type AuthMode = 'demo' | 'devauth' | 'oidc';
export type DemoUser = 'alice' | 'bob' | 'demo';

export const AUTH_MODE = (process.env.E2E_AUTH_MODE ?? 'demo') as AuthMode;
export const API_BASE_URL = process.env.API_BASE_URL ?? 'http://localhost:5080';
export const WEB_BASE_URL = process.env.WEB_BASE_URL ?? 'http://localhost:4200';

const DEMO_PROFILES: Record<DemoUser, { id: string; name: string; email: string; roles: string[]; tenantId: string }> = {
  alice: {
    id: '44444444-4444-4444-4444-444444444444',
    name: 'Alice',
    email: 'alice@vibechat.local',
    roles: ['user'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
  bob: {
    id: '55555555-5555-5555-5555-555555555555',
    name: 'Bob',
    email: 'bob@vibechat.local',
    roles: ['user'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
  demo: {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'Demo',
    email: 'demo@vibechat.local',
    roles: ['user', 'admin'],
    tenantId: '11111111-1111-1111-1111-111111111111',
  },
};

/** Rewrite /api/X → /api/v1/X and attach DevAuth header for API + hub calls. */
export async function attachDevAuth(context: BrowserContext, user: DemoUser): Promise<void> {
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
}

export async function loginAs(
  page: Page,
  user: DemoUser,
  mode: AuthMode = AUTH_MODE,
): Promise<void> {
  if (mode === 'demo') {
    await page.goto('/login');
    await page.getByRole('button', { name: /Explorar demo local/i }).click();
    await page.waitForURL(/\/app/);
    return;
  }

  if (mode === 'devauth') {
    const profile = DEMO_PROFILES[user];
    await page.addInitScript((p) => {
      localStorage.setItem('vc.demo-auth', JSON.stringify(p));
    }, profile);
    await page.goto('/app');
    await page.waitForURL(/\/app/);
    return;
  }

  // oidc — Keycloak account forms vary by theme; use demo emails from realm export
  await page.goto('/login');
  await page.getByRole('button', { name: /Entrar com Keycloak/i }).click();
  await page.waitForURL(/realms\/vibechat|login/i, { timeout: 30_000 });
  const email = DEMO_PROFILES[user].email;
  const password = process.env.E2E_KEYCLOAK_PASSWORD ?? 'Demo123!';
  const userField = page.locator('#username, input[name="username"]').first();
  const passField = page.locator('#password, input[name="password"]').first();
  await userField.fill(email);
  await passField.fill(password);
  await page.locator('#kc-login, button[type="submit"]').first().click();
  await page.waitForURL(/\/app/, { timeout: 60_000 });
}

export async function openUserSession(
  browser: Browser,
  user: DemoUser,
  mode: AuthMode = AUTH_MODE,
): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext();
  if (mode === 'devauth') {
    await attachDevAuth(context, user);
  }
  const page = await context.newPage();
  await loginAs(page, user, mode);
  return { context, page };
}

export async function selectChannelGeral(page: Page): Promise<void> {
  const channel = page.getByText(/^#?\s*geral$/i).first();
  if (await channel.isVisible().catch(() => false)) {
    await channel.click().catch(() => undefined);
  }
  await page.getByRole('heading', { name: /geral/i }).waitFor({ state: 'visible', timeout: 20_000 });
}
