import {test,expect} from '@playwright/test';
test('catalog exposes only connected destinations and never queries private data',async({page})=>{
 let calls=0;page.on('request',r=>{if(new URL(r.url()).pathname.startsWith('/api/'))calls++});await page.goto('/patient-app/services');await expect(page.getByRole('heading',{name:'همهٔ خدمات',exact:true})).toBeVisible();expect(calls).toBe(0);
 await expect(page.locator('.all-service-row[href]')).toHaveCount(1);await expect(page.getByRole('link',{name:'نوبت و پزشک یا مرکز',exact:true})).toHaveAttribute('href','/patient-app/booking');
 for(const label of ['عضویت و گروه اعضا','اعتبار حمایتی و سوابق مالی','راهنمای هوشمند','پرونده و نسخه‌ها','ارجاع‌های درمانی'])await expect(page.getByRole('button',{name:label,exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'خدمات حساب و پیگیری',exact:true}).click();await expect(page.locator('#all-services-account a')).toHaveCount(5);await expect(page.locator('#all-services-account').getByRole('link',{name:'پشتیبانی',exact:true})).toHaveAttribute('href','/patient-app/support');await expect(page.getByRole('button',{name:'خدمات حساب و پیگیری',exact:true})).toHaveAttribute('aria-expanded','true');
});
test('booking destination keeps its real session gate',async({page})=>{
 let calls=0;page.on('request',r=>{if(new URL(r.url()).pathname.startsWith('/api/'))calls++});await page.goto('/patient-app/services');await page.getByRole('link',{name:'نوبت و پزشک یا مرکز',exact:true}).click();await expect(page).toHaveURL(/\/patient-app\/booking$/);await expect(page.getByRole('alert')).toContainText('نشست ورود معتبر');expect(calls).toBe(0);
});
test('account and notification destinations use connected components',async({page})=>{
 await page.goto('/patient-app/services');await page.getByRole('button',{name:'خدمات حساب و پیگیری',exact:true}).click();await page.locator('#all-services-account').getByRole('link',{name:'اعلان‌ها',exact:true}).click();await expect(page).toHaveURL(/\/patient-app\/notifications$/);await expect(page.getByRole('alert')).toContainText('نشست ورود');await page.goBack();await expect(page.getByRole('heading',{name:'همهٔ خدمات',exact:true})).toBeVisible();await page.getByRole('navigation',{name:'ناوبری اصلی'}).getByRole('link',{name:'حساب من',exact:true}).click();await expect(page.getByRole('alert')).toContainText('نشست ورود');
});
test('legacy preview and connected home both reach the catalog without retaining preview data',async({page})=>{
 await page.goto('/patient-app/records');await page.getByRole('navigation',{name:'ناوبری اصلی'}).getByRole('link',{name:/خانه/}).click();await expect(page.getByRole('heading',{name:'سلام',exact:true})).toBeVisible();await page.getByRole('button',{name:'همهٔ خدمات',exact:true}).click();await page.getByRole('link',{name:'فهرست همهٔ خدمات',exact:true}).click();await expect(page).toHaveURL(/\/patient-app\/services$/);await expect(page.locator('.patient-all-services')).not.toContainText('نیلوفر');await expect(page.locator('.patient-all-services')).not.toContainText('پیش‌نمایش فیگما');
});
test('390px catalog follows Figma card and navigation geometry',async({page})=>{
 await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/services');await page.evaluate(()=>document.fonts.ready);const cards=page.locator('.all-service-row');await expect(cards).toHaveCount(6);
 for(const [i,y] of [170,238,328,396,464,532].entries()){const b=await cards.nth(i).boundingBox();expect(b!.x).toBe(20);expect(b!.y).toBe(y);expect(b!.width).toBe(350);expect(b!.height).toBe(60)}
 for(const [selector,w,h] of [['.appointments-logo',140,63],['.appointments-bell img',22,22],['.appointments-nav img',24,24]] as const)for(const image of await page.locator(selector).all()){expect(await image.evaluate(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0)).toBe(true);const b=await image.boundingBox();expect(b!.width).toBe(w);expect(b!.height).toBe(h)}
 await expect(page.getByRole('link',{name:'خانه',exact:true})).toHaveAttribute('aria-current','page');expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await page.screenshot({path:'/tmp/taranom-all-services-390.png',fullPage:true});
});
test('320px expanded catalog stays inside the viewport',async({page})=>{
 await page.setViewportSize({width:320,height:740});await page.goto('/patient-app/services');await page.getByRole('button',{name:'خدمات حساب و پیگیری',exact:true}).click();await expect(page.locator('#all-services-account').getByRole('link',{name:'اطلاعات فردی',exact:true})).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
});
