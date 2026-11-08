import {test,expect,type Page} from '@playwright/test';
type Center={centerRef:string;displayName:string;city:string;recordState:'ADMIN_DEFINED_UNVERIFIED';
 licensed:false;published:false;managerAssigned:false};
async function auth(page:Page){await page.addInitScript(()=>{
 window.taranomSession={getAccessToken:()=> 'taranom-admin-scoped-test-token'};
})}
const c=(centerRef:string,displayName:string,city:string):Center=>({
 centerRef,displayName,city,recordState:'ADMIN_DEFINED_UNVERIFIED',
 licensed:false,published:false,managerAssigned:false
});
test('Figma admin center screen creates only a server-issued unverified reference',async({page})=>{
 await auth(page);let created:Center|null=null,posts=0;
 await page.route('**/api/v1/operations/centers**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname;
  expect(req.headers().authorization).toBe('Bearer taranom-admin-scoped-test-token');
  if(req.method()==='GET'&&path.endsWith('/centers'))return route.fulfill({json:{centers:created?[created]:[],nextAfter:null}});
  if(req.method()==='POST'){posts++;const body=req.postDataJSON();
   expect(body).toEqual({requestId:expect.stringMatching(/^[0-9a-f-]{36}$/),
    displayName:'مرکز تست شمال',city:'رشت'});
   expect(body.centerRef).toBeUndefined();expect(body.licensed).toBeUndefined();
   created=c('center-'+ 'a'.repeat(32),body.displayName,body.city);
   return route.fulfill({json:{outcome:'Applied',...created}});
  }
  return route.fulfill({status:404});
 });
 await page.goto('/admin/centers');
 await expect(page.getByText('مرکزی در این صفحه یافت نشد.')).toBeVisible();
 await page.getByLabel('نام اعلامی مرکز').fill('مرکز تست شمال');
 await page.getByLabel('شهر اعلامی مرکز').fill('رشت');
 await page.getByRole('button',{name:'ایجاد مرکز با شناسه مرجع'}).click();
 await expect(page.getByRole('cell', {name:'center-'+ 'a'.repeat(32), exact:true})).toBeVisible();
 await expect(page.getByRole('cell',{name:'تعریف‌شده؛ تأییدنشده'})).toBeVisible();
 await expect(page.getByRole('cell',{name:'تخصیص داده نشده'})).toBeVisible();
 expect(posts).toBe(1);
 expect(await page.evaluate(()=>Object.keys(localStorage).length+Object.keys(sessionStorage).length)).toBe(0);
});
test('lost creation ACK reconciles with original UUID without another POST',async({page})=>{
 await auth(page);let id='',posts=0,lookups=0;const ref='center-'+ 'b'.repeat(32);
 await page.route('**/api/v1/operations/centers**',route=>{
  const req=route.request(),url=new URL(req.url());
  if(req.method()==='POST'){posts++;id=req.postDataJSON().requestId;return route.abort('failed')}
  if(url.pathname.includes('/creation-requests/')){lookups++;expect(url.pathname).toContain(id);
   return route.fulfill({json:{outcome:'Observed',centerRef:ref,recordState:'ADMIN_DEFINED_UNVERIFIED'}})}
  return route.fulfill({json:{centers:[],nextAfter:null}});
 });
 await page.goto('/admin/centers');
 await page.getByLabel('نام اعلامی مرکز').fill('مرکز پاسخ نامطمئن');
 await page.getByLabel('شهر اعلامی مرکز').fill('قم');
 await page.getByRole('button',{name:'ایجاد مرکز با شناسه مرجع'}).click();
 await expect(page.getByText('نتیجه ثبت نامطمئن است؛ هیچ درخواست دومی ارسال نمی‌شود.')).toBeVisible();
 await expect(page.getByRole('button',{name:'ایجاد مرکز با شناسه مرجع'})).toHaveCount(0);
 await page.getByRole('button',{name:'بررسی نتیجه ثبت'}).click();
 await expect(page.getByRole('status').filter({hasText:ref})).toBeVisible();
 expect(posts).toBe(1);expect(lookups).toBe(1);
});
test('denied admin does not reveal sample center records or enable writes',async({page})=>{
 await auth(page);let posts=0;
 await page.route('**/api/v1/operations/centers**',route=>{
  if(route.request().method()==='POST')posts++;
  return route.fulfill({status:403,json:{code:'FORBIDDEN'}});
 });
 await page.goto('/admin/centers');
 await expect(page.getByRole('alert')).toContainText('مجوز تعریف مراکز');
 await expect(page.getByText('بیمارستان مهرآفرین')).toHaveCount(0);
 await expect(page.getByRole('button',{name:'ایجاد مرکز با شناسه مرجع'})).toBeDisabled();
 expect(posts).toBe(0);
});
test('without session no privileged API request is sent',async({page})=>{
 let count=0;page.on('request',r=>{if(r.url().includes('/api/v1/'))count++});
 await page.goto('/admin/centers');
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 expect(count).toBe(0);
});
test('registered centers remain private on mobile and no false publication status appears',async({page})=>{
 await page.setViewportSize({width:390,height:844});await auth(page);
 await page.route('**/api/v1/operations/centers**',route=>route.fulfill({json:{
  centers:[c('center-'+ 'c'.repeat(32),'مرکز نام آزمایشی','شیراز')],nextAfter:null
 }}));
 await page.goto('/admin/centers');
 await expect(page.getByRole('cell',{name:'مرکز نام آزمایشی'})).toBeVisible();
 await expect(page.getByText('تعریف مرکز فقط با مجوز ادمین')).toBeVisible();
 await expect(page.getByText('تأیید صلاحیت انجام شد')).toHaveCount(0);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
