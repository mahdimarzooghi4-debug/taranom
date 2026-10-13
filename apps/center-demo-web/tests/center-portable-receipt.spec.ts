import {test, expect} from '@playwright/test';
import type {Page} from '@playwright/test';

const unique = (tag: string) => 'test:' + tag + '-' + crypto.randomUUID().replaceAll('-', '');
const api = (center: string) => '/internal/demo/v1/centers/' + encodeURIComponent(center);

async function setup(page: Page) {
  const center = unique('portable-center');
  const department = unique('portable-ward');
  await page.goto('/');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(department);
  await page.getByRole('button', {name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  return {center, department};
}
const uncertain = (page: Page) =>
  page.getByRole('alert', {name:'بازیابی دستور با نتیجه نامعلوم'});
const exportText = (page: Page) =>
  page.getByLabel('متن رسید آزمایشی برای کپی دستی').inputValue();
async function importOnly(page: Page, text: string) {
  await page.getByLabel('چسباندن رسید آزمایشی پس از بارگذاری مجدد').fill(text);
  await page.getByRole('button', {name:'بارگذاری رسید بدون ارسال'}).click();
}
const retry = (page: Page) =>
  uncertain(page).getByRole('button',{name:'تلاش دستی مجدد با همان درخواست'});

test('CENTER-98-01: after real PG commit and lost ACK, portable exact-body replay after reload', async ({page,request}) => {
  const {center,department} = await setup(page);
  const posted: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands',async route=>{
    posted.push(route.request().postData() ?? '');
    if (posted.length === 1) {
      const committed=await route.fetch();
      expect(committed.status()).toBe(200);
      expect((await committed.json()).outcome).toBe('Applied');
      await route.abort('failed');
    } else await route.continue();
  });
  await page.getByRole('button',{name:'ثبت بخش آزمایشی'}).click();
  await expect(uncertain(page)).toBeVisible();
  const original = await exportText(page);
  const receipt = JSON.parse(original);
  expect(receipt.contract).toBe('TRN-TECH-107');
  expect(receipt.syntheticOnly).toBe(true);
  expect(receipt.centerRef).toBe(center);
  expect(receipt.originalBody).toBe(posted[0]);
  const before=await (await request.get(api(center))).json();
  expect(before.currentVersion).toBe(1);
  expect(before.eventCount).toBe(1);
  expect(before.departments).toEqual([department]);
  // A source GET is informational; it MUST NOT unfreeze another command.
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(uncertain(page)).toBeVisible();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'})).toHaveCount(0);
  await page.reload();
  await expect(uncertain(page)).toHaveCount(0);
  expect(posted).toHaveLength(1);
  await importOnly(page,original);
  await expect(uncertain(page)).toBeVisible();
  await expect(page.getByTestId('center-receipt-feedback')).toContainText('هیچ POST انجام نشده');
  expect(posted).toHaveLength(1);
  await expect(page.getByLabel('شناسه مرکز آزمایشی')).toHaveValue(center);
  await expect(page.getByRole('button',{name:'ثبت بخش آزمایشی'})).toBeDisabled();
  await retry(page).click();
  await expect(uncertain(page)).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('هیچ رخداد جدیدی ایجاد نشد');
  expect(posted).toHaveLength(2);
  expect(posted[0]).toBe(posted[1]);
  const after=await (await request.get(api(center))).json();
  expect(after.currentVersion).toBe(1);
  expect(after.eventCount).toBe(1);
  expect(after.professionalQualificationVerified).toBe(false);
  expect(after.nationalReleaseAllowed).toBe(false);
});

test('CENTER-98-02: receipt preserved across reload when original POST never reached server', async ({page,request}) => {
  const {center} = await setup(page);
  const bodies: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands',route=>{
    bodies.push(route.request().postData() ?? '');
    return bodies.length===1?route.abort('failed'):route.continue();
  });
  await page.getByRole('button',{name:'ثبت بخش آزمایشی'}).click();
  await expect(uncertain(page)).toBeVisible();
  const receipt=await exportText(page);
  expect((await (await request.get(api(center))).json()).currentVersion).toBe(0);
  await page.reload();
  await importOnly(page,receipt);
  await expect(uncertain(page)).toBeVisible();
  expect(bodies).toHaveLength(1);
  await retry(page).click();
  await expect(uncertain(page)).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('فقط در پایگاه آزمایشی ثبت شد');
  expect(bodies).toHaveLength(2);
  expect(bodies[0]).toBe(bodies[1]);
  const final=await (await request.get(api(center))).json();
  expect(final.currentVersion).toBe(1);
  expect(final.eventCount).toBe(1);
});

