import { expect, test } from '@playwright/test';

test('OPS-01 actual .NET HTTP serves 17 blocked dimensions into React', async ({ page }) => {
  const outside: string[] = [];
  page.on('request', req => { if (new URL(req.url()).origin !== 'http://127.0.0.1:4318') outside.push(req.url()); });
  await page.goto('/operations');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('اتصال واقعی محلی');
  await expect(page.getByRole('status').first()).toContainText('اتصال HTTP به فرایند .NET');
  await expect(page.locator('.dimensions li')).toHaveCount(17);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
  expect(outside).toEqual([]);
});
test('OPS-02 all 13 epics, 3 markets and national scope are independently filterable', async ({ page }) => {
  await page.goto('/operations');
  await expect(page.locator('.dimensions li')).toHaveCount(17);
  await page.getByLabel('نمایش').selectOption('EPIC');
  await expect(page.locator('.dimensions li')).toHaveCount(13);
  await page.getByLabel('نمایش').selectOption('MARKET');
  await expect(page.locator('.dimensions li')).toHaveCount(3);
  await page.getByLabel('نمایش').selectOption('NATIONAL');
  await expect(page.locator('.dimensions li')).toHaveCount(1);
  await expect(page.locator('.dimensions li')).toContainText(['NATIONAL-COVERAGE']);
});
test('OPS-03 transport failure never looks like empty successful data', async ({ page }) => {
  await page.route('**/internal/ops/v1/status', route => route.abort());
  await page.goto('/operations');
  await expect(page.getByRole('status')).toContainText('اتصال یا شواهد سرویس معتبر نیست');
  await expect(page.locator('.dimensions li')).toHaveCount(0);
  await expect(page.getByText('17 / 17')).toHaveCount(0);
});
test('OPS-04 immutable no-cache API rejects mutation and preserves blocked state', async ({ request }) => {
  const response = await request.get('/internal/ops/v1/status');
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  expect(response.headers()['access-control-allow-origin']).toBeUndefined();
  const payload = await response.json();
  expect(payload.releaseAllowed).toBe(false);
  expect(payload.verifiedIntegrations).toBe(false);
  expect(payload.dimensions).toHaveLength(17);
  const mutation = await request.post('/internal/ops/v1/status', { data: { releaseAllowed: true } });
  expect(mutation.status()).toBe(405);
  expect((await (await request.get('/internal/ops/v1/status')).json()).releaseAllowed).toBe(false);
});
test('OPS-05 refresh, keyboard focus and Persian RTL preserve deny-only state', async ({ page }) => {
  await page.goto('/operations');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('.dimensions li')).toHaveCount(17);
  await page.getByRole('button', { name: 'بازآزمایی اتصال محلی' }).click();
  await expect(page.locator('.dimensions li')).toHaveCount(17);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
