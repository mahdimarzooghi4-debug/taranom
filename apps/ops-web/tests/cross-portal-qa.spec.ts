import {test,expect} from '@playwright/test';

test.skip(process.env.TARANOM_CI_CROSS_PORTAL !== '1',
  'Real two-host PostgreSQL handoff is permitted only in the isolated CI service job');

const op='http://127.0.0.1:4318';
const persistent='http://127.0.0.1:4320';
function synthetic(label:string){return 'test:'+label+'-'+crypto.randomUUID().replaceAll('-','');}

test('CROSS-PORTAL-01: real member preview to isolated support-case journal and back',async({page})=>{
  const offsite:string[]=[];
  page.on('request',req=>{
    const target=new URL(req.url());
    if(![op,persistent].includes(target.origin))offsite.push(req.url());
  });
  await page.goto(op+'/markets');
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
  await page.getByRole('navigation',{name:'محیط‌های توسعه بدون داده واقعی'})
    .getByRole('link',{name:/پیش‌نمایش عضو/}).click();
  await page.goto(op+'/member/support');
  await expect(page.getByRole('heading',{level:1,name:'پشتیبانی و شکایات'})).toBeVisible();
  await page.getByRole('navigation',{name:'گردش‌کار آزمایشی ایزوله'})
    .getByRole('link',{name:'آزمایش پرونده پشتیبانی با PostgreSQL'}).click();
  await expect(page).toHaveURL(persistent+'/member-demo/');
  const caseRef=synthetic('cross-support');
  await page.getByLabel('پرونده آزمایشی').fill(caseRef);
  await page.getByLabel('شناسه عضو فرضی').fill(synthetic('member'));
  await page.getByLabel('ارجاع مدرک تأییدنشده').fill(synthetic('evidence'));
  await page.getByLabel('ثبت‌کننده ادعایی').fill(synthetic('submitter'));
  await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
  await expect(page.getByTestId('support-status')).toContainText('PostgreSQL خوانده شد');
  await page.getByRole('button',{name:'ثبت درخواست اولیه آزمایشی'}).click();
  await expect(page.getByRole('region',{name:'وضعیت پرونده پشتیبانی آزمایشی'}))
    .toContainText('نسخه: 1');
  await page.reload();
  await page.getByLabel('پرونده آزمایشی').fill(caseRef);
  await page.getByRole('button',{name:'بازخوانی پرونده پشتیبانی آزمایشی'}).click();
  await expect(page.getByRole('region',{name:'وضعیت پرونده پشتیبانی آزمایشی'}))
    .toContainText('نسخه: 1');
  await expect(page.getByRole('region',{name:'وضعیت پرونده پشتیبانی آزمایشی'}))
    .toContainText('شکایت رسمی: خیر');
  await page.goto(op+'/member/support');
  await expect(page.getByRole('heading',{level:1,name:'پشتیبانی و شکایات'})).toBeVisible();
  await expect(page.getByText('اتصال عضو هنوز برقرار نیست')).toBeVisible();
  expect(offsite).toEqual([]);
});

test('CROSS-PORTAL-02: real center preview to protected event journal and back',async({page})=>{
  const offsite:string[]=[];
  page.on('request',req=>{
    const target=new URL(req.url());
    if(![op,persistent].includes(target.origin))offsite.push(req.url());
  });
  await page.goto(op+'/center');
  await expect(page.getByText('مدیر مرکز هنوز احراز نشده است')).toBeVisible();
  await page.getByRole('navigation',{name:'گردش‌کار آزمایشی ایزوله'})
    .getByRole('link',{name:'آزمایش رویدادهای مرکز با PostgreSQL'}).click();
  await expect(page).toHaveURL(persistent+'/');
  const center=synthetic('cross-center'),dept=synthetic('dept'),doctor=synthetic('doc');
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByLabel('شناسه بخش آزمایشی').fill(dept);
  await page.getByLabel('شناسه پزشک آزمایشی').fill(doctor);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('status').first()).toContainText('PostgreSQL خوانده شد');
  await page.getByRole('button',{name:'ثبت بخش آزمایشی'}).click();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
    .toContainText('نسخه ثبت‌شده: 1');
  await page.reload();
  await page.getByLabel('شناسه مرکز آزمایشی').fill(center);
  await page.getByRole('button',{name:'بازخوانی مرکز از PostgreSQL'}).click();
  await expect(page.getByRole('region',{name:'وضعیت ذخیره‌شده آزمایشی'}))
    .toContainText('نسخه ثبت‌شده: 1');
  await expect(page.getByRole('note')).toContainText('DENIED_BY_DEFAULT');
  await page.goto(op+'/center');
  await expect(page.getByText('مدیر مرکز هنوز احراز نشده است')).toBeVisible();
  expect(offsite).toEqual([]);
});

test('CROSS-PORTAL-03: no cross-host cookie, fake login or official access',async({request})=>{
  const bogus=await request.get(op+'/api/v1/member/login');
  expect(bogus.status()).toBe(404);
  const remote=await request.get(persistent+'/internal/demo/v1/support/real:member');
  expect(remote.status()).toBe(400);
  const spoof=await request.get(persistent+'/member-demo/',{
    headers:{Host:'evil.example:4320'}});
  expect(spoof.status()).toBe(403);
  expect((await spoof.json()).memberAuthenticated).toBe(false);
  const states=await request.get(op+'/internal/ops/v1/status');
  expect((await states.json()).releaseAllowed).toBe(false);
});
