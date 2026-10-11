import { expect, test } from '@playwright/test';

function unique(prefix: string) {
  return 'test:' + prefix + '-' + crypto.randomUUID().replaceAll('-', '');
}
async function setup(page: import('@playwright/test').Page) {
  await page.goto('/');
  const center = unique('center'), department = unique('dept'), doctor = unique('doctor');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return { center, department, doctor };
}
test('CENTER-DEMO-01: true PostgreSQL write/read, versioned complete center workflow', async ({ page }) => {
  const keys = await setup(page);
  await expect(page.getByText('نسخه ثبت‌شده:')).toContainText('نسخه ثبت‌شده:');
  await page.getByRole('button', { name: 'ثبت بخش آزمایشی' }).click();
  await expect(page.getByRole('status').first()).toContainText('فقط در پایگاه آزمایشی ثبت شد');
  await page.getByRole('button', { name: 'ثبت وابستگی تأییدنشده پزشک آزمایشی' }).click();
  await expect(page.getByText('در انتظار بررسی مستقل')).toBeVisible();
  await page.getByRole('button', { name: 'تخصیص آزمایشی پزشک به بخش' }).click();
  await expect(page.getByRole('region', { name: 'وضعیت ذخیره‌شده آزمایشی' })).toContainText(keys.department);
  await page.getByRole('button', { name: 'پایان وابستگی آزمایشی پزشک' }).click();
  await expect(page.getByRole('region', { name: 'وضعیت ذخیره‌شده آزمایشی' })).toContainText('پایان‌یافته');
  await expect(page.getByRole('region', { name: 'وضعیت ذخیره‌شده آزمایشی' })).toContainText('نسخه ثبت‌شده: 4');
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('region', { name: 'وضعیت ذخیره‌شده آزمایشی' })).toContainText('نسخه ثبت‌شده: 4');
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});
test('CENTER-DEMO-02: invalid references blocked before contacting journal', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill('real:center');
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status').first()).toContainText('فقط شناسه‌های آزمایشی');
  await expect(page.getByRole('button', { name: 'ثبت بخش آزمایشی' })).toBeDisabled();
});
test('CENTER-DEMO-03: network loss clears privileged-looking state', async ({ page }) => {
  await setup(page);
  await page.route('**/internal/demo/v1/centers/**', route => route.abort());
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status').first()).toContainText('وضعیت معتبر فرض نمی‌شود');
  await expect(page.getByRole('button', { name: 'پایان وابستگی آزمایشی پزشک' })).toBeDisabled();
});
test('CENTER-DEMO-04: HTTP scope and replay are durable and deny real center data', async ({ request }) => {
  const center = unique('http');
  const path = '/internal/demo/v1/centers/' + encodeURIComponent(center);
  const read = await request.get(path);
  expect(read.status()).toBe(200);
  expect((await read.json()).currentVersion).toBe(0);
  const payload = {
    requestId: crypto.randomUUID(), expectedVersion: 0,
    kind: 'DEFINE_DEPARTMENT', departmentRef: unique('dept'), doctorRef: null
  };
  const first = await request.post(path + '/commands', { data: payload });
  expect(first.status()).toBe(200);
  expect((await first.json()).currentVersion).toBe(1);
  const replay = await request.post(path + '/commands', { data: payload });
  expect(replay.status()).toBe(200);
  expect((await replay.json()).outcome).toBe('Replayed');
  const cross = await request.post(path + '/commands', { data: { ...payload, departmentRef: unique('other') } });
  expect(cross.status()).toBe(409);
  expect((await cross.json()).outcome).toBe('ConflictingReplay');
  const stale = await request.post(path + '/commands', { data: { ...payload, requestId: crypto.randomUUID() } });
  expect(stale.status()).toBe(409);
  expect((await stale.json()).outcome).toBe('StaleRevision');
  expect((await (await request.get(path)).json()).currentVersion).toBe(1);
  expect((await request.get('/internal/demo/v1/centers/real:center')).status()).toBe(400);
  expect((await request.post(path + '/commands', { data: { ...payload, requestId: crypto.randomUUID(), managerAuthenticated: true } })).status()).toBe(400);
});
test('CENTER-DEMO-05: no verified rights or real identity even after successful workflow', async ({ request, page }) => {
  const outside: string[] = [];
  page.on('request', req => {
    if (new URL(req.url()).origin !== 'http://127.0.0.1:4320') outside.push(req.url());
  });
  const { center } = await setup(page);
  await page.getByRole('button', { name: 'ثبت بخش آزمایشی' }).click();
  const json = await (await request.get('/internal/demo/v1/centers/' + encodeURIComponent(center))).json();
  expect(json.syntheticOnly).toBe(true);
  expect(json.managerAuthenticated).toBe(false);
  expect(json.professionalQualificationVerified).toBe(false);
  expect(json.nationalReleaseAllowed).toBe(false);
  expect(json.doctors).toHaveLength(0);
  expect(json.eventCount).toBe(1);
  expect(outside).toEqual([]);
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
});


