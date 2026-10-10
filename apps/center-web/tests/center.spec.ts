import { test, expect } from '@playwright/test';

const routes = [
  '/center', '/center/departments', '/center/doctors',
  '/center/qualifications', '/center/audit',
] as const;

test('CENTER-01: all five routes belong to one disconnected center panel', async ({ page }) => {
  for (const route of routes) {
    await page.goto(route);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('note')).toContainText('بدون پزشک نمونه');
    await expect(page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' })
      .locator('a[aria-current="page"]')).toHaveCount(1);
    await expect(page.getByText('مدیر مرکز هنوز احراز نشده است')).toBeVisible();
  }
});

test('CENTER-02: chain hospital is one multi-department center, no network portal', async ({ page }) => {
  await page.goto('/center/departments');
  await expect(page.getByText('بیمارستان زنجیره‌ای نیز در این مدل یک مرکز با بخش‌های مختلف است.')).toBeVisible();
  await expect(page.getByRole('link', { name: /شبکه درمانی|مدیریت شبکه/ })).toHaveCount(0);
  expect(routes).not.toContain('/center/network' as (typeof routes)[number]);
});

test('CENTER-03: no fictitious doctor, patient or department record on direct link', async ({ page }) => {
  for (const route of ['/center/doctors', '/center/departments', '/center/audit']) {
    await page.goto(route);
    await expect(page.getByRole('status', { name: 'بخش متصل نیست' })).toBeVisible();
    await expect(page.getByRole('status')).toContainText('نبود اتصال به معنی');
  }
  // The educational notice may legitimately say "بدون پزشک نمونه".
  // Prove no real-looking record row or invented named clinician exists.
  await expect(page.locator('table tbody tr, [data-record="doctor"], [data-record="department"]')).toHaveCount(0);
  await expect(page.getByText(/دکتر رضایی|بیمار نمونه|۵۰ پزشک/)).toHaveCount(0);
});

test('CENTER-04: defining department is a non-mutating locked workflow', async ({ page }) => {
  await page.goto('/center/departments');
  await page.getByRole('button', { name: '+ تعریف بخش' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'تعریف بخش جدید' })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'ثبت در سامانه — غیرفعال' })).toBeDisabled();
  await expect(dialog).toContainText('هیچ تغییری ذخیره نمی‌شود');
  await expect(dialog.getByRole('textbox')).toHaveCount(0);
  await dialog.getByRole('button', { name: 'بازگشت به پنل' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('button', { name: '+ تعریف بخش' })).toBeFocused();
});

test('CENTER-05: doctor action never implies clinical qualification', async ({ page }) => {
  await page.goto('/center/doctors');
  await page.getByRole('button', { name: '+ معرفی پزشک' }).click();
  await expect(page.getByRole('dialog')).toContainText('تأیید صلاحیت');
  await expect(page.getByRole('dialog').getByRole('button', { name: /ثبت در سامانه/ })).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '+ معرفی پزشک' })).toBeFocused();
});

test('CENTER-06: department assignment and ending affiliation are also locked', async ({ page }) => {
  await page.goto('/center/doctors');
  for (const label of ['انتساب پزشک به بخش', 'پایان همکاری پزشک']) {
    await page.getByRole('button', { name: label }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.getByRole('dialog').getByRole('button', { name: /ثبت در سامانه/ })).toBeDisabled();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }
});

test('CENTER-07: qualification is independently gated from center affiliation', async ({ page }) => {
  await page.goto('/center/qualifications');
  await expect(page.getByRole('heading', { name: 'ثبت وابستگی ≠ تأیید صلاحیت' })).toBeVisible();
  await expect(page.getByText('احراز هویت و پروانه حرفه‌ای')).toBeVisible();
  await expect(page.getByText('احراز قرارداد و حدود خدمت')).toBeVisible();
  await expect(page.getByText('مجوز دسترسی درمانی')).toBeVisible();
  await expect(page.getByText(/پزشک صرفاً با تعریف در مرکز/)).toBeVisible();
  await expect(page.getByRole('button', { name: /تأیید|فعال‌سازی|مجوز/ })).toHaveCount(0);
});

