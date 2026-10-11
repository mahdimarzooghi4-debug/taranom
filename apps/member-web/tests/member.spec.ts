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

test('all ten member routes are shell-only and unknown routes render safe 404', async ({ page }) => {
  const routes = ['/member', '/member/membership', '/member/credit', '/member/consents', '/member/care',
    '/member/records', '/member/costs', '/member/billing', '/member/support', '/member/prevention'];
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

test('COST-01: member pre-treatment costs show all five shares as unknown, not zero', async ({ page }) => {
  await page.goto('/member/costs');
  await expect(page.getByRole('heading', { name: 'اجزای برآورد غیرقطعی' })).toBeVisible();
  const list = page.getByRole('list', { name: 'سهم‌های نامشخص پیش از خدمت' });
  await expect(list.getByRole('listitem')).toHaveCount(5);
  for (const label of [
    'هزینه احتمالی خدمت', 'سهم احتمالی بیمه پایه', 'سهم احتمالی بیمه تکمیلی',
    'سهم احتمالی اعتبار درمان', 'سهم احتمالی عضو',
  ]) {
    await expect(list.getByText(label, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(5);
  await expect(page.getByText('همه مقادیر نامعلوم‌اند')).toBeVisible();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
});

test('COST-02: disconnected cost awareness never records legal consent or financial choice', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => {
    const url = request.url();
    if (['/api/', '/auth/', '/payment/', '/payments/', '/wallet/']
      .some(path => url.includes(path))) requests.push(url);
  });
  await page.goto('/member/costs');
  await expect(page.getByRole('button', { name: 'ثبت آگاهی و انتخاب — نیازمند اتصال امن' })).toBeDisabled();
  await expect(page.getByText(/این نما تضمین قیمت، پوشش بیمه، موجودی کیف پول یا بدهی عضو نیست/))
    .toBeVisible();
  await page.keyboard.press('Tab');
  expect(requests).toEqual([]);
  await expect(page.getByRole('button', { name: /پرداخت|رزرو اعتبار|تأیید درمان/ })).toHaveCount(0);
});

test('COST-03: cost route remains isolated through member navigation and browser history', async ({ page }) => {
  await page.goto('/member');
  await page.getByRole('link', { name: 'برآورد هزینه' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'برآورد هزینه' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'اجزای برآورد غیرقطعی' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'اجزای برآورد غیرقطعی' })).toBeVisible();
});

test('SUPPORT-01: medical and insurance complaint routes remain legally distinct', async ({ page }) => {
  await page.goto('/member/support');
  await expect(page.getByRole('heading', { name: 'موضوعات شکایت و اعتراض' })).toBeVisible();
  const paths = page.getByRole('list', { name: 'مسیرهای مستقل رسیدگی' });
  await expect(paths.getByRole('listitem')).toHaveCount(2);
  const medical = paths.getByRole('listitem').filter({ hasText: 'شکایت از خدمت یا حادثه پزشکی' });
  const insurance = paths.getByRole('listitem').filter({ hasText: 'اعتراض به پوشش یا سهم بیمه' });
  await expect(medical).toContainText('مرجع پزشکی و حقوقی صاحب‌اختیار');
  await expect(insurance).toContainText('فقط با بیمه‌گر یا مرجع قانونی');
  await expect(insurance).toContainText('موجب برداشت، مسدودی یا اصلاح خودکار کیف پول نمی‌شود');
  await expect(medical).toContainText('مدارک سلامت صرفاً با مبنای قانونی');
});

test('SUPPORT-02: disconnected complaint UI cannot submit, pay or access private sources', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (['/api/', '/auth/', '/wallet/', '/claims/', '/medical/']
      .some(path => request.url().includes(path))) privateRequests.push(request.url());
  });
  await page.goto('/member/support');
  const paths = page.getByRole('list', { name: 'مسیرهای مستقل رسیدگی' });
  await expect(paths.getByRole('button', { name: 'ثبت درخواست — نیازمند احراز هویت و مرجع معتبر' })).toHaveCount(2);
  for (const button of await paths.getByRole('button').all()) {
    await expect(button).toBeDisabled();
  }
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
  expect(privateRequests).toEqual([]);
  await expect(page.getByRole('button', { name: /تأیید بیمه|صدور رأی|پرداخت خسارت/ })).toHaveCount(0);
});

test('SUPPORT-03: no fabricated case number or processing deadline, urgent help stays independent', async ({ page }) => {
  await page.goto('/member/support');
  await expect(page.getByText('هیچ شماره پرونده، زمان پاسخ یا نتیجه رسیدگی ساخته نشده است.', { exact: false })).toBeVisible();
  await expect(page.getByText('در فوریت پزشکی، کمک پزشکی و تماس با خدمات فوریت را به ثبت شکایت یا بررسی بیمه موکول نکنید.')).toBeVisible();
  await expect(page.getByRole('list', { name: 'مسیرهای مستقل رسیدگی' }).getByRole('heading',
    { name: 'ثبت و پیگیری هنوز متصل نیست' })).toHaveCount(2);
  await expect(page).toHaveURL(/\/member\/support$/);
});

