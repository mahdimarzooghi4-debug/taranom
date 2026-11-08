import {test,expect,type Page} from '@playwright/test';
const ref='550e8400-e29b-41d4-a716-446655440000';
const label='پیش‌نویس آزمایشی خانواده';
type Draft={draftRef:string;requestId:string;method:string;recipientLabel:string;state:string;createdAt:string;withdrawnAt:string|null};
async function mock(page:Page,mode:'ok'|'lostCreate'|'lostWithdraw'='ok'){
 const drafts:Draft[]=[];let creates=0,withdraws=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-group-token'}});
 await page.route('**/api/v1/me/group-invitation-drafts**',route=>{
  const req=route.request(),u=new URL(req.url());expect(req.headers().authorization).toBe('Bearer synthetic-group-token');
  if(req.method()==='POST'&&u.pathname.endsWith('/withdraw')){withdraws++;drafts[0]={...drafts[0],state:'Withdrawn',withdrawnAt:'2026-11-07T10:00:00Z'};if(mode==='lostWithdraw')return route.abort();return route.fulfill({json:{draft:drafts[0]}})}
  if(req.method()==='POST'){creates++;const c=req.postDataJSON();expect(Object.keys(c).sort()).toEqual(['method','recipientLabel','requestId']);const item={...c,draftRef:ref,state:'Draft',createdAt:'2026-11-07T09:00:00Z',withdrawnAt:null};drafts.push(item);if(mode==='lostCreate')return route.abort();return route.fulfill({json:{outcome:'Applied',draft:item}})}
  const id=u.searchParams.get('requestId');return route.fulfill({json:{drafts:id?drafts.filter(d=>d.requestId===id):drafts,nextBefore:null}});
 });return {creates:()=>creates,withdraws:()=>withdraws};
}
async function create(page:Page){await page.goto('/patient-app/add-member');await page.getByRole('button',{name:/دعوت برای ثبت‌نام/}).click();await page.getByLabel('نام نمایشی پیش‌نویس',{exact:true}).fill(label);await page.getByRole('button',{name:'ذخیرهٔ پیش‌نویس دعوت',exact:true}).click()}
test('create persists an owner draft without claiming an invite was sent or membership accepted',async({page})=>{
 const api=await mock(page);await create(page);await expect(page.locator('.group-draft-receipt')).toContainText('پیش‌نویس ذخیره‌شده');await expect(page.locator('.group-draft-receipt')).toContainText(label);expect(api.creates()).toBe(1);await page.getByRole('link',{name:'مشاهدهٔ پیش‌نویس‌ها',exact:true}).click();await expect(page.locator('.group-draft-card')).toContainText(label);await expect(page.locator('.group-draft-boundary')).toContainText('دعوت ارسال‌شده نیستند');await page.reload();await expect(page.locator('.group-draft-card')).toContainText(label);
});
test('lost create receipt uses exact request lookup without another write',async({page})=>{
 const api=await mock(page,'lostCreate');await create(page);await page.getByRole('button',{name:'بررسی نتیجهٔ ذخیره',exact:true}).click();await expect(page.locator('.group-draft-receipt')).toContainText(label);expect(api.creates()).toBe(1);
});
test('withdrawal asks in-page confirmation and lost response is reconciled',async({page})=>{
 const api=await mock(page,'lostWithdraw');await create(page);await page.getByRole('link',{name:'مشاهدهٔ پیش‌نویس‌ها',exact:true}).click();await page.getByRole('button',{name:'بایگانی پیش‌نویس',exact:true}).click();expect(api.withdraws()).toBe(0);await page.getByRole('button',{name:'تأیید بایگانی',exact:true}).click();await page.getByRole('button',{name:'بررسی نتیجهٔ ذخیره',exact:true}).click();await expect(page.locator('.group-draft-card')).toContainText('بایگانی‌شده');expect(api.withdraws()).toBe(1);
});
test('no session cannot list or create drafts and form stays available',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.goto('/patient-app/group-members');await expect(page.getByRole('alert')).toContainText('نشست ورود');await create(page);await expect(page.getByRole('alert')).toContainText('نشست ورود');await expect(page.getByLabel('نام نمایشی پیش‌نویس',{exact:true})).toHaveValue(label);expect(calls).toBe(0);
});
test('denied refresh removes previously loaded draft labels',async({page})=>{
 await mock(page);await create(page);await page.getByRole('link',{name:'مشاهدهٔ پیش‌نویس‌ها',exact:true}).click();await expect(page.locator('.group-draft-card')).toContainText(label);await page.route('**/api/v1/me/group-invitation-drafts**',r=>r.fulfill({status:403,json:{}}));await page.getByRole('button',{name:'تازه‌سازی پیش‌نویس‌ها',exact:true}).click();await expect(page.getByRole('alert')).toBeVisible();await expect(page.locator('.group-draft-card')).toHaveCount(0);
});
test('invalid draft receipt cannot show a saved or accepted membership',async({page})=>{
 await mock(page);await page.route('**/api/v1/me/group-invitation-drafts',r=>r.request().method()==='POST'?r.fulfill({json:{outcome:'Applied',draft:{state:'Accepted'}}}):r.fulfill({json:{drafts:[],nextBefore:null}}));await create(page);await expect(page.getByRole('alert')).toContainText('قابل تأیید نیست');await expect(page.locator('.group-draft-receipt')).toHaveCount(0);
});
test('390px group method chooser retains Figma slots and local assets',async({page})=>{
 await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/add-member');await page.evaluate(()=>document.fonts.ready);
 for(const [i,y] of [190,336].entries()){const b=await page.locator('.group-method').nth(i).boundingBox();expect(b!.x).toBe(20);expect(b!.y).toBe(y);expect(b!.width).toBe(350);expect(b!.height).toBe(126)}
 for(const [selector,w,h] of [['.appointments-logo',140,63],['.appointments-bell img',22,22],['.appointments-nav img',24,24]] as const)for(const img of await page.locator(selector).all()){expect(await img.evaluate(i=>(i as HTMLImageElement).complete&&(i as HTMLImageElement).naturalWidth>0)).toBe(true);const b=await img.boundingBox();expect(b!.width).toBe(w);expect(b!.height).toBe(h)}
 await expect(page.getByRole('link',{name:'حساب من',exact:true})).toHaveAttribute('aria-current','page');expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);await page.screenshot({path:'/tmp/taranom-group-choice-390.png',fullPage:true});
});
