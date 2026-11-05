import { test, expect } from '@playwright/test';
test('PATIENT-API: own appointments load and patient cancellation persists visibly',async({page})=>{
 await page.addInitScript(()=>{Object.defineProperty(window,'taranomSession',{value:{getAccessToken:()=> 'synthetic-ci-token'}})});
 let cancelled=false;const methods:string[]=[];
 await page.route('**/api/v1/**',async route=>{const req=route.request(),url=new URL(req.url());methods.push(req.method()+' '+url.pathname);expect(req.headers().authorization).toBe('Bearer synthetic-ci-token');
  if(url.pathname==='/api/v1/me/appointments')return route.fulfill({json:{appointments:[{centerRef:'center-synthetic-1',scheduleRevision:3,bookingRef:'booking-synthetic-1',memberRef:'member-synthetic-1',slotRef:'slot-synthetic-1',startsAt:'2026-11-20T09:00:00Z',state:cancelled?'CancelledByMember':'Booked'}]}});
  if(url.pathname.endsWith('/booking-synthetic-1/cancel')){cancelled=true;expect(req.method()).toBe('POST');return route.fulfill({status:200,json:{state:'CancelledByMember'}})}
  return route.fulfill({status:404});
 });
 await page.goto('/patient-app/appointments');
 await expect(page.getByText('booking-synthetic-1')).toBeVisible();
 await page.getByRole('button',{name:'لغو نوبت'}).click();
 await expect(page.getByText('لغوشده توسط شما')).toBeVisible();
 await expect(page.getByRole('button',{name:'لغو نوبت'})).toHaveCount(0);
 expect(methods.some(x=>x==='GET /api/v1/me/appointments')).toBeTruthy();
 expect(methods.some(x=>x.startsWith('POST /api/v1/member/members/'))).toBeTruthy();
 expect(await page.evaluate(()=>Object.keys(localStorage).length+Object.keys(sessionStorage).length)).toBe(0);
}