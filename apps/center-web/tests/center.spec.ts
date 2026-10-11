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

test('CENTER-15: action dialog traps forward and backward keyboard focus and isolates background', async ({ page }) => {
  await page.goto('/center/doctors');
  const trigger = page.getByRole('button', { name: '+ معرفی پزشک' });
  await trigger.click();
  const dialog = page.getByRole('dialog');
  const close = dialog.getByRole('button', { name: 'بازگشت به پنل' });
  await expect(dialog).toHaveAttribute('aria-modal', 'true');
  await expect(close).toBeFocused();
  await expect(page.locator('.sidebar')).toHaveAttribute('inert', '');
  await expect(page.locator('.workspace')).toHaveAttribute('inert', '');
  await expect(dialog.getByRole('button', { name: /ثبت در سامانه/ })).toBeDisabled();
  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(close).toBeFocused();
  // HTML inert blocks focus/interaction but does not mean CSS display:none.
  expect(await page.locator('#center-navigation').evaluate(
    element => element.closest('[inert]') !== null)).toBe(true);
  await expect(page).toHaveURL(/\/center\/doctors$/);
  await close.click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('.sidebar')).not.toHaveAttribute('inert', '');
  await expect(page.locator('.workspace')).not.toHaveAttribute('inert', '');
  await expect(trigger).toBeFocused();
});

test('CENTER-16: all four locked operations have keyboard containment and restore trigger focus', async ({ page }) => {
  for (const [route, name] of [
    ['/center/departments', '+ تعریف بخش'],
    ['/center/doctors', '+ معرفی پزشک'],
    ['/center/doctors', 'انتساب پزشک به بخش'],
    ['/center/doctors', 'پایان همکاری پزشک'],
  ] as const) {
    await page.goto(route);
    const trigger = page.getByRole('button', { name });
    await trigger.click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.keyboard.press('Shift+Tab');
    await expect(page.getByRole('dialog').getByRole('button', { name: 'بازگشت به پنل' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(trigger).toBeFocused();
  }
});

test('CENTER-17: browser history dismisses action modal without changing any center data', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (/\/api\/|\/oauth\/|\/token(?:\?|$)/i.test(request.url())) privateRequests.push(request.url());
  });
  await page.goto('/center');
  await page.getByRole('link', { name: /پزشکان وابسته/ }).first().click();
  await expect(page).toHaveURL(/\/center\/doctors$/);
  await page.getByRole('button', { name: '+ معرفی پزشک' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'نمای کلی مرکز' })).toBeFocused();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.workspace')).not.toHaveAttribute('inert', '');
  expect(privateRequests).toEqual([]);
});

test('CENTER-18: mobile modal closes previously open menu and keeps keyboard focus within dialog', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/center/doctors');
  const menu = page.getByRole('button', { name: 'باز کردن فهرست' });
  await menu.click();
  await expect(page.getByRole('navigation', { name: 'بخش‌های پنل مرکز' })).toBeVisible();
  // While the menu is expanded the action remains a normal visible control.
  await page.getByRole('button', { name: '+ معرفی پزشک' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(menu).toHaveAttribute('aria-expanded', 'false');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('dialog').getByRole('button', { name: 'بازگشت به پنل' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '+ معرفی پزشک' })).toBeFocused();
});

