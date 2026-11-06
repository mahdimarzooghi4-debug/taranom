import {test,expect,type Page} from '@playwright/test';

const first={centerRef:'center-live-alpha',revision:3,openSlotCount:2,nextAvailableAt:'2027-11-20T09:00:00Z'};
const second={centerRef:'center-live-beta',revision:2,openSlotCount:1,nextAvailableAt:'2027-11-22T09:00:00Z'};

async function authenticate(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'member-access'}});
}
test('patient home opens live center discovery and only returned centers can be selected',async({page})=>{
 await authenticate(page);
 let discoveryCalls=0;
 await page.route('**/api/v1/me/available-appointment-centers',route=>{
  discoveryCalls++;expect(route.request().headers().authorization).toBe('Bearer member-access');
  return route.fulfill({json:{centers:[first,second]}});
 });
 await page.goto('/patient-app');
 await page.getByRole('link',{name:'گرفتن نوبت'}).click();
 await expect(page).toHaveURL(/\/patient-app\/booking$/);
 await expect(page.getByRole('heading',{name:'مراکز دارای ظرفیت'})).toBeVisible();
 await expect(page.getByText('center-live-alpha')).toBeVisible();
 await expect(page.getByText('دکتر سارا رضایی')).toHaveCount(0);
 expect(discoveryCalls).toBe(1);
 await page.getByRole('searchbox',{name:'جست‌وجو بر اساس مرجع مرکز'}).fill('beta');
 await expect(page.getByText('center-live-alpha')).toHaveCount(0);
 await expect(page.getByText('center-live-beta')).toBeVisible();
 await page.getByRole('link',{name:'انتخاب زمان این مرکز'}).click();
 await expect(page).toHaveURL(/\/patient-app\/slots\?center=center-live-beta$/);
});
test('unauthenticated discovery never fetches and cannot expose sample providers',async({page})=>{
 let requests=0;page.on('request',r=>{if(r.url().includes('/api/'))requests++});
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
 expect(requests).toBe(0);
});
test('revoked member access shows denial without stale center data',async({page})=>{
 await authenticate(page);
 let count=0;
 await page.route('**/api/v1/me/available-appointment-centers',route=>{
  count++;return count===1?route.fulfill({json:{centers:[first]}}):route.fulfill({status:403,json:{code:'Forbidden'}});
 });
 await page.goto('/patient-app/booking');
 await expect(page.getByText('center-live-alpha')).toBeVisible();
 await page.reload();
 await expect(page.getByRole('alert')).toContainText('دسترسی نوبت');
 await expect(page.getByText('center-live-alpha')).toHaveCount(0);
});
test('empty or malformed catalog fails closed with no selectable center',async({page})=>{
 await authenticate(page);
 await page.route('**/api/v1/me/available-appointment-centers',route=>route.fulfill({json:{centers:[]}}));
 await page.goto('/patient-app/booking');
 await expect(page.getByText('در حال حاضر ظرفیت آزادی در برنامه‌های ثبت‌شده یافت نشد.')).toBeVisible();
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
 await page.unroute('**/api/v1/me/available-appointment-centers');
 await page.route('**/api/v1/me/available-appointment-centers',route=>route.fulfill({json:{centers:[{...first,openSlotCount:0}]}}));
 await page.reload();
 await expect(page.getByRole('alert')).toContainText('فهرست ظرفیت مراکز معتبر نیست');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
});
test('mobile discovery fits viewport and loads existing local Figma assets',async({page})=>{
 await page.setViewportSize({width:390,height:844});await authenticate(page);
 await page.route('**/api/v1/me/available-appointment-centers',route=>route.fulfill({json:{centers:[first]}}));
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 expect(await page.locator('.appointments-surface img').evaluateAll(imgs=>imgs.every(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0))).toBeTruthy();
});
