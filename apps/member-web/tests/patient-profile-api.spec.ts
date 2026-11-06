import {test,expect,type Page} from '@playwright/test';
const initial={revision:2,fullName:'کاربر آزمایشی',birthDate:'1990-03-21',city:'تهران'};
async function api(page:Page,mode:'ok'|'conflict'|'lost'='ok'){
 let profile={...initial},puts=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-profile-token'}});
 await page.route('**/api/v1/me/profile',async route=>{
  const r=route.request();expect(r.headers().authorization).toBe('Bearer synthetic-profile-token');
  if(r.method()==='PUT'){
   puts++;const body=r.postDataJSON();expect(body.expectedRevision).toBe(2);expect(body.requestId).toBeTruthy();expect(body.nationalId).toBeUndefined();expect(body.phone).toBeUndefined();
   if(mode==='conflict')return route.fulfill({status:409});
   profile={revision:3,fullName:body.fullName.trim(),birthDate:body.birthDate,city:body.city.trim()};
   if(mode==='lost')return route.abort('failed');
   return route.fulfill({json:{outcome:'Applied',revision:3}});
  }
  return route.fulfill({json:{profile}});
 });
 return {puts:()=>puts};
}
test('mobile profile saves declared fields and locked fields cannot be edited',async({page})=>{
 const server=await api(page);await page.goto('/patient-app/edit-info');
 await page.getByLabel('نام و نام خانوادگی').fill('نام ویرایش شده');await page.getByLabel('شهر محل سکونت').fill('مشهد');
 await expect(page.getByLabel('شماره همراه')).toBeDisabled();await expect(page.getByLabel('کد ملی')).toBeDisabled();
 await page.getByRole('button',{name:'ذخیرهٔ تغییرات'}).click();await expect(page.getByRole('status')).toHaveText('تغییرات ذخیره شد.');expect(server.puts()).toBe(1);
 await page.reload();await expect(page.getByLabel('نام و نام خانوادگی')).toHaveValue('نام ویرایش شده');
 await page.goto('/patient-app/personal-info');await expect(page.getByLabel('شهر محل سکونت')).toHaveValue('مشهد');await expect(page.getByLabel('نام و نام خانوادگی')).toHaveJSProperty('readOnly',true);
});
test('concurrent edit conflict requires refreshed profile before another save',async({page})=>{
 await api(page,'conflict');await page.goto('/patient-app/edit-info');await page.getByLabel('نام و نام خانوادگی').fill('تغییر');await page.getByRole('button',{name:'ذخیرهٔ تغییرات'}).click();
 await expect(page.getByRole('alert')).toContainText('در جای دیگری');await expect(page.getByRole('button',{name:'ذخیرهٔ تغییرات'})).toBeDisabled();
 await page.getByRole('button',{name:'دریافت آخرین مشخصات'}).click();await expect(page.getByLabel('نام و نام خانوادگی')).toHaveValue(initial.fullName);await expect(page.getByRole('button',{name:'ذخیرهٔ تغییرات'})).toBeEnabled();
});
test('lost save response reconciles without a second write',async({page})=>{
 const server=await api(page,'lost');await page.goto('/patient-app/edit-info');await page.getByLabel('شهر محل سکونت').fill('یزد');await page.getByRole('button',{name:'ذخیرهٔ تغییرات'}).click();
 await page.getByRole('button',{name:'بررسی نتیجه ذخیره'}).click();await expect(page.getByRole('status')).toHaveText('ذخیرهٔ تغییرات تأیید شد.');expect(server.puts()).toBe(1);
});
test('web profile uses the same self endpoint',async({page})=>{
 const server=await api(page);await page.goto('/patient-portal/account');await page.getByLabel('شهر محل سکونت').fill('کرمان');await page.getByRole('button',{name:'ذخیرهٔ تغییرات'}).click();
 await expect(page.getByRole('status')).toHaveText('تغییرات ذخیره شد.');expect(server.puts()).toBe(1);
});
test('missing login cannot load or save profile',async({page})=>{
 let requests=0;page.on('request',r=>{if(r.url().includes('/api/'))requests++});await page.goto('/patient-app/edit-info');
 await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(requests).toBe(0);await expect(page.getByRole('button',{name:'ذخیرهٔ تغییرات'})).toHaveCount(0);
});
test('390px profile follows Figma fields and uses account navigation assets',async({page})=>{
 await api(page);await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/edit-info');await expect(page.getByLabel('نام و نام خانوادگی')).toHaveValue(initial.fullName);await page.evaluate(()=>document.fonts.ready);
 await expect(page.getByRole('navigation').getByRole('link',{name:'حساب من'})).toHaveAttribute('aria-current','page');
 expect(await page.locator('.appointments-surface img').evaluateAll(images=>images.every(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0))).toBeTruthy();
 await page.screenshot({path:'/tmp/taranom-profile-390.png',fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