test('SUPPORT-04: navigation back and forward retains disconnected legal boundaries', async ({ page }) => {
  await page.goto('/member');
  await page.getByRole('link', { name: 'پشتیبانی و شکایات' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'پشتیبانی و شکایات' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'موضوعات شکایت و اعتراض' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'موضوعات شکایت و اعتراض' })).toBeVisible();
});

test('CARE-01: three non-emergency care responsibilities remain legally separated', async ({ page }) => {
  await page.goto('/member/care');
  await expect(page.getByRole('heading', { name: 'مراحل و مسئولیت‌های مسیر درمان' })).toBeVisible();
  const list = page.getByRole('list', { name: 'مرزهای هماهنگی و درمان' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  const coordination = list.getByRole('listitem').filter({ hasText: 'درخواست و هماهنگی غیراضطراری' });
  const clinical = list.getByRole('listitem').filter({ hasText: 'تصمیم بالینی و ارجاع تخصصی' });
  const followUp = list.getByRole('listitem').filter({ hasText: 'پیگیری پس از خدمت' });
  await expect(coordination).toContainText('ترنم: هماهنگی ارتباط');
  await expect(clinical).toContainText('پزشک یا مرکز درمانی دارای صلاحیت');
  await expect(followUp).toContainText('متخصص مجاز: تصمیم مراقبت');
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
});

test('CARE-02: care preview never claims a licensed visit, appointment or referral', async ({ page }) => {
  await page.goto('/member/care');
  await expect(page.getByText('هیچ مسیر فعالی وجود ندارد')).toBeVisible();
  await expect(page.getByText('سامانه نمی‌تواند تشخیص، درمان یا ارجاع پزشکی را خودکار تأیید کند.')).toBeVisible();
  await expect(page.getByText('هیچ مراجعه، برنامه مراقبتی یا وضعیت تکمیل خدمت تأیید نشده است.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'درخواست هماهنگی — نیازمند هویت و مجوز واقعی' })).toBeDisabled();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
});

test('CARE-03: emergency pathway cannot be hidden behind care coordination and no private calls occur', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (['/api/', '/clinical/', '/referral/', '/appointment/', '/identity/']
      .some(path => request.url().includes(path))) privateRequests.push(request.url());
  });
  await page.goto('/member/care');
  await expect(page.getByText('در احتمال فوریت پزشکی، دریافت کمک فوری را به هماهنگی غیراضطراری یا پاسخ پزشک در سامانه موکول نکنید.')).toBeVisible();
  await expect(page.getByText('مشارکت عضو و استفاده از اطلاعات سلامت فقط با مبنای قانونی و مجوز معتبر انجام می‌شود.', { exact: false })).toBeVisible();
  expect(privateRequests).toEqual([]);
  await expect(page.getByRole('button', { name: /تأیید درمان|تجویز|ارجاع تخصصی/ })).toHaveCount(0);
});

test('CARE-04: browser history restores care context without enabling any action', async ({ page }) => {
  await page.goto('/member');
  await page.getByRole('link', { name: 'هماهنگی درمان' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'هماهنگی درمان' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'مراحل و مسئولیت‌های مسیر درمان' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'مراحل و مسئولیت‌های مسیر درمان' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'درخواست هماهنگی — نیازمند هویت و مجوز واقعی' })).toBeDisabled();
});

test('CONSENT-10: independent care and secondary research consent prerequisites appear unknown', async ({ page }) => {
  await page.goto('/member/consents');
  await expect(page.getByRole('heading', { name: 'مرزهای مستقل رضایت و حفاظت داده' })).toBeVisible();
  const list = page.getByRole('list', { name: 'پیش‌شرط‌های مستقل اشتراک و استفاده داده' });
  await expect(list.getByRole('listitem')).toHaveCount(4);
  for (const name of [
    'اشتراک‌گذاری برای خدمات درمانی',
    'رضایت مستقل پژوهش و آموزش هوش مصنوعی',
    'مجوز و مبنای قانونی پردازش ثانویه',
    'حفاظت داده و محدوده استفاده',
  ]) {
    await expect(list.getByText(name, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(4);
  await expect(page.getByText('هیچ رضایت معتبری دریافت نشده است')).toBeVisible();
});

test('CONSENT-11: no implicit AI consent, real medical sharing or dataset action', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (['/api/', '/consent/', '/health/', '/training/', '/ai/', '/identity/']
      .some(route => request.url().includes(route))) privateRequests.push(request.url());
  });
  await page.goto('/member/consents');
  const list = page.getByRole('list', { name: 'پیش‌شرط‌های مستقل اشتراک و استفاده داده' });
  await expect(list).toContainText('عضویت، پرداخت یا رضایت درمانی هرگز جانشین رضایت آگاهانه مستقل پژوهشی نیست.');
  await expect(list).toContainText('رضایت به‌تنهایی مجوز قانونی استفاده پژوهشی یا انتقال داده ایجاد نمی‌کند.');
  await expect(page.getByText(/نحوه لغو رضایت، نگهداری/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'ثبت یا تغییر رضایت — نیازمند احراز هویت و قرارداد قانونی' })).toBeDisabled();
  expect(privateRequests).toEqual([]);
  expect(await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) })))
    .toEqual({ local: [], session: [] });
});