test('CENTER-08: browser-supplied role and tenant cannot activate center manager', async ({ page }) => {
  await page.goto('/center/doctors?role=admin&authorized=true&center=test:other&verified=true');
  await expect(page.getByText('مدیر مرکز هنوز احراز نشده است')).toBeVisible();
  await page.getByRole('button', { name: '+ معرفی پزشک' }).click();
  await expect(page.getByRole('dialog').getByRole('button', { name: /ثبت در سامانه/ })).toBeDisabled();
  await expect(page.getByRole('link', { name: /شبکه درمانی/ })).toHaveCount(0);
});

test('CENTER-09: navigation and browser history focus section heading', async ({ page }) => {
  await page.goto('/center');
  await page.getByRole('link', { name: /بخش‌های مرکز/ }).first().click();
  await expect(page.getByRole('heading', { level: 1, name: 'بخش‌های مرکز' })).toBeFocused();
  await page.getByRole('link', { name: /پزشکان وابسته/ }).first().click();
  await expect(page.getByRole('heading', { level: 1, name: 'پزشکان وابسته' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'بخش‌های مرکز' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { level: 1, name: 'پزشکان وابسته' })).toBeFocused();
});

test('CENTER-10: mobile menu toggles, Escape closes and returns focus', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/center');
  const menu = page.getByRole('button', { name: 'باز کردن فهرست' });
  await expect(menu).toHaveAttribute('aria-expanded', 'false');
  await menu.click();
  await expect(page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' })).toBeHidden();
  await expect(menu).toBeFocused();
  await menu.click();
  await page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' }).getByRole('link', { name: /بررسی صلاحیت/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'بررسی صلاحیت' })).toBeVisible();
  await expect(menu).toHaveAttribute('aria-expanded', 'false');
});

test('CENTER-11: Persian RTL accessible skip link reaches main', async ({ page }) => {
  await page.goto('/center');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'پرش به محتوای اصلی' });
  await expect(skip).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#center-main')).toBeFocused();
});

test('CENTER-12: no private APIs, third-party requests or browser storage mutations', async ({ page }) => {
  const offsite: string[] = [];
  const privateCalls: string[] = [];
  page.on('request', req => {
    const url = new URL(req.url());
    if (url.protocol !== 'data:' && url.origin !== 'http://127.0.0.1:4184') offsite.push(req.url());
    if (/\/api\/|\/oauth\/|\/token(?:\?|$)/i.test(url.pathname)) privateCalls.push(req.url());
  });
  await page.goto('/center');
  await page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' }).getByRole('link', { name: 'پزشکان وابسته' }).click();
  await page.getByRole('button', { name: '+ معرفی پزشک' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'بازگشت به پنل' }).click();
  expect(offsite).toEqual([]);
  expect(privateCalls).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
  }))).toEqual({ local: [], session: [] });
});

test('CENTER-13: tests use built Vite preview and do not load development HMR', async ({ page }) => {
  const paths: string[] = [];
  page.on('request', r => paths.push(new URL(r.url()).pathname));
  await page.goto('/center');
  await expect(page.getByRole('heading', { level: 1, name: 'نمای کلی مرکز' })).toBeVisible();
  expect(paths.some(p => p.startsWith('/assets/index-') && p.endsWith('.js'))).toBe(true);
  expect(paths.some(p => p.startsWith('/@vite') || p.startsWith('/src/'))).toBe(false);
});

test('CENTER-14: unknown paths render safe 404 without private data', async ({ page }) => {
  await page.goto('/center/network');
  await expect(page.getByRole('heading', { name: 'صفحه مرکز پیدا نشد' })).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);
  await page.getByRole('link', { name: 'بازگشت به نمای کلی مرکز' }).click();
  await expect(page.getByRole('heading', { name: 'نمای کلی مرکز' })).toBeFocused();
});