test('CENTER-HISTORY-01: four PostgreSQL events paginate and reconstruct earlier state in browser', async ({ page }) => {
  const { department, doctor } = await setup(page);
  await page.getByRole('button', { name: 'ثبت بخش آزمایشی' }).click();
  await page.getByRole('button', { name: 'ثبت وابستگی تأییدنشده پزشک آزمایشی' }).click();
  await page.getByRole('button', { name: 'تخصیص آزمایشی پزشک به بخش' }).click();
  await page.getByRole('button', { name: 'پایان وابستگی آزمایشی پزشک' }).click();
  await page.getByRole('button', { name: 'بازخوانی نخستین صفحه تاریخچه' }).click();
  const timeline = page.getByRole('region', { name: 'رخدادهای صفحه فعلی' });
  await expect(timeline.locator('li')).toHaveCount(2);
  await expect(timeline).toContainText('نسخه 1: تعریف بخش');
  await expect(timeline).toContainText('نسخه 2: وابستگی پزشک در انتظار بررسی');
  await page.getByRole('button', { name: 'صفحه بعد رخدادها' }).click();
  await expect(timeline.locator('li')).toHaveCount(2);
  await expect(timeline).toContainText('نسخه 3: انتساب به بخش');
  await expect(timeline).toContainText('نسخه 4: پایان وابستگی');
  await expect(page.getByRole('button', { name: 'صفحه بعد رخدادها' })).toBeDisabled();
  await page.getByLabel('شماره نسخه برای بازسازی (از صفر تا نسخه جاری)').fill('3');
  await page.getByRole('button', { name: 'بازسازی نسخه انتخابی' }).click();
  const historical = page.getByRole('region', { name: 'نمای بازسازی‌شده نسخه گذشته' });
  await expect(historical).toContainText('وضعیت در نسخه 3');
  await expect(historical).toContainText(department);
  await expect(historical).toContainText(doctor);
  await expect(historical).toContainText('در انتظار بررسی مستقل');
  await page.getByLabel('شماره نسخه برای بازسازی (از صفر تا نسخه جاری)').fill('4');
  await page.getByRole('button', { name: 'بازسازی نسخه انتخابی' }).click();
  await expect(historical).toContainText('وضعیت در نسخه 4');
  await expect(historical).toContainText('پایان‌یافته');
  await expect(historical).toContainText('بخش‌های منتسب: هیچ');
});