test('CONSENT-12: member navigation preserves disabled privacy action through history', async ({ page }) => {
  await page.goto('/member');
  await page.getByRole('link', { name: 'رضایت‌ها و حریم خصوصی' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'رضایت‌ها و حریم خصوصی' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'مرزهای مستقل رضایت و حفاظت داده' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('button', { name: 'ثبت یا تغییر رضایت — نیازمند احراز هویت و قرارداد قانونی' })).toBeDisabled();
});


test('MEMBER-HTTP-01: real built-preview HTML response supplies frame protections and deny egress headers', async ({ request, page }) => {
  const response = await request.get('/member/records');
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
  await page.goto('/member/records');
  await expect(page.getByRole('main')).toBeVisible();
});

test('MEMBER-HTTP-02: built JS asset also gets no-sniff and no-store response headers', async ({ page, request }) => {
  await page.goto('/member');
  const asset = await page.locator('script[type="module"][src^="/assets/"]').getAttribute('src');
  expect(asset).toMatch(/^\/assets\/index-[a-zA-Z0-9_-]+\.js$/);
  const response = await request.get(asset!);
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('javascript');
  expect(response.headers()['x-content-type-options']).toBe('nosniff');
  expect(response.headers()['cache-control']).toContain('no-store');
  expect(response.headers()['content-security-policy']).toContain("connect-src 'none'");
});

test('MEMBER-HTTP-03: Chromium cannot render an iframe of the built preview', async ({ page }) => {
  const target = 'http://127.0.0.1:4174/member';
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


test('BUNDLE-01: built member shell fetches only local route, JS and CSS (no external resources)', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', request => requested.push(request.url()));
  await page.goto('/member/records');
  await expect(page.getByRole('main')).toBeVisible();
  expect(requested.length).toBeGreaterThanOrEqual(3);
  const origin = new URL(page.url()).origin;
  for (const fullUrl of requested) {
    const target = new URL(fullUrl);
    expect(target.origin).toBe(origin);
    expect(target.pathname).toMatch(/^\/(?:member(?:\/.*)?|assets\/index-[a-zA-Z0-9_-]+\.(?:js|css))$/);
    expect(target.pathname).not.toMatch(/\.map$/);
  }
});

test('PAYER-01: three market funding routes remain unknown and membership stays individual', async ({ page }) => {
  await page.goto('/member/membership');
  await expect(page.getByRole('heading', { name: 'مسیرهای سه‌گانه تأمین هزینه عضویت' })).toBeVisible();
  const routes = page.getByRole('list', { name: 'مسیرهای پرداخت و مالکیت فردی' });
  await expect(routes.getByRole('listitem')).toHaveCount(3);
  for (const name of ['عموم مردم', 'نهادهای حمایتی', 'سازمان‌ها و شرکت‌ها']) {
    await expect(routes.getByText(name, { exact: true })).toBeVisible();
  }
  await expect(routes.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
  await expect(routes).toContainText('عضو یا نهاد حمایتی، فقط مطابق قرارداد معتبر');
  await expect(routes).toContainText('عضو یا کارفرما، فقط مطابق قرارداد معتبر');
  await expect(page.getByText('مالک عضویت و کیف پول درمانی همیشه خود فرد عضو است.', { exact: false })).toBeVisible();
});

test('PAYER-02: sponsor subscription payment does not confer clinical or wallet rights', async ({ page }) => {
  await page.goto('/member/membership');
  await expect(page.getByText('پرداخت حق اشتراک توسط حامی، حق دیدن پرونده سلامت، برداشت اعتبار،', { exact: false })).toBeVisible();
  await expect(page.getByText('هیچ قرارداد یا پرداختی تأیید نشده است')).toBeVisible();
  await expect(page.getByRole('button', { name: 'درخواست یا پرداخت عضویت — نیازمند قرارداد و سرویس معتبر' })).toBeDisabled();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
});

test('PAYER-03: individual wallet preview exposes no balance or payment activation', async ({ page }) => {
  await page.goto('/member/credit');
  await expect(page.getByRole('heading', { name: 'اختیار کیف پول درمانی متعلق به عضو است' })).toBeVisible();
  await expect(page.getByText('موجودی نامعلوم')).toBeVisible();
  await expect(page.getByText('تأمین هزینه اشتراک توسط سازمان یا نهاد حمایتی، مالکیت کیف پول یا دسترسی', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'مشاهده گردش اعتبار — نیازمند احراز هویت و منبع مالی معتبر' })).toBeDisabled();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
});

test('PAYER-04: member funding preview does not contact private services or write browser storage', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (['/api/', '/billing/', '/subscription/', '/wallet/', '/identity/', '/health/']
      .some(path => request.url().includes(path))) privateRequests.push(request.url());
  });
  await page.goto('/member/membership');
  await page.getByRole('link', { name: 'اعتبار درمان' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'اعتبار درمان' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'مسیرهای سه‌گانه تأمین هزینه عضویت' })).toBeVisible();
  expect(privateRequests).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('PREVENTION-01: member sees three separate unverified education/prevention/clinical responsibilities', async ({ page }) => {
  await page.goto('/member/prevention');
  await expect(page.getByRole('heading', { level: 1, name: 'آموزش و پیشگیری' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'سه مسیر مستقل پیشگیری و مراقبت' })).toBeVisible();
  const list = page.getByRole('list', { name: 'مسیرهای آموزش پیشگیری و مراقبت' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  for (const name of ['آموزش سلامت', 'هماهنگی فعالیت‌های پیشگیرانه', 'مراقبت مستمر بالینی']) {
    await expect(list.getByText(name, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'شواهد معتبر موجود نیست' })).toHaveCount(3);
});

test('PREVENTION-02: member consent and research/AI consent are not inferred from participation', async ({ page }) => {
  await page.goto('/member/prevention');
  await expect(page.getByRole('heading', { name: 'مشارکت اختیاری و رضایت مستقل' })).toBeVisible();
  await expect(page.getByText('انتخاب عضو نامعلوم است')).toBeVisible();
  await expect(page.getByText(/مشارکت در آموزش یا مراقبت، رضایت مستقل پژوهش و آموزش هوش مصنوعی نیست/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'درخواست مشارکت — نیازمند هویت و رضایت معتبر' })).toBeDisabled();
  await page.getByRole('link', { name: 'رضایت‌ها و حریم خصوصی' }).click();
  await expect(page.getByRole('heading', { name: 'مرزهای مستقل رضایت و حفاظت داده' })).toBeVisible();
});

test('PREVENTION-03: clinical continuity decisions never appear as automatic care plans', async ({ page }) => {
  await page.goto('/member/prevention');
  await expect(page.getByText(/برنامه مراقبت مستمر فقط با ارزیابی و تصمیم متخصص مجاز/)).toBeVisible();
  await expect(page.getByText(/هیچ پایش خودکار دائمی، تشخیص پزشکی یا محتوای آموزشیِ تأییدشده/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'مشاهده پیشنهاد تیم درمانی — نیازمند مرجع بالینی معتبر' })).toBeDisabled();
  await expect(page.getByText(/در احتمال فوریت، دریافت کمک فوری پزشکی را/)).toBeVisible();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
});

