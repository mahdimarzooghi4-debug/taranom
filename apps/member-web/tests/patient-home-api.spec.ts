import {test,expect,type Page} from '@playwright/test';
const profile={revision:1,fullName:'کاربر آزمایشی',birthDate:null,city:'تهران'};
async function mock(page:Page,unread=2){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-home-token'}});
 await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile}}));
 await page.route('**/api/v1/me/notifications*',r=>r.fulfill({json:{notifications:[],unreadCount:unread,nextBefore:null}}));
 const make=(bookingRef:string,state:string,startsAt:string)=>({bookingRef,state,startsAt,memberRef:'member-test',centerRef:'center-test',slotRef:'slot-'+bookingRef,scheduleRevision:1});
 await page.route('**/api/v1/me/appointments',r=>r.fulfill({json:{appointments:[make('future','Booked','2099-11-06T10:00:00Z'),make('cancelled','CancelledByMember','2099-11-06T10:00:00Z'),make('past','Booked','2000-11-06T10:00:00Z')]}}));
}
test('home reads owner data without sample credit and uses connected routes',async({page})=>{
 await mock(page);const requests:string[]=[];page.on('request',r=>{if(new URL(r.url()).pathname.startsWith('/api/')){expect(r.headers().authorization).toBe('Bearer synthetic-home-token');requests.push(new URL(r.url()).pathname)}});await page.goto('/patient-app?subject=another-user');await expect(page.getByRole('heading',{name:'سلام، کاربر',exact:true})).toBeVisible();await expect(page.locator('.home-tile').filter({hasText:'نوبت‌های من'})).toContainText('۱ نوبت پیش‌رو');await expect(page.getByAltText('اعلان خوانده‌نشده',{exact:true})).toBeVisible();await expect(page.locator('.patient-home')).not.toContainText('۲٬۵۰۰٬۰۰۰');await expect(page.locator('.patient-home')).not.toContainText('نیلوفر');
 expect(new Set(requests)).toEqual(new Set(['/api/v1/me/profile','/api/v1/me/appointments','/api/v1/me/notifications']));await expect(page.getByRole('link',{name:'گرفتن نوبت',exact:true})).toHaveAttribute('href','/patient-app/booking');await expect(page.getByRole('button',{name:'نسخه‌های من',exact:true})).toBeDisabled();await expect(page.getByRole('button',{name:'پروندهٔ سلامت',exact:true})).toBeDisabled();
});
test('unknown session cannot show sample identity or send API traffic',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/patient-app/');await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');expect(calls).toBe(0);await expect(page.getByRole('heading',{name:'سلام',exact:true})).toBeVisible();await expect(page.getByAltText('اعلان خوانده‌نشده',{exact:true})).toHaveCount(0);await expect(page.locator('.home-upcoming')).toHaveCount(0);
});
test('zero counts are distinct from missing data and do not display an unread dot',async({page})=>{
 await mock(page,0);await page.route('**/api/v1/me/appointments',r=>r.fulfill({json:{appointments:[]}}));await page.goto('/patient-app');await expect(page.locator('.home-tile').filter({hasText:'نوبت‌های من'})).toContainText('۰ نوبت پیش‌رو');await expect(page.getByAltText('اعلان خوانده‌نشده',{exact:true})).toHaveCount(0);
});
test('refresh clears old data and a rejected session invalidates all overview results',async({page})=>{
 await mock(page);await page.goto('/patient-app');await expect(page.getByRole('heading',{name:'سلام، کاربر',exact:true})).toBeVisible();await page.getByRole('button',{name:'همهٔ خدمات',exact:true}).click();await page.route('**/api/v1/me/profile',r=>r.fulfill({status:401,json:{}}));await page.getByRole('button',{name:'تازه‌سازی خانه',exact:true}).click();await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');await expect(page.getByRole('heading',{name:'سلام، کاربر',exact:true})).toHaveCount(0);await expect(page.getByAltText('اعلان خوانده‌نشده',{exact:true})).toHaveCount(0);await expect(page.locator('.home-upcoming')).toHaveCount(0);
});
test('partial service failure is unknown rather than a false zero and retry recovers',async({page})=>{
 await mock(page);await page.route('**/api/v1/me/appointments',r=>r.fulfill({status:403,json:{}}));await page.goto('/patient-app');await expect(page.getByRole('alert')).toContainText('دریافت نوبت‌های پیش‌رو ناموفق بود');await expect(page.getByRole('heading',{name:'سلام، کاربر',exact:true})).toBeVisible();await expect(page.locator('.home-upcoming')).toHaveCount(0);await expect(page.getByAltText('اعلان خوانده‌نشده',{exact:true})).toBeVisible();await page.route('**/api/v1/me/appointments',r=>r.fulfill({json:{appointments:[]}}));await page.getByRole('button',{name:'تلاش دوباره',exact:true}).click();await expect(page.locator('.home-tile').filter({hasText:'نوبت‌های من'})).toContainText('۰ نوبت پیش‌رو');await expect(page.getByRole('alert')).toHaveCount(0);
});
test('all services exposes real account, support and notification links',async({page})=>{
 await mock(page);await page.goto('/patient-app');await page.getByRole('button',{name:'همهٔ خدمات',exact:true}).click();await expect(page.locator('#home-service-links').getByRole('link',{name:'اطلاعات فردی',exact:true})).toHaveAttribute('href','/patient-app/personal-info');await expect(page.locator('#home-service-links').getByRole('link',{name:'پشتیبانی',exact:true})).toHaveAttribute('href','/patient-app/support');await page.locator('#home-service-links').getByRole('link',{name:'اعلان‌ها',exact:true}).click();await expect(page).toHaveURL(/\/patient-app\/notifications$/);
});
test('390px home matches Figma geometry and all local icon sizes',async({page})=>{
 await page.setViewportSize({width:390,height:844});await mock(page);await page.goto('/patient-app');await expect(page.getByRole('heading',{name:'سلام، کاربر',exact:true})).toBeVisible();await page.evaluate(()=>document.fonts.ready);
 for(const [selector,w,h] of [['.appointments-logo',140,63],['.appointments-bell>img:not(.appointments-unread)',22,22],['.appointments-unread',8,8],['.home-search img',22,22],['.home-cross img',18,18],['.home-icon img',20,20],['.appointments-nav img',24,24]] as const)for(const icon of await page.locator(selector).all()){expect(await icon.evaluate(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0)).toBe(true);const box=await icon.boundingBox();expect(box!.width).toBe(w);expect(box!.height).toBe(h)}
 for(const [selector,x,y,w,h] of [['.home-search',20,136,350,44],['.home-guide',20,194,350,142],['.home-services',20,382,350,260],['.home-finance',20,654,350,90]] as const){const b=await page.locator(selector).boundingBox();expect(b!.x).toBe(x);expect(b!.y).toBe(y);expect(b!.width).toBe(w);expect(b!.height).toBe(h)}
 await expect(page.getByRole('link',{name:'خانه',exact:true})).toHaveAttribute('aria-current','page');expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await page.screenshot({path:'/tmp/taranom-home-390.png',fullPage:true});
});
test('320px home stays usable with a long single-token name',async({page})=>{
 await page.setViewportSize({width:320,height:740});await mock(page,0);await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile:{...profile,fullName:'نام'.repeat(40)}}}));await page.goto('/patient-app');await expect(page.getByRole('heading',{name:'سلام، '+'نام'.repeat(40),exact:true})).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await expect(page.getByRole('link',{name:'گرفتن نوبت',exact:true})).toBeVisible();
});
