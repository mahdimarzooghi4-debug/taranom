import { test, expect } from '@playwright/test';
const item={centerRef:'center-test',scheduleRevision:4,bookingRef:'booking-test',memberRef:'member-test',slotRef:'slot-test',startsAt:'2027-11-20T09:00:00Z',state:'Booked'};
test.beforeEach(async({page})=>{
 await page.setViewportSize({width:390,height:844});
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-test-token'}});
});
test('Figma list, filters and detail use authenticated self API',async({page})=>{
 await page.route('**/api/v1/me/appointments',route=>route.fulfill({json:{appointments:[item,{...item,bookingRef:'past-booking',startsAt:'2020-01-01T09:00:00Z'}]}}));
 await page.goto('/patient-app/appointments');
 await expect(page.getByText('booking-test',{exact:true})).toBeVisible();
 await expect(page.getByText('past-booking',{exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'گذشته',exact:true}).click();
 await expect(page.getByText('past-booking',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'آینده',exact:true}).click();
 await page.getByRole('link',{name:'مشاهدهٔ جزئیات نوبت'}).click();
 await expect(page.getByRole('heading',{name:'جزئیات نوبت',exact:true})).toBeVisible();
 await expect(page.getByText('booking-test',{exact:true})).toBeVisible();
 await page.goto('/patient-app/appointment-detail?booking=another-account&center=center-test');
 await expect(page.getByText('نوبت موردنظر در حساب شما پیدا نشد.')).toBeVisible();
});
test('errors clear old data and retry recovers',async({page})=>{
 let denied=false;
 await page.route('**/api/v1/me/appointments',route=>denied?route.fulfill({status:403}):route.fulfill({json:{appointments:[item]}}));
 await page.goto('/patient-app/appointments');await expect(page.getByText('booking-test',{exact:true})).toBeVisible();
 denied=true;await page.getByRole('button',{name:'تازه‌سازی نوبت‌ها'}).click();
 await expect(page.getByRole('alert')).toContainText('دسترسی');
 await expect(page.getByText('booking-test',{exact:true})).toHaveCount(0);
 denied=false;await page.getByRole('button',{name:'تازه‌سازی نوبت‌ها'}).click();
 await expect(page.getByText('booking-test',{exact:true})).toBeVisible();
});
test('cancel confirmation can be dismissed without a write',async({page})=>{
 let posts=0;
 await page.route('**/api/v1/**',route=>{if(route.request().method()==='POST')posts++;return route.fulfill({json:{appointments:[item]}})});
 await page.goto('/patient-app/appointments');
 await page.getByRole('button',{name:'لغو نوبت',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');
 await expect(page.getByRole('dialog')).toHaveCount(0);expect(posts).toBe(0);
});
test('visual geometry and local Figma assets at 390px',async({page})=>{
 await page.route('**/api/v1/me/appointments',route=>route.fulfill({json:{appointments:[item]}}));
 await page.goto('/patient-app/appointments');await expect(page.getByText('booking-test',{exact:true})).toBeVisible();
 await page.evaluate(()=>document.fonts.ready);
 const assets=await page.locator('.appointments-surface img').evaluateAll(images=>images.map(i=>{const img=i as HTMLImageElement;const r=img.getBoundingClientRect();return {loaded:img.complete&&img.naturalWidth>0,width:r.width,height:r.height,source:img.getAttribute('src')}}));
 expect(assets).toHaveLength(6);expect(assets.every(a=>a.loaded&&a.source?.startsWith('/assets/appointments/'))).toBeTruthy();
 expect(assets[0].width).toBe(140);expect(assets[0].height).toBe(63);
 expect(assets.slice(2).every(a=>a.width===24&&a.height===24)).toBeTruthy();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await page.screenshot({path:'/tmp/taranom-appointments-390.png',fullPage:true});
});
