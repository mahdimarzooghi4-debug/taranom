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
