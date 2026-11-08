import {test,expect,type Page} from '@playwright/test';
async function login(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'member-access'}});
}
const path='**/api/v1/me/available-appointment-centers';
test('patient home shows publication hold, not false no-capacity claim',async({page})=>{
 await login(page);let calls=0;
 await page.route(path,route=>{calls++;return route.fulfill({json:{centers:[],publicationGateRequired:true}})});
 await page.goto('/patient-app');
 await page.getByRole('link',{name:'گرفتن نوبت'}).click();
 await expect(page).toHaveURL(/\/patient-app\/booking$/);
 await expect(page.getByRole('status')).toContainText('برای انتشار و نوبت‌گیری عمومی تأیید نشده');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
 expect(calls).toBe(1);
});
test('no session makes no provider discovery API request',async({page})=>{
 let requests=0;page.on('request',r=>{if(r.url().includes('/api/'))requests++});
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');
 expect(requests).toBe(0);
});
test('revoked member grant cannot reveal center',async({page})=>{
 await login(page);await page.route(path,route=>route.fulfill({status:403,json:{code:'FORBIDDEN'}}));
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('alert')).toContainText('دسترسی نوبت');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
});
test('nonempty or unmarked publication catalog fails closed',async({page})=>{
 await login(page);
 await page.route(path,route=>route.fulfill({json:{centers:[{
  centerRef:'center-not-approved',revision:1,openSlotCount:2,nextAvailableAt:'2027-11-20T09:00:00Z'
 }],publicationGateRequired:true}}));
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('alert')).toContainText('بدون مجوز انتشار');
 await expect(page.getByRole('link',{name:'انتخاب زمان این مرکز'})).toHaveCount(0);
 await page.unroute(path);await page.route(path,route=>route.fulfill({json:{centers:[]}}));
 await page.reload();
 await expect(page.getByRole('alert')).toContainText('بدون مجوز انتشار');
});
test('publication hold fits mobile viewport',async({page})=>{
 await login(page);await page.setViewportSize({width:390,height:844});
 await page.route(path,route=>route.fulfill({json:{centers:[],publicationGateRequired:true}}));
 await page.goto('/patient-app/booking');
 await expect(page.getByRole('status')).toContainText('برای انتشار و نوبت‌گیری عمومی تأیید نشده');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
