import {test,expect,type Page} from '@playwright/test';
const slot={slotRef:'slot-live',centerRef:'center-live',doctorRef:'doctor-live',startsAt:'2027-11-20T09:00:00Z',endsAt:'2027-11-20T09:30:00Z',state:1};
const booking={centerRef:'center-live',scheduleRevision:8,bookingRef:'booking-live',memberRef:'member-live',slotRef:'slot-live',startsAt:slot.startsAt,state:'Booked'};
async function mock(page:Page,mode:'ok'|'conflict'|'lost'='ok'){
 let posts=0,applied=false;const commands:unknown[]=[];
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'test-session'}});
 await page.route('**/api/v1/**',async route=>{
  const req=route.request(),path=new URL(req.url()).pathname;expect(req.headers().authorization).toBe('Bearer test-session');
  if(path==='/api/v1/me/appointment-scopes')return route.fulfill({json:{appointmentScopes:[{capability:'MemberAppointmentsManage',scopeRef:'member-live'}]}});
  if(path.endsWith('/appointment-slots'))return route.fulfill({json:{centerRef:'center-live',revision:7,slots:mode==='conflict'&&posts?[]:[slot]}});
  if(path==='/api/v1/me/appointments')return route.fulfill({json:{appointments:applied?[booking]:[]}});
  if(req.method()==='POST'&&path.endsWith('/appointments')){
   posts++;commands.push(req.postDataJSON());
   expect(path).toBe('/api/v1/member/members/member-live/centers/center-live/appointments');
   if(mode==='conflict')return route.fulfill({status:409,json:{code:'SlotUnavailable',revision:8}});
   applied=true;if(mode==='lost')return route.abort('failed');
   return route.fulfill({json:{outcome:'Applied',revision:8,appointment:booking}});
  }
  return route.fulfill({status:404});
 });
 return {posts:()=>posts,commands};
}
test('actual capacity selection, review and confirmed booking',async({page})=>{
 const server=await mock(page);await page.goto('/patient-app/slots?center=center-live&member=ignored-untrusted-member');
 await expect(page.getByRole('button',{name:'ادامه پس از انتخاب زمان'})).toBeDisabled();
 await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();
 await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();
 await expect(page.getByRole('heading',{name:'بازبینی نوبت',exact:true})).toBeVisible();expect(server.posts()).toBe(0);
 await page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true}).click();
 await expect(page.getByRole('heading',{name:'ثبت نوبت تأیید شد'})).toBeVisible();expect(server.posts()).toBe(1);
 expect(server.commands[0]).toMatchObject({expectedRevision:7,slotRef:'slot-live'});
 expect((server.commands[0] as {requestId:string}).requestId).toMatch(/^[a-f0-9-]{36}$/);
 await page.getByRole('link',{name:'مشاهدهٔ جزئیات نوبت'}).click();await expect(page.getByText('booking-live',{exact:true})).toBeVisible();
});
test('stale capacity never shows success and reloads open times',async({page})=>{
 const server=await mock(page,'conflict');await page.goto('/patient-app/slots?center=center-live');
 await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();
 await page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('تغییر');await expect(page.getByText('ظرفیت آزادی در این مرکز وجود ندارد.')).toBeVisible();
 expect(server.posts()).toBe(1);await expect(page.getByRole('heading',{name:'ثبت نوبت تأیید شد'})).toHaveCount(0);
});
test('uncertain write reconciles history without another POST',async({page})=>{
 const server=await mock(page,'lost');await page.goto('/patient-app/slots?center=center-live');
 await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();await page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('پاسخ قطعی');await page.getByRole('button',{name:'بررسی نتیجه ثبت'}).click();
 await expect(page.getByRole('heading',{name:'ثبت نوبت تأیید شد'})).toBeVisible();expect(server.posts()).toBe(1);
});
test('missing center does not use demo capacity or send API requests',async({page})=>{
 let apiCalls=0;page.on('request',r=>{if(r.url().includes('/api/'))apiCalls++});await page.goto('/patient-app/slots');
 await expect(page.getByText('ابتدا مرکز مراجعه را انتخاب کنید')).toBeVisible();expect(apiCalls).toBe(0);
});
test('review can return to selection without saving',async({page})=>{
 const server=await mock(page);await page.goto('/patient-app/slots?center=center-live');
 await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();await page.getByRole('button',{name:'تغییر زمان'}).click();
 await expect(page.getByRole('heading',{name:'انتخاب زمان نوبت',exact:true})).toBeVisible();expect(server.posts()).toBe(0);
});
test('390px slot and review screens use local Figma assets',async({page})=>{
 await page.setViewportSize({width:390,height:844});await mock(page);await page.goto('/patient-app/slots?center=center-live');await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').waitFor();await page.evaluate(()=>document.fonts.ready);
 expect(await page.locator('.appointments-surface img').evaluateAll(imgs=>imgs.every(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0))).toBeTruthy();
 await page.screenshot({path:'/tmp/taranom-slots-390.png',fullPage:true});
 await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();await page.screenshot({path:'/tmp/taranom-review-390.png',fullPage:true});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
test('no active member grant cannot request capacity or book',async({page})=>{
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'test-session'}});
 let capacityCalls=0;
 await page.route('**/api/v1/**',route=>{if(route.request().url().includes('/appointment-slots'))capacityCalls++;return route.fulfill({json:{appointmentScopes:[]}})});
 await page.goto('/patient-app/slots?center=center-live&member=untrusted-member');
 await expect(page.getByText('دسترسی نوبت‌گیری برای این حساب فعال نیست.')).toBeVisible();expect(capacityCalls).toBe(0);
 await expect(page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true})).toHaveCount(0);
});
test('unconfirmed reconciliation remains uncertain and cannot submit again',async({page})=>{
 const server=await mock(page,'lost');
 await page.route('**/api/v1/me/appointments',route=>route.fulfill({json:{appointments:[]}}));
 await page.goto('/patient-app/slots?center=center-live');await page.getByRole('group',{name:'زمان مراجعه'}).getByRole('button').click();await page.getByRole('button',{name:'ادامه پس از انتخاب زمان'}).click();await page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true}).click();
 await page.getByRole('button',{name:'بررسی نتیجه ثبت'}).click();await expect(page.getByRole('alert')).toContainText('هنوز ثبت نوبت تأیید نشده');
 expect(server.posts()).toBe(1);await expect(page.getByRole('button',{name:'تأیید و ثبت نوبت',exact:true})).toHaveCount(0);
});
