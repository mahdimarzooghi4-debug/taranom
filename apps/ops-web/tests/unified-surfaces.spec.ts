import {test,expect} from '@playwright/test';

test('SURFACES-01 real market -> member React bundle, direct reload and denied services',async({page})=>{
  const outside:string[]=[];
  page.on('request',req=>{
    if(new URL(req.url()).origin!=='http://127.0.0.1:4318')outside.push(req.url());
  });
  await page.goto('/markets');
  await expect(page.getByRole('status')).toContainText('BLOCKED_UNVERIFIED');
  await page.getByRole('navigation',{name:'محیط‌های توسعه بدون داده واقعی'})
    .getByRole('link',{name:/پیش‌نمایش عضو/}).click();
  await expect(page).toHaveURL(/\/member$/);
  await expect(page.getByRole('heading',{level:1,name:'فضایی آرام برای پیگیری سلامت'})).toBeVisible();
  await expect(page.getByRole('status').first()).toContainText('متصل نیست');
  await page.goto('/member/credit');
  await expect(page.getByRole('heading',{level:1,name:'اعتبار درمان'})).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading',{level:1,name:'اعتبار درمان'})).toBeVisible();
  await expect(page.getByText(/هیچ عدد پیش‌فرضی وجود ندارد/)).toBeVisible();
  expect(outside).toEqual([]);
});

test('SURFACES-02 real market -> center independent bundle with no real doctor authority',async({page})=>{
  const outside:string[]=[];
  page.on('request',req=>{
    if(new URL(req.url()).origin!=='http://127.0.0.1:4318')outside.push(req.url());
  });
  await page.goto('/markets');
  await expect(page.getByRole('status')).toContainText('BLOCKED_UNVERIFIED');
  await page.getByRole('navigation',{name:'محیط‌های توسعه بدون داده واقعی'})
    .getByRole('link',{name:/پیش‌نمایش مرکز/}).click();
  await expect(page).toHaveURL(/\/center$/);
  await expect(page.getByRole('heading',{level:1})).toBeVisible();
  await expect(page.getByText('هیچ پزشک یا مرکز واقعی در این محیط ثبت نشده است.')).toBeVisible();
  await page.goto('/center/doctors');
  await expect(page.getByRole('heading',{level:1,name:'پزشکان وابسته'})).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading',{level:1,name:'پزشکان وابسته'})).toBeVisible();
  await expect(page.getByText(/بدون ورود واقعی، بدون پزشک نمونه، بدون اطلاعات بیمار/)).toBeVisible();
  expect(outside).toEqual([]);
});

test('SURFACES-03 no fake login/unknown pages and hostile authority 403',async({request})=>{
  for(const path of ['/member/login','/member/private','/center/patients',
     '/center/register','/api/v1/member/login','/api/v1/member/wallet']){
    const res=await request.get(path);
    expect(res.status(),path).toBe(404);
    expect((await res.text()).toLowerCase()).not.toContain('<html');
  }
  for(const path of ['/member','/center','/center/doctors']){
    expect((await request.post(path,{data:{memberAuthenticated:true}})).status()).toBe(405);
    const denied=await request.get(path,{headers:{Host:'evil.example:4318'}});
    expect(denied.status()).toBe(403);
    expect((await denied.json()).nationalReleaseAllowed).toBe(false);
  }
});
