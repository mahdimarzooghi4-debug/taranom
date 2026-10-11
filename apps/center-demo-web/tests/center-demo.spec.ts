import { expect, test } from '@playwright/test';

function unique(prefix: string) {
  return 'test:' + prefix + '-' + crypto.randomUUID().replaceAll('-', '');
}
async function setup(page: import('@playwright/test').Page) {
  await page.goto('/center-demo');
  const center = unique('center'), department = unique('dept'), doctor = unique('doctor');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status')).toContainText('PostgreSQL خوانده شد');
  return { center, department, doctor };
}
test('CENTER-DEMO-01: true PostgreSQL write/read, versioned complete center workflow', async ({ page }) => {
  const keys = await setup(page);
  await expect(page.getByText('نسخه ثبت‌شده:')).toContainText('نسخه ثبت‌شده:');
  await page.getByRole('button', { name: 'ثبت بخش آزمایشی' }).click();
  await expect(page.getByRole('status')).toContainText('فقط در پایگاه آزمایشی ثبت شد');
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
  await page.goto('/center-demo');
  await page.getByLabel('شناسه مرکز آزمایشی').fill('real:center');
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status')).toContainText('فقط شناسه‌های آزمایشی');
  await expect(page.getByRole('button', { name: 'ثبت بخش آزمایشی' })).toBeDisabled();
});
test('CENTER-DEMO-03: network loss clears privileged-looking state', async ({ page }) => {
  await setup(page);
  await page.route('**/internal/demo/v1/centers/**', route => route.abort());
  await page.getByRole('button', { name: 'بازخوانی مرکز از PostgreSQL' }).click();
  await expect(page.getByRole('status')).toContainText('وضعیت معتبر فرض نمی‌شود');
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
