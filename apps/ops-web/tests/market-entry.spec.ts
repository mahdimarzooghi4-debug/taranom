import { test, expect } from '@playwright/test';

const paths = ['/markets/individuals','/markets/support','/markets/corporate'];

test('MARKET-01 three approved audiences displayed from real .NET status, not fake enrollment', async ({page}) => {
  const outside: string[] = [];
  page.on('request',req => {
    if (new URL(req.url()).origin !== 'http://127.0.0.1:4318') outside.push(req.url());
  });
  await page.goto('/markets');
  await expect(page.getByRole('status')).toContainText('سه بازار برنامه‌ریزی شده‌اند');
  await expect(page.locator('.market-tile')).toHaveCount(3);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
  await expect(page.getByRole('navigation', {name:'مسیرهای سه بازار'}).getByRole('link')).toHaveCount(4);
  await expect(page.getByText('ثبت‌نام، پرداخت و فعال‌سازی: غیرفعال')).toHaveCount(3);
  expect(outside).toEqual([]);
});

test('MARKET-02 each audience has its own real URL and sponsor privacy boundary', async ({page}) => {
  for (const [idx, path] of paths.entries()) {
    await page.goto(path);
    await expect(page.locator('.market-tile')).toHaveCount(1);
    await expect(page.getByRole('status')).toContainText('BLOCKED_UNVERIFIED');
    await expect(page.getByRole('navigation').locator('[aria-current="page"]')).toHaveAttribute('href', path);
    if (idx !== 0)
      await expect(page.getByText('تأمین حق اشتراک توسط حامی فقط بر اساس قرارداد معتبر')).toBeVisible();
    await expect(page.getByText('حامی هیچ حق خودکاری برای دسترسی به پرونده سلامت')).toBeVisible();
  }
});

test('MARKET-03 unreadable backend never invents source-based market availability', async ({page}) => {
  await page.route('**/internal/ops/v1/market-entry',route => route.abort());
  await page.goto('/markets');
  await expect(page.getByRole('status')).toContainText('منبع وضعیت قابل تأیید نیست');
  await expect(page.locator('.market-tile')).toHaveCount(0);
  await expect(page.getByRole('navigation',{name:'مسیرهای سه بازار'})).toHaveCount(0);
});

test('MARKET-04 forged authority and browser-supplied actor cannot enable membership', async ({request}) => {
  const normal=await request.get('/internal/ops/v1/market-entry');
  expect(normal.status()).toBe(200);
  const v=await normal.json();
  expect(v.markets).toHaveLength(3);
  expect(v.onboardingEnabled).toBe(false);
  expect(v.paymentsEnabled).toBe(false);
  expect(v.nationalReleaseAllowed).toBe(false);
  for (const headers of [{Host:'evil.example:4318'},{Forwarded:'host=evil.example'}]) {
    const denied=await request.get('/internal/ops/v1/market-entry',{headers,failOnStatusCode:false});
    expect(denied.status()).toBe(403);
  }
  expect((await request.get('/internal/ops/v1/market-entry?actor=admin')).status()).toBe(400);
  expect((await request.post('/internal/ops/v1/market-entry',{data:{onboardingEnabled:true}})).status()).toBe(405);
  expect((await (await request.get('/internal/ops/v1/market-entry')).json()).releaseAllowed).toBe(false);
});

test('MARKET-05 unknown market paths are 404, no fake login, no hidden payments', async ({request,page}) => {
  for (const path of ['/markets/login','/markets/real/onboard','/markets/x','/api/v1/payments']) {
    const response=await request.get(path);
    expect(response.status(),path).toBe(404);
    expect((await response.text()).toLowerCase()).not.toContain('<html');
  }
  await page.goto('/markets/support');
  await expect(page.locator('html')).toHaveAttribute('dir','rtl');
  await expect(page.locator('html')).toHaveAttribute('lang','fa');
  await expect(page.getByRole('textbox')).toHaveCount(0);
  await expect(page.getByRole('button',{name:/ثبت‌نام|پرداخت|شروع عضویت/})).toHaveCount(0);
  await expect(page.getByRole('status')).toContainText('BLOCKED_UNVERIFIED');
});
