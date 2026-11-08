import {test,expect,type Page} from '@playwright/test';
const profile={revision:2,fullName:'کاربر آزمایشی ترنم',birthDate:'1990-05-12',city:'شیراز'};
async function session(page:Page){await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-account-token'}})}
test('account reads owner profile and routes to connected personal details and support',async({page})=>{
 await session(page);await page.route('**/api/v1/me/profile',route=>{expect(route.request().method()).toBe('GET');expect(route.request().headers().authorization).toBe('Bearer synthetic-account-token');expect(new URL(route.request().url()).search).toBe('');return route.fulfill({json:{profile}})});
 await page.goto('/patient-app/account?subject=another-user');await expect(page.getByRole('heading',{name:profile.fullName,exact:true})).toBeVisible();await expect(page.locator('.patient-account')).not.toContainText('نیلوفر رضایی');await expect(page.locator('.patient-account')).not.toContainText('۰۹۱۲');
 await expect(page.getByRole('link',{name:'اطلاعات فردی',exact:true})).toHaveAttribute('href','/patient-app/personal-info');await expect(page.getByRole('link',{name:'پشتیبانی',exact:true})).toHaveAttribute('href','/patient-app/support');await expect(page.getByRole('link',{name:'حساب من',exact:true})).toHaveAttribute('aria-current','page');
 await page.getByRole('link',{name:'اطلاعات فردی',exact:true}).click();await expect(page.getByLabel('نام و نام خانوادگی')).toHaveValue(profile.fullName);
});
test('refresh clears stale name on denied session then recovers with new profile',async({page})=>{
 await session(page);let reads=0;await page.route('**/api/v1/me/profile',route=>{reads++;return reads===2?route.fulfill({status:403,json:{}}):route.fulfill({json:{profile:{...profile,fullName:reads===1?profile.fullName:'نام تازهٔ آزمایشی'}}})});
 await page.goto('/patient-app/account');await expect(page.getByRole('heading',{name:profile.fullName,exact:true})).toBeVisible();await page.getByRole('button',{name:'تازه‌سازی حساب'}).click();await expect(page.getByRole('alert')).toContainText('مجاز نیست');await expect(page.getByRole('heading',{name:profile.fullName,exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'تازه‌سازی حساب'}).click();await expect(page.getByRole('heading',{name:'نام تازهٔ آزمایشی',exact:true})).toBeVisible();
});
test('missing session sends no request and shows no sample patient',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/patient-app/account');await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(calls).toBe(0);await expect(page.locator('.account-summary')).not.toContainText(profile.fullName);await expect(page.getByRole('link',{name:'پشتیبانی',exact:true})).toBeVisible();
});
test('unregistered profile does not imply membership or verified identity',async({page})=>{
 await session(page);await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile:{revision:0,fullName:'',city:'',birthDate:null}}}));await page.goto('/patient-app/account');await expect(page.getByRole('heading',{name:'نام ثبت نشده',exact:true})).toBeVisible();await expect(page.getByRole('status')).toContainText('تکمیل نام');await expect(page.locator('.account-membership')).toContainText('در انتظار اتصال');await expect(page.getByRole('button',{name:'خروج از حساب',exact:false})).toBeDisabled();
});
test('invalid profile response cannot be displayed as verified data',async({page})=>{
 await session(page);await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile:{fullName:'نام نامعتبر',revision:-1}}}));await page.goto('/patient-app/account');await expect(page.getByRole('alert')).toContainText('معتبر نیست');await expect(page.getByRole('heading',{name:'نام نامعتبر',exact:true})).toHaveCount(0);
});
test('account navigation from preview loads the connected component',async({page})=>{
 await session(page);await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile}}));await page.goto('/patient-app');await page.getByRole('navigation',{name:'ناوبری اصلی'}).getByRole('link',{name:/حساب من/}).click();await expect(page).toHaveURL(/\/patient-app\/account$/);await expect(page.getByRole('heading',{name:profile.fullName,exact:true})).toBeVisible();
});
test('account matches 390px Figma slots and local asset geometry',async({page})=>{
 await page.setViewportSize({width:390,height:844});await session(page);await page.route('**/api/v1/me/profile',r=>r.fulfill({json:{profile}}));await page.goto('/patient-app/account');await expect(page.getByRole('heading',{name:profile.fullName,exact:true})).toBeVisible();await page.evaluate(()=>document.fonts.ready);
 for(const [selector,w,h] of [['.appointments-logo',140,63],['.appointments-bell img',22,22],['.account-avatar',58,58],['.appointments-nav img',24,24]] as const){for(const image of await page.locator(selector).all()){expect(await image.evaluate(el=>(el as HTMLImageElement).complete&&(el as HTMLImageElement).naturalWidth>0)).toBe(true);const box=await image.boundingBox();expect(box!.width).toBe(w);expect(box!.height).toBe(h)}}
 const summary=await page.locator('.account-summary').boundingBox();expect(summary!.x).toBe(20);expect(summary!.y).toBe(138);expect(summary!.width).toBe(350);expect(summary!.height).toBe(120);expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await page.screenshot({path:'/tmp/taranom-account-390.png',fullPage:true});
});
