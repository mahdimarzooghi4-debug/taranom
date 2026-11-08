import {test,expect} from '@playwright/test';
const item={centerRef:'web-center',scheduleRevision:3,bookingRef:'web-booking',memberRef:'web-member',slotRef:'web-slot',startsAt:'2027-11-20T09:00:00Z',state:'Booked'};
test.beforeEach(async({page})=>{await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-web-token'}})});
test('web self appointments, actual counts, details and confirmed cancellation',async({page})=>{
 let cancelled=false,posts=0;
 await page.route('**/api/v1/**',route=>{
  const req=route.request();expect(req.headers().authorization).toBe('Bearer synthetic-web-token');
  if(req.method()==='POST'){
   posts++;expect(req.postDataJSON()).toMatchObject({expectedRevision:3});cancelled=true;
   return route.fulfill({json:{outcome:'Applied',revision:4,appointment:{...item,state:2}}});
  }
  expect(new URL(req.url()).pathname).toBe('/api/v1/me/appointments');
  return route.fulfill({json:{appointments:[{...item,state:cancelled?'CancelledByMember':'Booked'}]}});
 });
 await page.goto('/patient-portal/appointments');
 await expect(page.getByText('web-center',{exact:true})).toBeVisible();
 await expect(page.locator('.appointment-stats article').last().locator('strong')).toHaveText('۱');
 await expect(page.getByText('دکتر سارا رضایی',{exact:true})).toHaveCount(0);
 await page.getByText('جزئیات نوبت',{exact:true}).click();await expect(page.getByText('web-booking',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'لغو نوبت',exact:true}).click();expect(posts).toBe(0);
 await page.getByRole('button',{name:'تأیید لغو نوبت',exact:true}).click();
 await expect(page.getByText('لغوشده توسط شما',{exact:true})).toBeVisible();expect(posts).toBe(1);
 await expect(page.locator('.appointment-stats article').last().locator('strong')).toHaveText('۰');
});
test('web denied refresh removes previous account data',async({page})=>{
 let forbidden=false;await page.route('**/api/v1/me/appointments',route=>forbidden?route.fulfill({status:403}):route.fulfill({json:{appointments:[item]}}));
 await page.goto('/patient-portal/appointments');await expect(page.getByText('web-center',{exact:true})).toBeVisible();
 forbidden=true;await page.getByRole('button',{name:'تازه‌سازی نوبت‌ها'}).click();
 await expect(page.getByRole('alert')).toContainText('دسترسی');await expect(page.getByText('web-center',{exact:true})).toHaveCount(0);
 await expect(page.locator('.appointment-stats article').last().locator('strong')).toHaveText('—');
});
test('empty web history does not show demo patient appointments',async({page})=>{
 await page.route('**/api/v1/me/appointments',route=>route.fulfill({json:{appointments:[]}}));await page.goto('/patient-portal/appointments');
 await expect(page.getByText('نوبت آینده‌ای برای شما ثبت نشده است.')).toBeVisible();await expect(page.getByText('دکتر سارا رضایی',{exact:true})).toHaveCount(0);
});
test('malformed cancellation never claims confirmed success',async({page})=>{
 await page.route('**/api/v1/**',route=>route.request().method()==='POST'?route.fulfill({json:{state:'CancelledByMember'}}):route.fulfill({json:{appointments:[item]}}));
 await page.goto('/patient-portal/appointments');await page.getByRole('button',{name:'لغو نوبت',exact:true}).click();await page.getByRole('button',{name:'تأیید لغو نوبت',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('قابل تأیید نیست');await expect(page.getByText('لغو نوبت ثبت شد.',{exact:true})).toHaveCount(0);
});
test('web 1440px visual review and local assets',async({page})=>{
 await page.setViewportSize({width:1440,height:1080});await page.route('**/api/v1/me/appointments',route=>route.fulfill({json:{appointments:[item]}}));
 await page.goto('/patient-portal/appointments');await expect(page.getByText('web-center',{exact:true})).toBeVisible();await page.evaluate(()=>document.fonts.ready);
 await expect.poll(()=>page.locator('.patient-portal-app img').evaluateAll(images=>images.filter(i=>!(i as HTMLImageElement).complete||!(i as HTMLImageElement).naturalWidth).map(i=>i.getAttribute('src')))).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await page.screenshot({path:'/tmp/taranom-web-appointments.png',fullPage:true});
});
