import { test, expect } from '@playwright/test';

test('WEB-01: direct member URL does not show an authenticated member or private data', async ({ page }) => {
  await page.goto('/member');
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeVisible();
  await expect(page.getByText('بدون حساب واقعی، بدون داده درمانی و بدون اتصال مالی')).toBeVisible();
  await expect(page.getByRole('status')).toContainText('متصل نیست');
  await expect(page.getByText('اتصال عضو هنوز برقرار نیست')).toBeVisible();
});

test('WEB-04: no login or successful identity is fabricated', async ({ page }) => {
  await page.goto('/member/membership');
  await expect(page.getByRole('heading', { level: 1, name: 'عضویت من' })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('متصل نیست');
  await expect(page.getByText(/فقط پس از اتصال به سرویس معتبر و احراز هویت/)).toBeVisible();
  await expect(page.getByRole('button', { name: /ورود|احراز هویت/ })).toHaveCount(0);
});

test('WEB-05: wallet is unavailable without fictitious zero or live values', async ({ page }) => {
  await page.goto('/member/credit');
  await expect(page.getByRole('heading', { name: 'اعتبار درمان', exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('هیچ عدد پیش‌فرضی');
  await expect(page.getByText(/موجودی شما\s*[:：]\s*[۰-۹0-9]/)).toHaveCount(0);
  await expect(page.getByRole('button', { name: /برداشت|انتقال اعتبار|پرداخت/ })).toHaveCount(0);
});

test('WEB-06: provider and coverage pages never imply live approval', async ({ page }) => {
  for (const route of ['/member/care', '/member/billing']) {
    await page.goto(route);
    await expect(page.getByRole('status')).toContainText('متصل نیست');
    await expect(page.getByText('بدون حساب واقعی، بدون داده درمانی و بدون اتصال مالی')).toBeVisible();
  }
});

test('WEB-07: estimates are explicitly nonbinding and separate from invoices', async ({ page }) => {
  await page.goto('/member/costs');
  await expect(page.getByRole('status')).toContainText('تعهد مالی نیست');
  await expect(page.getByRole('status')).toContainText('صورتحساب قطعی');
});

test('WEB-08: emergency warning is visible without authentication or payment', async ({ page }) => {
  await page.goto('/member/care');
  await expect(page.getByText(/در وضعیت اضطراری، دریافت کمک پزشکی/)).toBeVisible();
});

test('WEB-09: separate consent purposes, without assumed research permission', async ({ page }) => {
  await page.goto('/member/consents');
  await expect(page.getByRole('status')).toContainText('پژوهش هوش مصنوعی مستقل‌اند');
});

test('WEB-10: navigation creates no real account storage or requests to private APIs', async ({ page }) => {
  const calls: string[] = [];
  page.on('request', req => { if (/\/api\/|\/oauth\/|\/token(?:\?|$)/i.test(req.url())) calls.push(req.url()); });
  await page.goto('/member');
  await page.getByRole('link', { name: 'عضویت من' }).click();
  await page.getByRole('link', { name: 'سوابق سلامت' }).click();
  expect(calls).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage),
  }))).toEqual({ local: [], session: [] });
});

test('WEB-11: Persian RTL semantics and keyboard skip link', async ({ page }) => {
  await page.goto('/member');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('navigation', { name: 'بخش‌های وب‌اپ عضو' })).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'پرش به محتوای اصلی' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();
});

test('WEB-11: mobile navigation is accessible and collapses after selecting a route', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/member');
  const trigger = page.getByRole('button', { name: 'باز کردن فهرست بخش‌ها' });
  await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  await trigger.click();
  await expect(page.getByRole('button', { name: 'بستن فهرست بخش‌ها' })).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('link', { name: 'پشتیبانی و شکایات' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'پشتیبانی و شکایات' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'باز کردن فهرست بخش‌ها' })).toHaveAttribute('aria-expanded', 'false');
});

test('all nine member routes are shell-only and unknown routes render safe 404', async ({ page }) => {
  const routes = ['/member', '/member/membership', '/member/credit', '/member/consents', '/member/care',
    '/member/records', '/member/costs', '/member/billing', '/member/support'];
  for (const route of routes) {
    await page.goto(route);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('status')).toContainText('متصل نیست');
    await expect(page.getByRole('navigation', { name: 'بخش‌های وب‌اپ عضو' })
      .locator('a[aria-current="page"]')).toHaveCount(1);
  }
  await page.goto('/member/not-a-page');
  await expect(page.getByRole('heading', { name: 'صفحه پیدا نشد' })).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);
});