test('CENTER-HISTORY-02: audit-derived historical reads never mutate or authorize rights', async ({ request }) => {
  const center = unique('history'), dep = unique('department');
  const url = '/internal/demo/v1/centers/' + encodeURIComponent(center);
  const cmd = { requestId: crypto.randomUUID(), expectedVersion: 0, kind: 'DEFINE_DEPARTMENT',
    departmentRef: dep, doctorRef: null };
  expect((await request.post(url + '/commands', {data: cmd})).status()).toBe(200);
  const history = await request.get(url + '/history?afterVersion=0&limit=1');
  expect(history.status()).toBe(200);
  const h = await history.json();
  expect(h.syntheticOnly).toBe(true);
  expect(h.managerAuthenticated).toBe(false);
  expect(h.professionalQualificationVerified).toBe(false);
  expect(h.nationalReleaseAllowed).toBe(false);
  expect(h.events).toHaveLength(1);
  expect(h.events[0].version).toBe(1);
  expect(h.events[0].hypotheticalOnly).toBe(true);
  expect(h.hasMore).toBe(false);
  const at = await request.get(url + '/versions/0');
  expect(at.status()).toBe(200);
  const old = await at.json();
  expect(old.asOfVersion).toBe(0);
  expect(old.currentVersion).toBe(1);
  expect(old.snapshot.departments).toHaveLength(0);
  expect(old.snapshot.managerAuthenticated).toBe(false);
  expect((await (await request.get(url)).json()).currentVersion).toBe(1);
});

test('CENTER-HISTORY-03: strict cursor/version input and unexpected query are denied', async ({ request }) => {
  const base = '/internal/demo/v1/centers/' + encodeURIComponent(unique('bad-history'));
  for (const route of [
    '/history?afterVersion=-1&limit=2',
    '/history?afterVersion=0&limit=21',
    '/history?afterVersion=0&limit=0',
    '/history?afterVersion=0&limit=2&approval=true',
    '/history?afterVersion=0&afterVersion=0&limit=2',
    '/history?afterVersion=9223372036854775807&limit=2',
    '/versions/-1', '/versions/99', '/versions/0?actor=admin'
  ]) {
    const response = await request.get(base + route);
    expect(response.status(), route).toBe(400);
    const data = await response.json();
    expect(data.nationalReleaseAllowed).toBe(false);
    expect(data.managerAuthenticated).toBe(false);
  }
  const real = await request.get('/internal/demo/v1/centers/real:center/history?afterVersion=0&limit=2');
  expect(real.status()).toBe(400);
});

test('CENTER-HISTORY-04: events isolated by center even when another center advances', async ({ request }) => {
  const base = '/internal/demo/v1/centers/';
  const a = unique('isolated-a'), b = unique('isolated-b');
  const payload = {requestId: crypto.randomUUID(), expectedVersion: 0, kind: 'DEFINE_DEPARTMENT',
    departmentRef: unique('department'), doctorRef: null};
  expect((await request.post(base + encodeURIComponent(a) + '/commands', { data: payload })).status()).toBe(200);
  const bHistory = await (await request.get(base + encodeURIComponent(b) +
    '/history?afterVersion=0&limit=2')).json();
  expect(bHistory.events).toHaveLength(0);
  expect(bHistory.currentVersion).toBe(0);
  expect((await (await request.get(base + encodeURIComponent(b) + '/versions/0')).json())
    .snapshot.departments).toHaveLength(0);
  const aHistory = await (await request.get(base + encodeURIComponent(a) +
    '/history?afterVersion=0&limit=2')).json();
  expect(aHistory.events).toHaveLength(1);
});

test('CENTER-HISTORY-05: network failure and new write clear stale history/old previews', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'بازخوانی نخستین صفحه تاریخچه' }).click();
  await expect(page.getByRole('region', { name: 'رخدادهای صفحه فعلی' })).toBeVisible();
  await page.getByRole('button', { name: 'بازسازی نسخه انتخابی' }).click();
  await expect(page.getByRole('region', { name: 'نمای بازسازی‌شده نسخه گذشته' })).toBeVisible();
  await page.getByRole('button', { name: 'ثبت بخش آزمایشی' }).click();
  await expect(page.getByRole('region', { name: 'رخدادهای صفحه فعلی' })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'نمای بازسازی‌شده نسخه گذشته' })).toHaveCount(0);
  await page.route('**/history?*', route => route.abort());
  await page.getByRole('button', { name: 'بازخوانی نخستین صفحه تاریخچه' }).click();
  await expect(page.getByRole('status').last()).toContainText('یکپارچگی داده تأیید نشد');
  await expect(page.getByRole('region', { name: 'رخدادهای صفحه فعلی' })).toHaveCount(0);
});


