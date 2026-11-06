import {test,expect,type Page} from '@playwright/test';
const ref='550e8400-e29b-41d4-a716-446655440000',message='Synthetic member request for account assistance',answer='پاسخ آزمایشی پشتیبانی برای درخواست حساب';
async function server(page:Page,mode:'ok'|'lost'|'forbidden'|'conflict'='ok'){
 let ticket={requestRef:ref,requestId:ref,topic:'account',message,state:'Registered',createdAt:'2026-11-06T09:00:00Z',revision:0,reply:null as string|null,repliedAt:null as string|null},posts=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-staff-token'}});
 await page.route('**/api/v1/operations/support-requests**',route=>{
  expect(route.request().headers().authorization).toBe('Bearer synthetic-staff-token');
  if(mode==='forbidden')return route.fulfill({status:403,json:{}});
  if(route.request().method()==='POST'){
   posts++;const cmd=route.request().postDataJSON();expect(cmd.expectedRevision).toBe(0);expect(cmd.subject).toBeUndefined();expect(cmd.requestId).toMatch(/^[a-f0-9-]{36}$/);
   if(mode==='conflict')return route.fulfill({status:409,json:{}});
   ticket={...ticket,state:'Answered',revision:1,reply:cmd.reply,repliedAt:'2026-11-06T10:00:00Z'};
   if(mode==='lost')return route.abort('failed');return route.fulfill({json:{outcome:'Applied',request:ticket}});
  }
  return route.fulfill({json:{requests:[ticket],nextBefore:null}});
 });return {posts:()=>posts};
}
async function respond(page:Page){await page.goto('/admin/support-requests');await page.getByRole('button',{name:'مشاهده درخواست',exact:true}).click();await page.getByLabel('پاسخ به درخواست').fill(answer);await page.getByRole('button',{name:'ثبت پاسخ',exact:true}).click()}
test('staff queue reads API and persists answer with readback',async({page})=>{const api=await server(page);await respond(page);await expect(page.getByRole('status')).toContainText('پاسخ ثبت شد');await expect(page.locator('.support-desk-detail')).toContainText(answer);expect(api.posts()).toBe(1);await page.reload();await page.getByRole('button',{name:'مشاهده درخواست',exact:true}).click();await expect(page.locator('.support-desk-detail')).toContainText(answer);await expect(page.getByRole('button',{name:'ثبت پاسخ',exact:true})).toHaveCount(0)});
test('lost reply response is reconciled without another POST',async({page})=>{const api=await server(page,'lost');await respond(page);await page.getByRole('button',{name:'بررسی نتیجه پاسخ'}).click();await expect(page.getByRole('status')).toContainText('پاسخ ثبت شد');expect(api.posts()).toBe(1)});
test('forbidden queue contains no sample requests',async({page})=>{await server(page,'forbidden');await page.goto('/admin/support-requests');await expect(page.getByRole('alert')).toContainText('مجوز');await expect(page.getByRole('button',{name:'مشاهده درخواست',exact:true})).toHaveCount(0)});
test('stale response cannot claim success',async({page})=>{await server(page,'conflict');await respond(page);await expect(page.getByRole('alert')).toContainText('تغییر کرده');await expect(page.getByLabel('پاسخ به درخواست')).toHaveValue(answer);await expect(page.getByText('پاسخ ثبت شد و در پیگیری بیمار قابل مشاهده است.')).toHaveCount(0)});
test('no authenticated session sends no API calls',async({page})=>{let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/admin/support-requests');await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(calls).toBe(0)});