test('PREVENTION-04: new member route remains disconnected and preserves browser focus/back state', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', request => {
    if (['/api/', '/patient/', '/medical/', '/health/', '/research/', '/training/', '/program/']
      .some(path => request.url().includes(path))) privateRequests.push(request.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'آموزش و پیشگیری' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'آموزش و پیشگیری' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('button', { name: 'درخواست مشارکت — نیازمند هویت و رضایت معتبر' })).toBeDisabled();
  expect(privateRequests).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('BILLING-01: post-service legal-source view keeps five missing amounts genuinely unknown', async ({ page }) => {
  await page.goto('/member/billing');
  await expect(page.getByRole('heading', { name: 'اسناد و سهم‌های صورتحساب درمان' })).toBeVisible();
  const list = page.getByRole('list', { name: 'منشأهای مستقل صورتحساب پس از خدمت' });
  await expect(list.getByRole('listitem')).toHaveCount(5);
  for (const heading of [
    'هزینه خدمت در سند درمان', 'سهم بیمه پایه در صورتحساب',
    'سهم بیمه تکمیلی در صورتحساب', 'سهم کیف پول درمانی در صورتحساب',
    'سهم نهایی عضو در صورتحساب'
  ]) {
    await expect(list.getByText(heading, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(5);
  await expect(page.getByText('هیچ سندی تأیید نشده است')).toBeVisible();
});

test('BILLING-02: dispute and correction claims cannot mutate credit or activate a payment', async ({ page }) => {
  const attempted: string[] = [];
  page.on('request', req => {
    if (['/api/', '/billing/', '/claims/', '/payment/', '/wallet/']
      .some(p => req.url().includes(p))) attempted.push(req.url());
  });
  await page.goto('/member/billing');
  await expect(page.getByText(/ثبت اعتراض موجب اصلاح،/)).toBeVisible();
  await expect(page.getByText(/رزرو، برداشت یا تسویه خودکار کیف پول عضو نمی‌شود/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'پیگیری اختلاف یا اصلاح — نیازمند سند و مرجع قانونی' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'دریافت صورتحساب رسمی — نیازمند صادرکننده معتبر' })).toBeDisabled();
  await expect(page.getByRole('button', { name: /پرداخت اکنون|تسویه بدهی|تأیید سهم بیمه/ })).toHaveCount(0);
  expect(attempted).toEqual([]);
});

test('BILLING-03: sponsor and employer see no member bill and no fabricated source identity', async ({ page }) => {
  await page.goto('/member/billing?payer=employer&insured=true');
  await expect(page.getByText(/نهاد حامی یا کارفرما صرفاً به‌دلیل پرداخت اشتراک/)).toBeVisible();
  await expect(page.getByText(/به صورتحساب،/)).toBeVisible();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
  await expect(page.getByText(/مبلغ قابل پرداخت[:：]\s*[۰-۹0-9]/)).toHaveCount(0);
});

test('BILLING-04: member navigates estimates and bills without conflating official documents', async ({ page }) => {
  await page.goto('/member/costs');
  await expect(page.getByRole('heading', { name: 'اجزای برآورد غیرقطعی' })).toBeVisible();
  await page.getByRole('link', { name: 'صورتحساب‌ها' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'صورتحساب‌ها' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'اسناد و سهم‌های صورتحساب درمان' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'برآورد هزینه' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'اسناد و سهم‌های صورتحساب درمان' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'دریافت صورتحساب رسمی — نیازمند صادرکننده معتبر' })).toBeDisabled();
});

test('RECORD-01: record sharing presents three legally distinct unavailable paths', async ({ page }) => {
  await page.goto('/member/records');
  await expect(page.getByRole('heading', { name: 'مسیرهای مجزای مشاهده و اشتراک پرونده' })).toBeVisible();
  const list = page.getByRole('list', { name: 'حدود مجاز دسترسی به پرونده سلامت' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  for (const label of [
    'مشاهده شخصی سوابق سلامت عضو', 'اشتراک محدود با متخصص درمانگر',
    'استثنای قانونی یا فوریت پزشکی'
  ]) {
    await expect(list.getByText(label, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
  await expect(page.getByText('هیچ دسترسی فعالی وجود ندارد')).toBeVisible();
});

test('RECORD-02: employer and sponsor never receive care records through subscription payment', async ({ page }) => {
  await page.goto('/member/records?employer=true&authorized=true');
  await expect(page.getByText(/پرداخت اشتراک توسط کارفرما یا نهاد حمایتی مجوز مشاهده پرونده سلامت نیست/))
    .toBeVisible();
  await expect(page.getByText(/بدون نیاز درمانی مشخص، صلاحیت مستقل/)).toBeVisible();
  for (const name of [
    'مشاهده پرونده شخصی — نیازمند هویت و منبع معتبر',
    'مدیریت دامنه اشتراک با متخصص — نیازمند رضایت و بررسی قانونی',
    'سوابق دسترسی به پرونده — نیازمند ممیزی معتبر'
  ]) {
    await expect(page.getByRole('button', { name })).toBeDisabled();
  }
});

test('RECORD-03: clinical sharing never implies secondary AI consent or emergency record disclosure', async ({ page }) => {
  await page.goto('/member/records');
  await expect(page.getByText(/اشتراک برای درمان رضایت پژوهش یا آموزش هوش مصنوعی نیست/)).toBeVisible();
  await expect(page.getByText(/هشدار فوریت پزشکی به‌خودی‌خود مجوز افشای اطلاعات ایجاد نمی‌کند/)).toBeVisible();
  await expect(page.getByText(/در فوریت پزشکی دریافت کمک فوری را/)).toBeVisible();
  await expect(page.getByRole('button', { name: /تأیید دسترسی پزشک|افشای اضطراری|رضایت آموزش AI/ })).toHaveCount(0);
});

test('RECORD-04: record route is disconnected, stores nothing and keeps browser history/focus', async ({ page }) => {
  const privateCalls: string[] = [];
  page.on('request', req => {
    if (['/api/', '/health/', '/records/', '/patient/', '/clinical/', '/identity/', '/consent/']
      .some(prefix => req.url().includes(prefix))) privateCalls.push(req.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'سوابق سلامت' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'سوابق سلامت' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'مسیرهای مجزای مشاهده و اشتراک پرونده' })).toBeVisible();
  expect(privateCalls).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('CREDIT-PERIOD-01: membership route explains official initial 90-day wait and nonrenewal boundaries', async ({ page }) => {
  await page.goto('/member/membership');
  await expect(page.getByRole('heading', { name: 'مرزهای زمانی عضویت و اعتبار درمانی' })).toBeVisible();
  const steps = page.getByRole('list', { name: 'قواعد مصوب چرخه عضویت' });
  await expect(steps.getByRole('listitem')).toHaveCount(4);
  for (const label of ['آغاز رسمی عضویت', 'انتظار اولیه ۹۰ روز کامل',
    'عدم تمدید و دوره وقفه', 'فعال‌سازی دوباره اشتراک']) {
    await expect(steps.getByText(label, { exact: true })).toBeVisible();
  }
  await expect(steps.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(4);
  await expect(page.getByText('تاریخ رسمی موجود نیست')).toBeVisible();
});

test('CREDIT-PERIOD-02: credit route separates retained wallet, nonrenewal gap and late review', async ({ page }) => {
  await page.goto('/member/credit');
  const list = page.getByRole('list', { name: 'مرزهای تاریخچه اعتبار عضو' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  for (const label of ['اعتبار درمانی قبلی در کیف پول شخصی',
    'درآمد منتسب به دوره وقفه', 'منفعت دیرثبت‌شده مربوط به عضویت معتبر قبلی']) {
    await expect(list.getByText(label, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
  await expect(page.getByText('هیچ مبلغی بررسی یا تأیید نشده است')).toBeVisible();
  await expect(page.getByRole('button',
    { name: 'بررسی منافع دیرثبت‌شده — نیازمند اسناد و مرجع مالی معتبر' })).toBeDisabled();
});

test('CREDIT-PERIOD-03: no source claim creates an automatic wallet credit or new waiting policy', async ({ page }) => {
  await page.goto('/member/credit');
  await expect(page.getByText(/صرف ادعای منبع درآمد،/)).toBeVisible();
  await expect(page.getByText(/موجودی یا اعتبار قابل‌مصرف ایجاد نمی‌کند/)).toBeVisible();
  await expect(page.getByRole('button', { name: /تأیید واریز|برداشت نقدی|پرداخت به من|افزایش خودکار اعتبار/ })).toHaveCount(0);
  await page.getByRole('link', { name: 'عضویت من' }).click();
  await expect(page.getByText(/انتظار تازه‌ای برای اعتبار قبلی فرض نمی‌شود/)).toBeVisible();
  await expect(page.getByRole('button',
    { name: 'مشاهده تاریخچه معتبر عضویت — نیازمند هویت و منبع رسمی' })).toBeDisabled();
});

test('CREDIT-PERIOD-04: credit and membership history are private and disconnected', async ({ page }) => {
  const privateCalls: string[] = [];
  page.on('request', req => {
    if (['/api/', '/finance/', '/payment/', '/wallet/', '/membership/', '/subscription/']
      .some(value => req.url().includes(value))) privateCalls.push(req.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'عضویت من' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'عضویت من' })).toBeFocused();
  await page.getByRole('link', { name: 'اعتبار درمان' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'اعتبار درمان' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'مرزهای زمانی عضویت و اعتبار درمانی' })).toBeVisible();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'منشأ زمانی منافع و حفظ اعتبار قبلی' })).toBeVisible();
  expect(privateCalls).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('AI-DATA-01: consent page shows four separate unknown AI data and governance lanes', async ({ page }) => {
  await page.goto('/member/consents');
  await expect(page.getByRole('heading', { name: 'مراحل حفاظت داده تا بررسی نسخه هوش مصنوعی' })).toBeVisible();
  const list = page.getByRole('list', { name: 'مراحل مستقل حاکمیت داده و AI' });
  await expect(list.getByRole('listitem')).toHaveCount(4);
  for (const title of [
    'رضایت آگاهانه و مستقل برای پژوهش و AI',
    'منشأ قانونی و حفاظت داده سلامت',
    'تفکیک داده آموزش و ارزیابی مستقل',
    'کاندید، کمیته مشترک و تأیید نهایی انسانی'
  ]) {
    await expect(list.getByText(title, { exact: true })).toBeVisible();
  }
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(4);
  await expect(page.getByText('هیچ داده یا نسخه‌ای فعال نیست')).toBeVisible();
});

test('AI-DATA-02: care, paid subscription and training remain independent and never auto promote', async ({ page }) => {
  await page.goto('/member/consents?research=true&opt_in=yes&candidate=approved');
  await expect(page.getByText(/صرف حضور در درمان، پرداخت اشتراک یا ثبت رضایت درمانی/)).toBeVisible();
  await expect(page.getByText(/ساخت کاندید، تأیید مدل یا فعال‌سازی خودکار Production محسوب نمی‌شود/))
    .toBeVisible();
  await expect(page.getByText(/سرویس هوش مصنوعی خارجی در اجرای ترنم مجاز نیست/)).toBeVisible();
  await expect(page.getByRole('button',
    { name: 'مشاهده وضعیت رضایت و استفاده پژوهشی — نیازمند منبع و مجوز واقعی' })).toBeDisabled();
  await expect(page.getByRole('button',
    { name: 'بررسی نسخه هوش مصنوعی — فقط مرجع پزشکی و فنی صاحب‌اختیار' })).toBeDisabled();
  await expect(page.getByRole('button', { name: /شروع آموزش|فعال‌سازی مدل|ارسال پرونده/ })).toHaveCount(0);
});

test('AI-DATA-03: absent legal provenance and no data partitions cannot imply ready AI', async ({ page }) => {
  await page.goto('/member/consents');
  const list = page.getByRole('list', { name: 'مراحل مستقل حاکمیت داده و AI' });
  await expect(list.getByText(/هیچ پرونده‌ای منتقل نشده است/)).toBeVisible();
  await expect(list.getByText(/هیچ Dataset واقعی یا سیاست آموزش آماده نیست/)).toBeVisible();
  await expect(page.getByRole('status')).toContainText('این بخش هنوز به سرویس واقعی متصل نیست');
  await expect(page.getByRole('button',
    { name: 'ثبت یا تغییر رضایت — نیازمند احراز هویت و قرارداد قانونی' })).toBeDisabled();
});

test('AI-DATA-04: AI data explanations remain disconnected across member navigation and history', async ({ page }) => {
  const privateRequests: string[] = [];
  page.on('request', req => {
    if (['/api/', '/training/', '/dataset/', '/models/', '/inference/', '/consent/', '/health/']
      .some(x => req.url().includes(x))) privateRequests.push(req.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'رضایت‌ها و حریم خصوصی' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'رضایت‌ها و حریم خصوصی' })).toBeFocused();
  await page.getByRole('link', { name: 'سوابق سلامت' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'سوابق سلامت' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'مراحل حفاظت داده تا بررسی نسخه هوش مصنوعی' })).toBeVisible();
  await expect(page.getByRole('button',
    { name: 'بررسی نسخه هوش مصنوعی — فقط مرجع پزشکی و فنی صاحب‌اختیار' })).toBeDisabled();
  expect(privateRequests).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('URGENT-01: safety guidance, proposed referral and receipt are separate', async ({ page }) => {
  await page.goto('/member/care');
  await expect(page.getByRole('heading', { name: 'سه مرز ایمنی در احتمال فوریت پزشکی' })).toBeVisible();
  const list = page.getByRole('list', { name: 'مرزهای راهنمایی فوری و ارجاع موازی' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  for (const title of [
    'راهنمایی غیرتشخیصی برای دریافت کمک فوری',
    'درخواست ارجاع هم‌زمان به تیم بالینی مجاز',
    'تأیید مستقل دریافت و پیگیری بالینی'
  ]) await expect(list.getByText(title, { exact: true })).toBeVisible();
  await expect(list.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
});

test('URGENT-02: urgent assistance never waits for member coordination, coverage or dispatch', async ({ page }) => {
  await page.goto('/member/care');
  await expect(page.getByText(/در احتمال فوریت پزشکی، دریافت کمک فوری را/)).toBeVisible();
  await expect(page.getByText(/منتظر هماهنگی، بیمه، پرداخت یا شکایت نمانید/)).toBeVisible();
  await expect(page.getByText('هیچ ارجاع یا دریافت واقعی ثبت نشده است')).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'آغاز ارجاع پزشکی — نیازمند احراز اختیار و پروتکل بالینی معتبر'
  })).toBeDisabled();
});

test('URGENT-03: paper record never claims actual delivery, diagnosis or 24-hour monitoring', async ({ page }) => {
  await page.goto('/member/care');
  const list = page.getByRole('list', { name: 'مرزهای راهنمایی فوری و ارجاع موازی' });
  await expect(list.getByText(/داشتن یادداشت درخواست، به‌معنای ارسال، تحویل، دریافت یا درمان موفق نیست/)).toBeVisible();
  await expect(page.getByText(/خدمت پایش شبانه‌روزی ارائه نمی‌کند/)).toBeVisible();
  await expect(page.getByText(/هیچ هشدار شخصی‌سازی‌شده، تشخیص پزشکی یا تضمین رسیدگی صادر نشده است/)).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'بررسی رسید ارجاع بالینی — نیازمند تیم و سامانه واقعی'
  })).toBeDisabled();
  await expect(page.getByRole('button', { name: /تشخیص|تجویز|ارجاع موفق|تأیید فوریت/ })).toHaveCount(0);
});

test('URGENT-04: safety explanation stays disconnected and keyboard/history safe', async ({ page }) => {
  const prohibited: string[] = [];
  page.on('request', request => {
    if (['/api/', '/urgent/', '/emergency/', '/triage/', '/referral/', '/clinical/', '/ai/']
      .some(x => request.url().includes(x))) prohibited.push(request.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'هماهنگی درمان' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'هماهنگی درمان' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'سه مرز ایمنی در احتمال فوریت پزشکی' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'سه مرز ایمنی در احتمال فوریت پزشکی' })).toBeVisible();
  expect(prohibited).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('ACTIVATION-01: membership route separates licensed identity, correction, payment and formal activation', async ({ page }) => {
  await page.goto('/member/membership');
  await expect(page.getByRole('heading', { name: 'شواهد لازم پیش از فعال‌سازی عضویت' })).toBeVisible();
  const steps = page.getByRole('list', { name: 'پیش‌شرط‌های مستقل فعال‌سازی عضویت' });
  await expect(steps.getByRole('listitem')).toHaveCount(4);
  for (const label of [
    'احراز هویت از ارائه‌دهنده تخصصی مجاز',
    'اصلاح کد ملی و احراز مجدد قبل از فعال‌سازی',
    'قرارداد و پرداخت واقعی اشتراک',
    'ثبت رسمی فعال‌سازی و مبدأ انتظار'
  ]) await expect(steps.getByText(label, { exact: true })).toBeVisible();
  await expect(steps.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(4);
});

test('ACTIVATION-02: no query strings or claimed paid status imply a member has been activated', async ({ page }) => {
  await page.goto('/member/membership?paid=1&identity=verified&activate=true&start_90_days=now');
  await expect(page.getByText('هیچ عضویت رسمی فعال نشده است')).toBeVisible();
  await expect(page.getByText(/حتی تکمیل.*ادعایی این مراحل/)).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'بررسی فعال‌سازی رسمی — نیازمند شواهد همه مراجع قانونی'
  })).toBeDisabled();
  await expect(page.getByRole('button', { name: /فعال‌سازی موفق|پرداخت موفق|شروع انتظار|ورود خودکار/ })).toHaveCount(0);
});

test('ACTIVATION-03: correction invalidates old identity and funder does not inherit private rights', async ({ page }) => {
  await page.goto('/member/membership');
  const steps = page.getByRole('list', { name: 'پیش‌شرط‌های مستقل فعال‌سازی عضویت' });
  await expect(steps.getByText(/نتیجه قدیمی پس از اصلاح دیگر معتبر نیست/)).toBeVisible();
  await expect(steps.getByText(/مالک عضویت و کیف پول شخص است/)).toBeVisible();
  await expect(page.getByText(/درخواست بدون فعال‌سازی باقی می‌ماند/)).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'اصلاح و احراز مجدد اطلاعات — نیازمند هویت و سرویس معتبر'
  })).toBeDisabled();
});

test('ACTIVATION-04: identity and payment activation screen has no private calls or browser persistence', async ({ page }) => {
  const forbidden: string[] = [];
  page.on('request', request => {
    if (['/api/', '/identity/', '/proofing/', '/membership/', '/payment/',
      '/provider/', '/wallet/', '/activation/', '/subscription/']
      .some(x => request.url().includes(x))) forbidden.push(request.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'عضویت من' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'عضویت من' })).toBeFocused();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('heading', { name: 'شواهد لازم پیش از فعال‌سازی عضویت' })).toBeVisible();
  expect(forbidden).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});

test('INSURANCE-HANDOFF-01: support distinguishes unified intake, legal forwarding and insurer-only response', async ({ page }) => {
  await page.goto('/member/support');
  await expect(page.getByRole('heading', { name: 'سه مرحله مستقل پیگیری اختلاف سهم بیمه' })).toBeVisible();
  const stages = page.getByRole('list', { name: 'پیوند اعتراض بیمه با اسناد رسمی' });
  await expect(stages.getByRole('listitem')).toHaveCount(3);
  for (const title of [
    'ثبت اعتراض نزد ترنم و پیوند به نسخه صورتحساب',
    'ارجاع از مسیر مستقیم یا واسطه بیمه‌ای مجاز',
    'پاسخ قانونی بیمه‌گر و اصلاح مستقل سند'
  ]) await expect(stages.getByText(title, { exact: true })).toBeVisible();
  await expect(stages.getByRole('heading', { name: 'اطلاعات موجود نیست' })).toHaveCount(3);
});

test('INSURANCE-HANDOFF-02: forwarded request is not insurer receipt or coverage and cannot change wallet', async ({ page }) => {
  await page.goto('/member/support?insurer_approved=1&paid=1&receipt=true');
  await expect(page.getByText('هیچ نتیجه یا رسید واقعی وجود ندارد')).toBeVisible();
  await expect(page.getByText(/درخواست ارسال‌شده، رسید بیمه‌گر یا رأی نهایی نیست/)).toBeVisible();
  await expect(page.getByText(/هیچ برداشت، مسدودی، جبران، برگشت یا اصلاح خودکار اعتبار درمان/)).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'بررسی رسید بیمه‌گر — نیازمند کانال و سند معتبر'
  })).toBeDisabled();
});

test('INSURANCE-HANDOFF-03: insurer authority and sponsor privacy remain distinct from medical complaint', async ({ page }) => {
  await page.goto('/member/support');
  await expect(page.getByRole('list', { name: 'مسیرهای مستقل رسیدگی' })
    .getByRole('listitem')).toHaveCount(2);
  await expect(page.getByText(/فقط بیمه‌گر یا مرجع قانونی می‌تواند سهم پوشش را/)).toBeVisible();
  await expect(page.getByText(/حامی یا کارفرما با پرداخت اشتراک حق دیدن اسناد درمان عضو/)).toBeVisible();
  await expect(page.getByRole('button', {
    name: 'درخواست بررسی اصلاح سهم — فقط با مرجع قانونی'
  })).toBeDisabled();
  await expect(page.getByText(/در فوریت پزشکی، کمک پزشکی و تماس با خدمات فوریت/)).toBeVisible();
});

test('INSURANCE-HANDOFF-04: no private invoice/insurer requests or persistent data during member navigation', async ({ page }) => {
  const forbidden: string[] = [];
  page.on('request', req => {
    if (['/api/', '/insurer/', '/insurance/', '/claims/', '/billing/',
      '/wallet/', '/health/', '/coverage/', '/payment/']
      .some(part => req.url().includes(part))) forbidden.push(req.url());
  });
  await page.goto('/member');
  await page.getByRole('link', { name: 'پشتیبانی و شکایات' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'پشتیبانی و شکایات' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'سه مرحله مستقل پیگیری اختلاف سهم بیمه' })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'فضایی آرام برای پیگیری سلامت' })).toBeFocused();
  await page.goForward();
  await expect(page.getByRole('button', {
    name: 'بررسی رسید بیمه‌گر — نیازمند کانال و سند معتبر'
  })).toBeDisabled();
  expect(forbidden).toEqual([]);
  expect(await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage)
  }))).toEqual({ local: [], session: [] });
});
