import {test,expect,type Page} from '@playwright/test';
const tracking='550e8400-e29b-41d4-a716-446655440000';
const body='برای استفاده از حساب به راهنمایی نیاز دارم.';
async function mock(page:Page,mode:'ok'|'lost'|'invalid'='ok'){
 const requests:{requestRef:string;requestId:string;topic:string;message:string;state:string;createdAt:string}[]=[];let posts=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-support-token'}});
 await page.route('**/api/v1/me/support-requests*',route=>{
  const req=route.request(),url=new URL(req.url());expect(req.headers().authorization).toBe('Bearer synthetic-support-token');
  if(req.method()==='POST'){
   posts++;const command=req.postDataJSON();expect(command.subject).toBeUndefined();expect(command.requestId).toMatch(/^[a-f0-9-]{36}$/);
   if(mode==='invalid')return route.fulfill({json:{outcome:'Applied'}});
   const item={...command,requestRef:tracking,state:'Registered',createdAt:'2026-11-06T09:00:00Z'};requests.push(item);
   if(mode==='lost')return route.abort('failed');return route.fulfill({json:{outcome:'Applied',request:item}});
  }
  const id=url.searchParams.get('requestId');return route.fulfill({json:{requests:id?requests.filter(r=>r.requestId===id):requests,nextBefore:null}});
 });return {posts:()=>posts};
}
async function fill(page:Page){await page.getByLabel('موضوع درخواست').selectOption('account');await page.getByLabel('شرح درخواست',{exact:true}).fill(body)}
test('support request persists visibly with receipt and own history',async({page})=>{
 const server=await mock(page);await page.goto('/patient-app/support');await fill(page);await page.getByRole('button',{name:'ثبت درخواست پشتیبانی',exact:true}).click();
 await expect(page.getByRole('status')).toContainText(tracking);expect(server.posts()).toBe(1);
 await page.getByRole('button',{name:'درخواست‌های من',exact:true}).click();await expect(page.locator('.support-history')).toContainText(body);
 await page.reload();await page.getByRole('button',{name:'درخواست‌های من',exact:true}).click();await expect(page.locator('.support-history')).toContainText(tracking);
});
test('lost response reconciles by request ID without repeating POST',async({page})=>{
 const server=await mock(page,'lost');await page.goto('/patient-app/support');await fill(page);await page.getByRole('button',{name:'ثبت درخواست پشتیبانی',exact:true}).click();
 await page.getByRole('button',{name:'بررسی نتیجه ثبت'}).click();await expect(page.getByRole('status')).toContainText(tracking);expect(server.posts()).toBe(1);
});
test('invalid receipt cannot claim a registered ticket',async({page})=>{
 await mock(page,'invalid');await page.goto('/patient-app/support');await fill(page);await page.getByRole('button',{name:'ثبت درخواست پشتیبانی',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('قابل تأیید نیست');await expect(page.getByRole('heading',{name:'درخواست پشتیبانی ثبت شد'})).toHaveCount(0);
});
test('unauthenticated create sends no request and preserves form',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/patient-app/support');await fill(page);await page.getByRole('button',{name:'ثبت درخواست پشتیبانی',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(calls).toBe(0);await expect(page.getByLabel('شرح درخواست',{exact:true})).toHaveValue(body);
 await expect(page.getByRole('button',{name:'ثبت درخواست پشتیبانی',exact:true})).toBeEnabled();
});
test('support history uses server cursor and clears stale data on denied refresh',async({page})=>{
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-support-token'}});let denied=false;
 await page.route('**/api/v1/me/support-requests*',route=>{
  if(denied)return route.fulfill({status:403});const before=new URL(route.request().url()).searchParams.get('before');
  const item={requestRef:before?'older-request':'latest-request',requestId:tracking,topic:'privacy',message:body,state:'Registered',createdAt:'2026-11-06T09:00:00Z'};
  return route.fulfill({json:{requests:[item],nextBefore:before?null:'21'}});
 });await page.goto('/patient-app/support');await page.getByRole('button',{name:'درخواست‌های من',exact:true}).click();await expect(page.locator('.support-history')).toContainText('latest-request');
 await page.getByRole('button',{name:'درخواست‌های قدیمی‌تر'}).click();await expect(page.locator('.support-history')).toContainText('older-request');
 denied=true;await page.getByRole('button',{name:'تازه‌سازی درخواست‌ها'}).click();await expect(page.getByRole('alert')).toContainText('دسترسی');await expect(page.getByText('older-request',{exact:true})).toHaveCount(0);
});
test('support Figma layout and assets at 390px',async({page})=>{
 await mock(page);await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/support');await page.evaluate(()=>document.fonts.ready);
 await expect.poll(()=>page.locator('.appointments-surface img').evaluateAll(imgs=>imgs.every(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0))).toBeTruthy();
 await expect(page.getByRole('navigation').getByRole('link',{name:'حساب من'})).toHaveAttribute('aria-current','page');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await page.screenshot({path:'/tmp/taranom-support-390.png',fullPage:true});
});
