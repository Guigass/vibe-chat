import { expect, test } from '@playwright/test';
import { API_BASE_URL, AUTH_MODE, loginAs } from '../helpers/auth';

test.describe(`admin diagnostics (${AUTH_MODE})`, () => {
  test('preflight, synthetic probe and sanitized bundle', async ({ page }) => {
    test.skip(AUTH_MODE !== 'devauth', 'Diagnostics API fixture uses DevAuth.');
    await loginAs(page, 'demo');
    const headers = { 'X-Dev-User': 'demo', 'content-type': 'application/json' };
    const workspaces = await page.request.get(`${API_BASE_URL}/api/v1/workspaces`, { headers });
    expect(workspaces.ok()).toBeTruthy();
    const list = (await workspaces.json()) as { id: string }[];
    const workspaceId = list[0]?.id;
    expect(workspaceId).toBeTruthy();

    const preflight = await page.request.get(`${API_BASE_URL}/api/v1/workspaces/${workspaceId}/diagnostics`, { headers });
    expect(preflight.ok()).toBeTruthy();
    const report = (await preflight.json()) as { verdict: string; checks: { code: string; status: string }[] };
    expect(['ready', 'degraded', 'action_required']).toContain(report.verdict);
    expect(report.checks.some((check) => check.code === 'email.configured' && check.status === 'Skipped')).toBeTruthy();

    const probe = await page.request.post(`${API_BASE_URL}/api/v1/workspaces/${workspaceId}/diagnostics/probes/email`, { headers });
    expect(probe.ok()).toBeTruthy();

    const created = await page.request.post(`${API_BASE_URL}/api/v1/workspaces/${workspaceId}/diagnostics/bundles`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { windowMinutes: 60 },
    });
    expect(created.status()).toBe(201);
    const bundle = (await created.json()) as { id: string };
    const download = await page.request.get(`${API_BASE_URL}/api/v1/workspaces/${workspaceId}/diagnostics/bundles/${bundle.id}`, { headers });
    expect(download.ok()).toBeTruthy();
    const manifest = await download.text();
    expect(manifest).toContain('vibechat.support-bundle.v1');
    expect(manifest).not.toContain('Bem-vindo');
    expect(manifest).not.toContain('Password');

    await page.goto('/admin/diagnostics');
    await expect(page.getByRole('status').first()).toBeVisible();
  });
});
