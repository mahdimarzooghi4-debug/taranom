import {test,expect} from '@playwright/test';
test('PAYER-81-01 Member pays hypothetically in each of the three blocked markets',async({page})=>{
 for(const route of ['/markets/individuals','/markets/support','/markets/corporate']){
  await page.goto(route);
  await expect(page.getByRole('status').first()).toContainText('BLOCKED_UNVERIFIED');
  await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
  await expect(page.getByTestId('payer-preview-status')).toContainText('هیچ قرارداد یا پرداختی احراز نشده');
  const result=page.getByTestId('payer-preview-result');
  await expect(result).toContainText('پرداخت‌کننده فرضی: خود عضو');
  await expect(result).toContainText('فقط خود فرد');
  await expect(result).toContainText('DENIED_BY_DEFAULT');
 }
});
test('PAYER-81-02 Sponsor scenario never creates health or wallet ownership',async({page})=>{
 for(const route of ['/markets/support','/markets/corporate']){
  await page.goto(route);
  await page.getByLabel('پرداخت‌کننده فرضی حق اشتراک').selectOption('SPONSOR');
  await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
  const result=page.getByTestId('payer-preview-result');
  await expect(result).toContainText('حامی صرفاً با قرارداد معتبر');
  await expect(result).toContainText('حق دسترسی حامی به اطلاعات درمانی: ندارد');
  await expect(result).toContainText('کنترل کیف پول توسط حامی: مجاز نیست');
  await expect(result).toContainText('وضعیت قرارداد واقعی حامی: احراز نشده');
  await expect(page.getByRole('button',{name:/ثبت‌نام|شروع پرداخت|فعال‌سازی/})).toHaveCount(0);
 }
});
test('PAYER-81-03 Direct public market has no sponsor payer route',async({page,request})=>{
 await page.goto('/markets/individuals');
 await expect(page.getByLabel('پرداخت‌کننده فرضی حق اشتراک').locator('option')).toHaveCount(1);
 const bad=await request.post('/internal/ops/v1/payer-scenario-preview',{data:{
  marketId:'MARKET-PUBLIC',payerMode:'SPONSOR'
 }});
 expect(bad.status()).toBe(400);
 expect((await bad.json()).nationalReleaseAllowed).toBe(false);
});
test('PAYER-81-04 Forged positive flags, then network outage invalidate previous output',async({page})=>{
 await page.route('**/internal/ops/v1/payer-scenario-preview',async route=>{
  const response=await route.fetch();const v=await response.json();
  v.paymentEnabled=true;v.sponsorClinicalAccessAuthorized=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(v)});
 });
 await page.goto('/markets/support');
 await page.getByLabel('پرداخت‌کننده فرضی حق اشتراک').selectOption('SPONSOR');
 await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
 await expect(page.getByTestId('payer-preview-status')).toContainText('قابل تأیید نیست');
 await expect(page.getByTestId('payer-preview-result')).toHaveCount(0);
 await page.unroute('**/internal/ops/v1/payer-scenario-preview');
 await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
 await expect(page.getByTestId('payer-preview-result')).toBeVisible();
 await page.route('**/internal/ops/v1/payer-scenario-preview',r=>r.abort());
 await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
 await expect(page.getByTestId('payer-preview-status')).toContainText('قابل تأیید نیست');
 await expect(page.getByTestId('payer-preview-result')).toHaveCount(0);
});
test('PAYER-81-05 Changing market/payer clears old scenario; no external calls',async({page})=>{
 const outside:string[]=[];
 page.on('request',r=>{if(new URL(r.url()).origin!=='http://127.0.0.1:4318')outside.push(r.url());});
 await page.goto('/markets/corporate');
 await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
 await expect(page.getByTestId('payer-preview-result')).toBeVisible();
 await page.getByLabel('پرداخت‌کننده فرضی حق اشتراک').selectOption('SPONSOR');
 await expect(page.getByTestId('payer-preview-result')).toHaveCount(0);
 await page.getByRole('button',{name:'بررسی فرضی مرز پرداخت‌کننده'}).click();
 await expect(page.getByTestId('payer-preview-result')).toBeVisible();
 await page.getByLabel('بازار مورد بررسی').selectOption('MARKET-PUBLIC');
 await expect(page.getByTestId('payer-preview-result')).toHaveCount(0);
 await expect(page.getByLabel('پرداخت‌کننده فرضی حق اشتراک')).toHaveValue('MEMBER');
 expect(outside).toEqual([]);
});