test('privacy: shell never requests external fonts, analytics or remote integrations', async ({ page }) => {
  const offsite: string[] = [];
  page.on('request', req => {
    const url = new URL(req.url());
    if (url.protocol !== 'data:' && url.origin !== 'http://127.0.0.1:4174') offsite.push(req.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'سوابق سلامت' }).click();
  await expect(page.getByRole('status')).toContainText('متصل نیست');
  expect(offsite).toEqual([]);
});

test('review: browser runs the built Vite artifact, never its development HMR scripts', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', request => requested.push(new URL(request.url()).pathname));
  await page.goto('/member');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  expect(requested.some(path => path.startsWith('/assets/index-') && path.endsWith('.js'))).toBe(true);
  expect(requested.some(path => path.startsWith('/@vite') || path.startsWith('/src/'))).toBe(false);
});

test('review: router sets heading focus after navigation and browser back/forward', async ({ page }) => {
  await page.goto('/member');
  await page.getByRole('link', { name: 'عضویت من' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'عضویت من' })).toBeFocused();
  await page.getByRole('link', { name: 'اعتبار درمان' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'اعتبار درمان' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'عضویت من' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { level: 1, name: 'اعتبار درمان' })).toBeFocused();
  await page.goto('/member/not-a-page');
  await page.getByRole('link', { name: 'بازگشت به نمای کلی' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
});

test('review: Escape closes mobile navigation and restores toggle focus', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/member');
  const open = page.getByRole('button', { name: 'باز کردن فهرست بخش‌ها' });
  await open.click();
  const close = page.getByRole('button', { name: 'بستن فهرست بخش‌ها' });
  await expect(close).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(open).toHaveAttribute('aria-expanded', 'false');
  await expect(open).toBeFocused();
  await expect(page.getByRole('navigation', { name: 'بخش‌های وب‌اپ عضو' })).toBeHidden();
});

test('WEB-12: no synthetic member identity provider can be enabled by query flags', async ({ page }) => {
  await page.goto('/member?demo=true&role=member&authenticated=true');
  await expect(page.getByRole('status')).toContainText('متصل نیست');
  await expect(page.getByText('بدون حساب واقعی، بدون داده درمانی و بدون اتصال مالی')).toBeVisible();
  await expect(page.getByRole('button', { name: /خروج از حساب/ })).toHaveCount(0);
});

test('MEMBER-CSP-01: disconnected built preview keeps policy and renders', async ({ page }) => {
  await page.goto('/member');
  const p = await page.locator('meta[http-equiv="Content-Security-Policy"]').getAttribute('content');
  expect(p).toContain("default-src 'none'");
  expect(p).toContain("script-src 'self'");
  expect(p).toContain("style-src 'self'");
  expect(p).toContain("connect-src 'none'");
  expect(p).toContain("form-action 'none'");
  expect(p).toContain("base-uri 'none'");
  await expect(page.locator('meta[name="referrer"]')).toHaveAttribute('content', 'no-referrer');
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex,nofollow,noarchive');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('MEMBER-CSP-02: policy blocks same-origin private API and remote connections before request', async ({ page }) => {
  const outgoing: string[] = [];
  page.on('request', request => {
    if (request.url().includes('blocked-csp-')) outgoing.push(request.url());
  });
  await page.goto('/member');
  const blocked = await page.evaluate(async () => {
    const attempt = async (url: string): Promise<boolean> => {
      try { await fetch(url, { method: 'GET' }); return false; }
      catch { return true; }
    };
    return [
      await attempt('/api/blocked-csp-local'),
      await attempt('https://example.com/blocked-csp-external'),
    ];
  });
  expect(blocked).toEqual([true, true]);
  expect(outgoing).toEqual([]);
});

test('MEMBER-CSP-03: injected inline script is rejected without breaking the preview', async ({ page }) => {
  await page.goto('/member');
  await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'window.__taranom_untrusted_inline_executed = true;';
    document.body.appendChild(script);
  });
  const executed = await page.evaluate(() => Boolean(
    (window as Window & { __taranom_untrusted_inline_executed?: boolean })
      .__taranom_untrusted_inline_executed));
  expect(executed).toBe(false);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});