test('CENTER-98-03: imported original loses race to separate actual center writer, exact stale denial', async ({page,request}) => {
  const {center,department} = await setup(page);
  const bodies: string[] = [];
  await page.route('**/internal/demo/v1/centers/**/commands',route=>{
    bodies.push(route.request().postData() ?? '');
    return bodies.length===1?route.abort('failed'):route.continue();
  });
  await page.getByRole('button',{name:'ثبت بخش آزمایشی'}).click();
  await expect(uncertain(page)).toBeVisible();
  const receipt=await exportText(page);
  await page.reload();
  const other=unique('other-ward');
  const external=await request.post(api(center)+'/commands',{data:{
    requestId:crypto.randomUUID(),expectedVersion:0,kind:'DEFINE_DEPARTMENT',
    departmentRef:other,doctorRef:null
  }});
  expect(external.status()).toBe(200);
  await importOnly(page,receipt);
  await retry(page).click();
  await expect(uncertain(page)).toHaveCount(0);
  await expect(page.getByRole('status').first()).toContainText('StaleRevision');
  expect(bodies).toHaveLength(2);
  expect(bodies[0]).toBe(bodies[1]);
  const latest=await (await request.get(api(center))).json();
  expect(latest.departments).toEqual([other]);
  expect(latest.departments).not.toContain(department);
  expect(latest.eventCount).toBe(1);
});

test('CENTER-98-04: strict forged, real-scope, noncanonical, stale envelopes denied without POST',async ({page})=>{
  const {center} = await setup(page);
  await page.route('**/internal/demo/v1/centers/**/commands',route=>route.abort('failed'));
  await page.getByRole('button',{name:'ثبت بخش آزمایشی'}).click();
  await expect(uncertain(page)).toBeVisible();
  const valid=await exportText(page);
  const envelope=JSON.parse(valid);
  const body=JSON.parse(envelope.originalBody);
  await page.reload();
  let posts=0;
  page.on('request',req=>{if(req.method()==='POST')posts++;});
  const variants=[
    JSON.stringify({...envelope,centerRef:'real:center'}),
    JSON.stringify({...envelope,syntheticOnly:false}),
    JSON.stringify({...envelope,managerAuthenticated:true}),
    JSON.stringify({...envelope,contract:'TRN-TECH-092'}),
    JSON.stringify({...envelope,originalBody:JSON.stringify({...body,requestId:'00000000-0000-4000-8000-000000000000',doctorRef:'test:forged'})}),
    JSON.stringify({...envelope,originalBody:JSON.stringify({...body,expectedVersion:-1})}),
    JSON.stringify({...envelope,originalBody:JSON.stringify({...body,departmentRef:'real:ward'})}),
    JSON.stringify({...envelope,originalBody:JSON.stringify({...body,kind:'END_DOCTOR'})}),
    JSON.stringify({...envelope,originalBody:JSON.stringify({...body,managerAuthenticated:true})}),
    JSON.stringify({...envelope,originalBody:' '+envelope.originalBody}),
    valid.replace('"syntheticOnly":true','"syntheticOnly":true,"syntheticOnly":true'),
    JSON.stringify({...envelope,originalBody:'{'}),
    '{}',
  ];
  for(const input of variants){
    await importOnly(page,input);
    await expect(page.getByTestId('center-receipt-feedback')).toContainText('رسید رد شد');
    await expect(uncertain(page)).toHaveCount(0);
    await expect(page.getByLabel('شناسه مرکز آزمایشی')).toHaveValue('test:center-demo');
    await expect(page.getByRole('button',{name:'ثبت بخش آزمایشی'})).toBeDisabled();
  }
  expect(posts).toBe(0);
  expect(await page.evaluate(()=>({
    local:localStorage.length,session:sessionStorage.length
  }))).toEqual({local:0,session:0});
  // Valid manual import remains possible after all negative attempts.
  await importOnly(page,valid);
  await expect(uncertain(page)).toBeVisible();
  await expect(page.getByLabel('شناسه مرکز آزمایشی')).toHaveValue(center);
  expect(posts).toBe(0);
  await page.reload();
  await expect(uncertain(page)).toHaveCount(0);
  expect(posts).toBe(0);
});
