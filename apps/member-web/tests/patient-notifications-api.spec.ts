import {test,expect,type Page} from '@playwright/test';
const ref='550e8400-e29b-41d4-a716-446655440000',source='550e8400-e29b-41d4-a716-446655440001';
const item={notificationRef:ref,sourceRequestId:source,kind:'SupportAnswered',createdAt:'2026-11-06T10:00:00Z',readAt:null as string|null};
async function mock(page:Page,mode:'ok'|'lost'='ok'){
 let current={...item},posts=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-notification-token'}});
 await page.route('**/api/v1/me/notifications**',route=>{
  expect(route.request().headers().authorization).toBe('Bearer synthetic-notification-token');
  if(route.request().method()==='POST'){posts++;expect(new URL(route.request().url()).pathname).toBe('/api/v1/me/notifications/'+ref+'/read');current={...current,readAt:'2026-11-06T11:00:00Z'};if(mode==='lost')return route.abort('failed');return route.fulfill({json:{notificationRef:ref,readAt:current.readAt}})}
  return route.fulfill({json:{notifications:[current],unreadCount:current.readAt?0:1,nextBefore:null}});
 });return {posts:()=>posts};
}
test('notification read is persisted and survives reload',async({page})=>{
 const server=await mock(page);await page.goto('/patient-app/notifications');await expect(page.locator('.notifications-count')).toContainText('۱');await expect(page.getByAltText('خوانده‌نشده',{exact:true})).toBeVisible();await page.getByRole('button',{name:'خواندم',exact:true}).click();await expect(page.getByText('خوانده‌شده',{exact:true})).toBeVisible();await expect(page.locator('.notifications-count')).toContainText('۰');expect(server.posts()).toBe(1);await page.reload();await expect(page.getByText('خوانده‌شده',{exact:true})).toBeVisible();await expect(page.getByAltText('خوانده‌نشده',{exact:true})).toHaveCount(0);
});
test('lost acknowledgement reconciles exact notification without another POST',async({page})=>{
 const server=await mock(page,'lost');await page.goto('/patient-app/notifications');await page.getByRole('button',{name:'خواندم',exact:true}).click();await page.getByRole('button',{name:'بررسی نتیجهٔ خواندن',exact:true}).click();await expect(page.getByText('خوانده‌شده',{exact:true})).toBeVisible();expect(server.posts()).toBe(1);
});
test('support link opens only the exact persisted request history',async({page})=>{
 await mock(page);await page.route('**/api/v1/me/support-requests*',route=>{expect(new URL(route.request().url()).searchParams.get('requestId')).toBe(source);return route.fulfill({json:{requests:[{requestRef:ref,requestId:source,topic:'account',message:'درخواست آزمایشی پشتیبانی حساب',state:'Answered',createdAt:'2026-11-06T09:00:00Z',revision:1,reply:'پاسخ آزمایشی مرتبط با همین اعلان',repliedAt:'2026-11-06T10:00:00Z'}],nextBefore:null}})});await page.goto('/patient-app/notifications');await page.getByRole('link',{name:'مشاهدهٔ پاسخ',exact:true}).click();await expect(page.locator('.support-history')).toContainText('پاسخ آزمایشی مرتبط با همین اعلان');
});
test('denied refresh clears old notifications and no session sends no requests',async({page})=>{
 await mock(page);await page.goto('/patient-app/notifications');await expect(page.locator('.notification-item')).toHaveCount(1);await page.route('**/api/v1/me/notifications**',r=>r.fulfill({status:403,json:{}}));await page.getByRole('button',{name:'تازه‌سازی اعلان‌ها',exact:true}).click();await expect(page.getByRole('alert')).toContainText('مجاز نیست');await expect(page.locator('.notification-item')).toHaveCount(0);
});
test('missing session and malformed responses do not invent notifications',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/patient-app/notifications');await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(calls).toBe(0);await page.evaluate(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-notification-token'}});await page.route('**/api/v1/me/notifications**',r=>r.fulfill({json:{notifications:[{...item,kind:'Unsupported'}],unreadCount:1,nextBefore:null}}));await page.getByRole('button',{name:'تازه‌سازی اعلان‌ها',exact:true}).click();await expect(page.getByRole('alert')).toContainText('معتبر نیست');await expect(page.locator('.notification-item')).toHaveCount(0);
});
test('server cursor and global unread count are retained across pages',async({page})=>{
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-notification-token'}});await page.route('**/api/v1/me/notifications**',route=>{const before=new URL(route.request().url()).searchParams.get('before');return route.fulfill({json:{notifications:before?[{...item,notificationRef:source}]:[item],unreadCount:23,nextBefore:before?null:'123'}})});await page.goto('/patient-app/notifications');await expect(page.locator('.notifications-count')).toContainText('۲۳');await page.getByRole('button',{name:'اعلان‌های قدیمی‌تر',exact:true}).click();await expect(page.getByRole('button',{name:'اعلان‌های قدیمی‌تر',exact:true})).toHaveCount(0);await expect(page.locator('.notifications-count')).toContainText('۲۳');
});
test('empty inbox has no fabricated medical or membership events',async({page})=>{
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-notification-token'}});await page.route('**/api/v1/me/notifications**',r=>r.fulfill({json:{notifications:[],unreadCount:0,nextBefore:null}}));await page.goto('/patient-app/notifications');await expect(page.getByRole('status')).toContainText('اعلانی برای نمایش وجود ندارد');await expect(page.locator('.notification-item')).toHaveCount(0);
});
test('390px notification screen uses Figma assets and right navigation color',async({page})=>{
 await page.setViewportSize({width:390,height:844});await mock(page);await page.goto('/patient-app/notifications');await expect(page.locator('.notification-item')).toHaveCount(1);await page.evaluate(()=>document.fonts.ready);
 for(const [selector,w,h] of [['.appointments-logo',140,63],['.notification-item img',8,8],['.appointments-nav img',24,24]] as const)for(const icon of await page.locator(selector).all()){expect(await icon.evaluate(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0)).toBe(true);const b=await icon.boundingBox();expect(b!.width).toBe(w);expect(b!.height).toBe(h)}
 await expect(page.getByRole('link',{name:'خانه',exact:true})).toHaveAttribute('aria-current','page');await expect(page.locator('.notifications-back')).toHaveAttribute('href','/patient-app/account');const card=await page.locator('.notification-item').boundingBox();expect(card!.width).toBe(350);expect(card!.x).toBe(20);expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await page.screenshot({path:'/tmp/taranom-notifications-390.png',fullPage:true});
});