test('CENTER-CSP-01: disconnected built preview keeps policy and renders', async ({ page }) => {
  await page.goto('/center');
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

test('CENTER-CSP-02: policy blocks same-origin private API and remote connections before request', async ({ page }) => {
  const outgoing: string[] = [];
  page.on('request', request => {
    if (request.url().includes('blocked-csp-')) outgoing.push(request.url());
  });
  await page.goto('/center');
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

test('CENTER-CSP-03: injected inline script is rejected without breaking the preview', async ({ page }) => {
  await page.goto('/center');
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


test('CENTER-HTTP-01: real built-preview HTML response supplies frame protections and deny egress headers', async ({ request, page }) => {
  const response = await request.get('/center/doctors');
  expect(response.status()).toBe(200);
  const headers = response.headers();
  expect(headers['content-type']).toContain('text/html');
  const policy = headers['content-security-policy'];
  expect(policy).toContain("default-src 'none'");
  expect(policy).toContain("frame-ancestors 'none'");
  expect(policy).toContain("connect-src 'none'");
  expect(policy).toContain("script-src 'self'");
  expect(policy).toContain("style-src 'self'");
  expect(policy).toContain("form-action 'none'");
  expect(headers['x-frame-options']).toBe('DENY');
  expect(headers['x-content-type-options']).toBe('nosniff');
  expect(headers['referrer-policy']).toBe('no-referrer');
  expect(headers['permissions-policy']).toContain('camera=()');
  expect(headers['permissions-policy']).toContain('microphone=()');
  expect(headers['permissions-policy']).toContain('geolocation=()');
  expect(headers['permissions-policy']).toContain('payment=()');
  expect(headers['cross-origin-opener-policy']).toBe('same-origin');
  expect(headers['cross-origin-resource-policy']).toBe('same-origin');
  expect(headers['cache-control']).toContain('no-store');
  await page.goto('/center/doctors');
  await expect(page.getByRole('main')).toBeVisible();
});

test('CENTER-HTTP-02: built JS asset also gets no-sniff and no-store response headers', async ({ page, request }) => {
  await page.goto('/center');
  const asset = await page.locator('script[type="module"][src^="/assets/"]').getAttribute('src');
  expect(asset).toMatch(/^\/assets\/index-[a-zA-Z0-9_-]+\.js$/);
  const response = await request.get(asset!);
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('javascript');
  expect(response.headers()['x-content-type-options']).toBe('nosniff');
  expect(response.headers()['cache-control']).toContain('no-store');
  expect(response.headers()['content-security-policy']).toContain("connect-src 'none'");
});

test('CENTER-HTTP-03: Chromium cannot render an iframe of the built preview', async ({ page }) => {
  const target = 'http://127.0.0.1:4184/center';
  const delivered = await page.request.get(target);
  expect(delivered.status()).toBe(200);
  expect(delivered.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
  expect(delivered.headers()['x-frame-options']).toBe('DENY');

  // Parent is about:blank with NO CSP. Validate actual blocked child content
  // after iframe load, not fragile Chromium console error wording.
  await page.goto('about:blank');
  await page.evaluate(async url => {
    const frame = document.createElement('iframe');
    frame.title = 'Embedding prohibited';
    frame.src = url;
    const load = new Promise<void>(resolve => {
      frame.addEventListener('load', () => resolve(), { once: true });
    });
    document.body.append(frame);
    await load;
  }, target);
  await expect(page.frameLocator('iframe').locator('#root')).toHaveCount(0);
  await expect(page.frameLocator('iframe').locator('main')).toHaveCount(0);
});


test('BUNDLE-01: built center shell fetches only local route, JS and CSS (no external resources)', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', request => requested.push(request.url()));
  await page.goto('/center/doctors');
  await expect(page.getByRole('main')).toBeVisible();
  expect(requested.length).toBeGreaterThanOrEqual(3);
  const origin = new URL(page.url()).origin;
  for (const fullUrl of requested) {
    const target = new URL(fullUrl);
    expect(target.origin).toBe(origin);
    expect(target.pathname).toMatch(/^\/(?:center(?:\/.*)?|assets\/index-[a-zA-Z0-9_-]+\.(?:js|css))$/);
    expect(target.pathname).not.toMatch(/\.map$/);
  }
});

test('NETWORK-01: center qualification page separates direct and partner-network models', async ({ page }) => {
  await page.goto('/center/qualifications');
  await expect(page.getByRole('heading', { name: 'دو مسیر مستقل احراز صلاحیت و قرارداد' })).toBeVisible();
  const paths = page.getByRole('list', { name: 'مسیرهای شبکه درمانی' });
  await expect(paths.getByRole('listitem')).toHaveCount(2);
  await expect(paths.getByRole('heading', { name: 'قرارداد مستقیم با ارائه‌دهنده' })).toBeVisible();
  await expect(paths.getByRole('heading', { name: 'همکاری با شبکه درمانی واجدصلاحیت' })).toBeVisible();
  await expect(paths.getByText('شواهد معتبر موجود نیست')).toHaveCount(2);
});

test('NETWORK-02: network qualification does not authorize treatment credit recipient', async ({ page }) => {
  await page.goto('/center/qualifications');
  await expect(page.getByRole('heading', { name: 'گیرنده اعتبار درمان، قرارداد مستقل لازم دارد' })).toBeVisible();
  await expect(page.getByText(/قرارداد شبکه، جای قرارداد گیرنده پرداخت را نمی‌گیرد/)).toBeVisible();
  await expect(page.getByText(/هیچ ارائه‌دهنده، پروانه، قرارداد، دریافت‌کننده وجه یا خدمت واقعی/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'بررسی و فعال‌سازی شبکه — نیازمند مرجع قانونی و قرارداد واقعی' })).toBeDisabled();
});

test('NETWORK-03: center network page stays disconnected through navigation and back', async ({ page }) => {
  const prohibited: string[] = [];
  page.on('request', request => {
    if (['/api/', '/provider/', '/license/', '/payment/', '/wallet/', '/patient/', '/contract/']
      .some(part => request.url().includes(part))) prohibited.push(request.url());
  });
  await page.goto('/center');
  await page.getByRole('link', { name: 'بررسی صلاحیت' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'بررسی صلاحیت' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'نمای کلی مرکز' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('button', { name: 'بررسی و فعال‌سازی شبکه — نیازمند مرجع قانونی و قرارداد واقعی' })).toBeDisabled();
  expect(prohibited).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});
