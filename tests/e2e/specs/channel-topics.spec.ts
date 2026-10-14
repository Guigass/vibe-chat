import { expect, test } from '@playwright/test';
import { API_BASE_URL, openUserSession } from '../helpers/auth';

test('forum channel lists topics and keeps the composer on the topic', async ({ browser }) => {
  const session = await openUserSession(browser, 'alice');
  const page = session.page;
  const name = `forum-${Date.now().toString(36)}`;
  const created = await page.request.post(
    `${API_BASE_URL}/api/v1/workspaces/11111111-1111-1111-1111-111111111111/channels`,
    {
      headers: { 'X-Dev-User': 'alice', 'content-type': 'application/json' },
      data: { name, type: 'Public', layout: 'Topics' },
    },
  );
  expect(created.ok()).toBeTruthy();

  await page.reload();
  await page.getByRole('button', { name: new RegExp(name, 'i') }).click();
  await expect(page.getByTestId('topic-board')).toBeVisible();

  const title = `Planejamento ${Date.now().toString(36)}`;
  await page.getByTestId('topic-title').fill(title);
  await page.getByTestId('topic-create').click();
  await expect(page.getByTestId('topic-destination')).toContainText(title);

  await page.getByTestId('topic-composer').fill('olá tópico');
  await page.getByTestId('topic-send').click();
  await expect(page.getByText('olá tópico')).toBeVisible();
  await session.context.close();
});
