import {test,expect,type Page} from '@playwright/test';
const ref='550e8400-e29b-41d4-a716-446655440000';
const label='پیش‌نویس آزمایشی خانواده';
type Draft={draftRef:string;requestId:string;method:string;recipientLabel:string;state:string;createdAt:string;withdrawnAt:string|null;latestInvitation:null};
async function mock(page:Page,mode:'ok'|'lostCreate'|'lostWithdraw'='ok'){
 const drafts:Draft[]=[];let creates=0,withdraws=0;
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-group-token'}});
 await page.route('**/api/v1/me/group-invitation-drafts**',route=>{
  const req=route.request(),u=new URL(req.url());expect(req.headers().authorization).toBe('Bearer synthetic-group-token');
  if(req.method()==='POST'&&u.pathname.endsWith('/withdraw')){withdraws++;drafts[0]={...drafts[0],state:'Withdrawn',withdrawnAt:'2026-11-07T10:00:00Z'};if(mode==='lostWithdraw')return route.abort();return route.fulfill({json:{draft:drafts[0]}})}
  if(req.method()==='POST'){creates++;const c=req.postDataJSON();expect(Object.keys(c).sort()).toEqual(['method','recipientLabel','requestId']);const item={...c,draftRef:ref,state:'Draft',createdAt:'2026-11-07T09:00:00Z',withdrawnAt:null};drafts.push({...item,latestInvitation:null});if(mode==='lostCreate')return route.abort();return route.fulfill({json:{outcome:'Applied',draft:item}})}
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

test('owner draft worklist shows latest effective invitation status without N+1 reads',async({page})=>{
 const invitationRequests:string[]=[];
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-group-token'}});
 const base={draftRef:ref,requestId:'550e8400-e29b-41d4-a716-446655440001',
  method:'ExistingMember',recipientLabel:'نمونه',state:'Draft',createdAt:'2026-11-07T09:00:00Z',
  withdrawnAt:null};
 const summary=(n:number,state:'Pending'|'Accepted'|'Declined'|'Revoked'|'Expired')=>({
  invitationRef:'550e8400-e29b-41d4-a716-'+String(n).padStart(12,'0'),state,
  createdAt:'2026-11-07T09:00:00Z',expiresAt:'2026-11-14T09:00:00Z',
  respondedAt:state==='Accepted'||state==='Declined'?'2026-11-07T10:00:00Z':null
 });
 const rows=[
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000101',recipientLabel:'بدون پیوند',latestInvitation:null},
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000102',recipientLabel:'در انتظار',latestInvitation:summary(102,'Pending')},
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000103',recipientLabel:'پذیرفته‌شده',latestInvitation:summary(103,'Accepted')},
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000104',recipientLabel:'ردشده',latestInvitation:summary(104,'Declined')},
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000105',recipientLabel:'لغوشده',latestInvitation:summary(105,'Revoked')},
  {...base,draftRef:'550e8400-e29b-41d4-a716-000000000106',recipientLabel:'منقضی‌شده',latestInvitation:summary(106,'Expired')}
 ];
 await page.route('**/api/v1/me/group-invitation-drafts**',route=>{
  expect(route.request().method()).toBe('GET');
  expect(route.request().headers().authorization).toBe('Bearer synthetic-group-token');
  return route.fulfill({json:{drafts:rows,nextBefore:null}});
 });
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations',route=>{
  invitationRequests.push(route.request().url());return route.fulfill({json:{invitation:null}});
 });
 await page.setViewportSize({width:390,height:844});
 await page.goto('/patient-app/group-members');
 await expect(page.locator('.group-draft-card')).toHaveCount(6);
 const card=(label:string)=>page.locator('.group-draft-card').filter({hasText:label});
 await expect(card('بدون پیوند')).toContainText('هنوز پیوند دعوتی ساخته نشده است');
 await expect(card('در انتظار')).toContainText('آخرین پیوند: منتظر پاسخ');
 await expect(card('پذیرفته‌شده')).toContainText('آخرین پیوند: دعوت پذیرفته شد');
 await expect(card('پذیرفته‌شده')).toContainText('پذیرش دعوت به‌معنای فعال‌شدن عضویت گروه نیست');
 await expect(card('ردشده')).toContainText('آخرین پیوند: دعوت رد شد');
 await expect(card('لغوشده')).toContainText('آخرین پیوند: پیوند لغو شده');
 await expect(card('منقضی‌شده')).toContainText('آخرین پیوند: پیوند منقضی شده');
 await expect(card('پذیرفته‌شده')).toContainText('زمان پاسخ:');
 await expect(card('در انتظار')).toContainText('پایان اعتبار:');
 expect(invitationRequests).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 await page.setViewportSize({width:320,height:740});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 expect(await page.locator('html').getAttribute('dir')).toBe('rtl');
});
test('forged latest invitation summary is rejected and old private draft cards are cleared',async({page})=>{
 await mock(page);await create(page);await page.getByRole('link',{name:'مشاهدهٔ پیش‌نویس‌ها',exact:true}).click();
 await expect(page.locator('.group-draft-card')).toContainText(label);
 await page.route('**/api/v1/me/group-invitation-drafts**',route=>route.fulfill({json:{
  drafts:[{draftRef:ref,requestId:'550e8400-e29b-41d4-a716-446655440001',
   method:'ExistingMember',recipientLabel:'forged private label',state:'Draft',
   createdAt:'2026-11-07T09:00:00Z',withdrawnAt:null,
   latestInvitation:{invitationRef:'550e8400-e29b-41d4-a716-446655440002',
    state:'Accepted',createdAt:'2026-11-07T09:00:00Z',expiresAt:'2026-11-14T09:00:00Z',
    respondedAt:'2026-11-07T10:00:00Z',recipientSubject:'forged-member'}}
  ],nextBefore:null
 }}));
 await page.getByRole('button',{name:'تازه‌سازی پیش‌نویس‌ها',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('پاسخ پیش‌نویس‌های دعوت معتبر نیست');
 await expect(page.locator('.group-draft-card')).toHaveCount(0);
});


test('owner searches private draft labels on the server before paging and can clear the filter',async({page})=>{
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-group-token'}});
 const requests:string[]=[];
 const rows=Array.from({length:47},(_,i)=>({
  sequence:i+1,draftRef:'550e8400-e29b-41d4-a716-'+String(i+1).padStart(12,'0'),
  requestId:'650e8400-e29b-41d4-a716-'+String(i+1).padStart(12,'0'),
  method:'ExistingMember',recipientLabel:i<22?'گروه اختصاصی '+i:'پیش‌نویس دیگر '+i,
  state:'Draft',createdAt:'2026-11-07T09:00:00Z',withdrawnAt:null,latestInvitation:null
 }));
 await page.route('**/api/v1/me/group-invitation-drafts**',route=>{
  const u=new URL(route.request().url());expect(route.request().method()).toBe('GET');
  expect(route.request().headers().authorization).toBe('Bearer synthetic-group-token');
  requests.push(u.search);
  const q=u.searchParams.get('q'),before=u.searchParams.get('before');
  const filtered=rows.filter(x=>(!before||x.sequence<Number(before))&&(!q||x.recipientLabel.toLowerCase().includes(q.toLowerCase())))
   .sort((a,b)=>b.sequence-a.sequence);
  const drafts=filtered.slice(0,20).map(({sequence,...rest})=>rest);
  return route.fulfill({json:{drafts,nextBefore:filtered.length>20?String(filtered[19].sequence):null}});
 });
 await page.setViewportSize({width:390,height:844});
 await page.goto('/patient-app/group-members');
 await expect(page.locator('.group-draft-card')).toHaveCount(20);
 await expect(page.locator('.group-draft-card').first()).toContainText('پیش‌نویس دیگر');
 const input=page.getByRole('searchbox',{name:'جست‌وجو در نام نمایشی پیش‌نویس'});
 await input.fill('  گروه  ');expect(requests).toHaveLength(1);
 await page.getByRole('button',{name:'جست‌وجو',exact:true}).click();
 await expect(page.locator('.group-draft-card')).toHaveCount(20);
 await expect(page.locator('.group-draft-card').first()).toContainText('گروه اختصاصی');
 await expect(page.getByRole('status',{name:''}).filter({hasText:'فیلتر فعال'})).toContainText('گروه');
 expect(requests.at(-1)).toBe('?q='+encodeURIComponent('گروه'));
 await page.getByRole('button',{name:'پیش‌نویس‌های قدیمی‌تر'}).click();
 await expect(page.locator('.group-draft-card')).toHaveCount(2);
 expect(requests.at(-1)).toContain('q='+encodeURIComponent('گروه'));
 expect(requests.at(-1)).toContain('before=');
 await page.getByRole('button',{name:'تازه‌سازی پیش‌نویس‌ها'}).click();
 await expect(page.locator('.group-draft-card')).toHaveCount(20);
 expect(requests.at(-1)).toBe('?q='+encodeURIComponent('گروه'));
 await input.fill('نامی که وجود ندارد');
 await page.getByRole('button',{name:'جست‌وجو',exact:true}).click();
 await expect(page.getByText('پیش‌نویسی با این نام نمایشی در حساب شما یافت نشد.')).toBeVisible();
 await page.getByRole('button',{name:'پاک‌کردن جست‌وجو'}).click();
 await expect(page.locator('.group-draft-card')).toHaveCount(20);
 expect(requests.at(-1)).toBe('');
 await expect(input).toHaveValue('');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 await page.setViewportSize({width:320,height:740});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 expect(await page.locator('html').getAttribute('dir')).toBe('rtl');
});
test('search with no session never sends private traffic; denial clears previous private cards',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});
 await page.goto('/patient-app/group-members');
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 await page.getByRole('searchbox',{name:'جست‌وجو در نام نمایشی پیش‌نویس'}).fill('گروه');
 await page.getByRole('button',{name:'جست‌وجو',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 expect(calls).toBe(0);
});
