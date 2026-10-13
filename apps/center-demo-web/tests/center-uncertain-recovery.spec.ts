import {expect, test} from '@playwright/test';

const unique = (name: string) => 'test:' + name + '-' + crypto.randomUUID().replaceAll('-', '');
const prefix = (center: string) => '/internal/demo/v1/centers/' + encodeURIComponent(center);
async function openCenter(page: import('@playwright/test').Page) {
  const center = unique('retry-center'), department = unique('retry-ward');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByRole('button', {name: 'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return {center, department};
}
test('CENTER-97-01: lost ACK after real PG commit manually replays exact command without duplication', async ({page, request}) => {
  const {center, department} = await openCenter(page);
  const payloads: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands', async route => {
    payloads.push(route.request().postData() ?? '');
    if (payloads.length === 1) {
      const response = await route.fetch();  // real Kestrel/PG commits first
      expect(response.status()).toBe(200);
      expect((await response.json()).outcome).toBe('Applied');
      await route.abort('failed');             // only the browser ACK is lost
    } else await route.continue();
  });
  await page.getByRole('button', {name: 'ثبت بخش آزمایشی'}).click();
  const recovery = page.getByRole('alert', {name: 'بازیابی دستور با نتیجه نامعلوم'});
  await expect(recovery).toBeVisible();
  await expect(page.getByRole('button', {name: 'ثبت بخش آزمایشی'})).toBeDisabled();
  await expect(page.getByLabel('شناسه مرکز آزمایشی')).toBeDisabled();
  await expect(page.getByTestId('center-guided-step')).toContainText('نامعلوم');
  await expect(page.getByRole('region', {name: 'وضعیت ذخیره‌شده آزمایشی'})).toHaveCount(0);
  let stored = await (await request.get(prefix(center))).json();
  expect(stored.currentVersion).toBe(1);
  expect(stored.eventCount).toBe(1);
  expect(stored.departments).toEqual([department]);
  await page.getByRole('button', {name: 'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(recovery).toBeVisible();
  await expect(page.getByRole('button', {name: 'ثبت بخش آزمایشی'})).toBeDisabled();
  await recovery.getByRole('button', {name: 'تلاش دستی مجدد با همان درخواست'}).click();
  await expect(recovery).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('هیچ رخداد جدیدی ایجاد نشد');
  expect(payloads).toHaveLength(2);
  expect(payloads[1]).toBe(payloads[0]);
  stored = await (await request.get(prefix(center))).json();
  expect(stored.currentVersion).toBe(1);
  expect(stored.eventCount).toBe(1);
  expect(stored.managerAuthenticated).toBe(false);
  expect(stored.professionalQualificationVerified).toBe(false);
  expect(stored.nationalReleaseAllowed).toBe(false);
});
test('CENTER-97-02: lost request before PG safely sends same byte payload once manually', async ({page,request}) => {
  const {center} = await openCenter(page);
  const bodies: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands', route => {
    bodies.push(route.request().postData() ?? '');
    return bodies.length === 1 ? route.abort('failed') : route.continue();
  });
  await page.getByRole('button', {name: 'ثبت بخش آزمایشی'}).click();
  const recovery = page.getByRole('alert', {name: 'بازیابی دستور با نتیجه نامعلوم'});
  await expect(recovery).toBeVisible();
  expect((await (await request.get(prefix(center))).json()).currentVersion).toBe(0);
  await recovery.getByRole('button', {name: 'تلاش دستی مجدد با همان درخواست'}).click();
  await expect(recovery).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('فقط در پایگاه آزمایشی ثبت شد');
  expect(bodies).toHaveLength(2);
  expect(bodies[1]).toBe(bodies[0]);
  const state = await (await request.get(prefix(center))).json();
  expect(state.currentVersion).toBe(1);
  expect(state.eventCount).toBe(1);
});
test('CENTER-97-03: external writer after dropped request causes conclusive stale 409, never silent apply', async ({page,request}) => {
  const {center,department} = await openCenter(page);
  const seen: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands', route => {
    seen.push(route.request().postData() ?? '');
    return seen.length === 1 ? route.abort('failed') : route.continue();
  });
  await page.getByRole('button', {name: 'ثبت بخش آزمایشی'}).click();
  const recovery = page.getByRole('alert', {name: 'بازیابی دستور با نتیجه نامعلوم'});
  await expect(recovery).toBeVisible();
  const different = unique('external-ward');
  const outside = await request.post(prefix(center) + '/commands', {data: {
    requestId: crypto.randomUUID(), expectedVersion: 0,
    kind: 'DEFINE_DEPARTMENT', departmentRef: different, doctorRef: null
  }});
  expect(outside.status()).toBe(200);
  await recovery.getByRole('button', {name: 'تلاش دستی مجدد با همان درخواست'}).click();
  await expect(recovery).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('StaleRevision');
  expect(seen).toHaveLength(2);
  expect(seen[1]).toBe(seen[0]);
  const stored = await (await request.get(prefix(center))).json();
  expect(stored.departments).toEqual([different]);
  expect(stored.departments).not.toContain(department);
  expect(stored.currentVersion).toBe(1);
  expect(stored.eventCount).toBe(1);
});
test('CENTER-97-04: invalid/forged response cannot clear uncertainty, browser reload does not auto-POST', async ({page,request}) => {
  const {center} = await openCenter(page);
  let calls = 0;
  await page.route('**/internal/demo/v1/centers/**/commands', route => {
    calls++;
    if (calls === 1) return route.fulfill({status:200, contentType:'application/json',
      body:JSON.stringify({contract:'TRN-TECH-073',syntheticOnly:true,centerRef:center,
        currentVersion:1,eventCount:1,departments:[],doctors:[],
        managerAuthenticated:true,professionalQualificationVerified:false,
        nationalReleaseAllowed:false,outcome:'Applied'})});
    return route.continue();
  });
  await page.getByRole('button', {name: 'ثبت بخش آزمایشی'}).click();
  await expect(page.getByRole('alert', {name:'بازیابی دستور با نتیجه نامعلوم'})).toBeVisible();
  await expect(page.getByRole('region', {name:'وضعیت ذخیره‌شده آزمایشی'})).toHaveCount(0);
  expect((await (await request.get(prefix(center))).json()).currentVersion).toBe(0);
  await page.getByRole('alert', {name:'بازیابی دستور با نتیجه نامعلوم'})
    .getByRole('button', {name:'تلاش دستی مجدد با همان درخواست'}).click();
  await expect(page.getByRole('alert', {name:'بازیابی دستور با نتیجه نامعلوم'})).toHaveCount(0);
  const current = await (await request.get(prefix(center))).json();
  expect(current.currentVersion).toBe(1);
  expect(current.eventCount).toBe(1);
  await page.reload();
  await expect(page.getByRole('alert', {name:'بازیابی دستور با نتیجه نامعلوم'})).toHaveCount(0);
  expect(calls).toBe(2);
});
