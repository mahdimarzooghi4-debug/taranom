import { test, expect } from '@playwright/test';
test('FIGMA-CENTER-API: current grants gate live schedule read and revisioned slot mutation',async({page})=>{
 await page.addInitScript(()=>{Object.defineProperty(window,'taranomSession',{value:{getAccessToken:()=> 'synthetic-ci-token'}})});
 const calls:string[]=[];
 await page.route('**/api/v1/**',async route=>{const req=route.request(),url=new URL(req.url());calls.push(req.method()+' '+url.pathname);expect(req.headers().authorization).toBe('Bearer synthetic-ci-token');
  if(url.pathname==='/api/v1/me/appointment-scopes')return route.fulfill({json:{appointmentScopes:[{capability:'CenterAppointmentsManage',scopeRef:'center-synthetic-1'}]}});
  if(url.pathname.endsWith('/appointment-slots')&&req.method()==='GET')return route.fulfill({json:{centerRef:'center-synthetic-1',revision:4,slots:[]}});
  if(url.pathname.endsWith('/appointments'))return route.fulfill({json:{centerRef:'center-synthetic-1',revision:4,appointments:[{bookingRef:'booking-synthetic-1',memberRef:'member-synthetic-1',slotRef:'slot-synthetic-1',startsAt:'2026-11-20T09:00:00Z',state:'Booked'}]}});
  if(url.pathname.endsWith('/appointment-slots')&&req.method()==='POST'){const body=req.postDataJSON();expect(body.expectedRevision).toBe(4);expect(body.requestId).toBeTruthy();return route.fulfill({status:200,json:{revision:5}})}
  return route.fulfill({status:404});
 });
 await page.goto('/center-portal/schedules');
 await expect(page.getByText('booking-synthetic-1')).toBeVisible();
 await page.getByLabel('مرجع پزشک در سامانه').fill('doctor-synthetic-1');
 await page.getByLabel('زمان شروع').fill('2026-11-21T09:00');
 await page.getByRole('button',{name:'ثبت ظرفیت'}).click();
 await expect.poll(()=>calls.filter(x=>x.endsWith('POST /api/v1/centers/center-synthetic-1/appointment-slots')).length).toBe(1);
 expect(await page.evaluate(()=>Object.keys(localStorage).length+Object.keys(sessionStorage).length)).toBe(0);
});
