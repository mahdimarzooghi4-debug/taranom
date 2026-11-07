import {test,expect,type Page} from '@playwright/test';
const draftRef='550e8400-e29b-41d4-a716-446655440000',inviteRef='550e8400-e29b-41d4-a716-446655440001';
const secret='a'.repeat(64),createdAt='2026-11-07T09:00:00Z',expiresAt='2026-11-14T09:00:00Z';
const base={invitationRef:inviteRef,state:'Pending',createdAt,expiresAt,respondedAt:null};
async function session(page:Page){await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-invitation-session'}})}
async function owner(page:Page,lost=false){let count=0,requestId='',savedToken='';await session(page);await page.route('**/api/v1/me/group-invitation-drafts**',r=>{
 const req=r.request(),u=new URL(req.url());
 if(u.pathname.endsWith('/invitations')){
  if(req.method()==='POST'){const c=req.postDataJSON();expect(Object.keys(c).sort()).toEqual(['requestId','token']);expect(c.token).toMatch(/^[a-f0-9]{64}$/);count++;requestId=c.requestId;savedToken=c.token;if(lost)return r.abort();return r.fulfill({json:{outcome:'Applied',invitation:{...base,requestId}}})}
  return r.fulfill({json:{invitation:requestId?{...base,requestId}:null}});
 }
 return r.fulfill({json:{drafts:[{draftRef,requestId:draftRef,method:'NewRegistration',recipientLabel:'پیش‌نویس آزمایشی',state:'Draft',createdAt,withdrawnAt:null}],nextBefore:null}});
 });return {count:()=>count,token:()=>savedToken};}
async function receiver(page:Page,mode:'ok'|'lost'|'decline'|'revoked'|'malformed'='ok'){
 await session(page);let writes=0,state=mode==='revoked'?'Revoked':'Pending';
 await page.route('**/api/v1/me/group-invitations/*',r=>{
  const req=r.request(),body=req.postDataJSON();expect(req.headers().authorization).toBe('Bearer synthetic-invitation-session');expect(body.token).toBe(secret);expect(new URL(req.url()).search).toBe('');
  if(req.url().endsWith('/respond')){writes++;expect(body.consentVersion).toBe('group-invitation-v1');state=body.decision;if(mode==='lost')return r.abort()}
  return r.fulfill({json:{invitation:mode==='malformed'?{...base,state:'ActiveMember'}:{...base,state,respondedAt:['Accepted','Declined'].includes(state)?'2026-11-07T10:00:00Z':null},consentVersion:'group-invitation-v1'}});
 });return ()=>writes;
}
test('owner explicitly creates fragment-only secret and can reconcile lost receipt without a second write',async({page})=>{
 const api=await owner(page,true);await page.goto('/patient-app/group-invitation?draft='+draftRef);await page.getByRole('button',{name:'ساخت پیوند دعوت',exact:true}).click();await expect(page.getByRole('button',{name:'بررسی نتیجهٔ دعوت'})).toBeVisible();await page.getByRole('button',{name:'بررسی نتیجهٔ دعوت'}).click();await expect(page.locator('.group-invitation-link textarea')).toHaveValue(new RegExp('/patient-app/join-group#'+api.token()+'$'));expect(api.count()).toBe(1);await expect(page.locator('.group-draft-boundary')).toContainText('دسترسی به پروندهٔ سلامت یا کیف پول ایجاد نمی‌شود');
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
test('recipient preview never accepts automatically and acceptance requires explicit consent',async({page})=>{
 const writes=await receiver(page);await page.goto('/patient-app/join-group#'+secret);await expect(page).toHaveURL(/\/patient-app\/join-group$/);expect(writes()).toBe(0);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await expect(page.getByRole('button',{name:'پذیرش دعوت',exact:true})).toBeDisabled();expect(writes()).toBe(0);await page.getByRole('checkbox').check();await page.getByRole('button',{name:'پذیرش دعوت',exact:true}).click();await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('دعوت پذیرفته شد');await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('عضویت ترنم هنوز');expect(writes()).toBe(1);
});
test('lost recipient response reconciles same authenticated recipient without duplicate acceptance',async({page})=>{
 const writes=await receiver(page,'lost');await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await page.getByRole('checkbox').check();await page.getByRole('button',{name:'پذیرش دعوت',exact:true}).click();await page.getByRole('button',{name:'بررسی نتیجهٔ دعوت'}).click();await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('دعوت پذیرفته شد');expect(writes()).toBe(1);
});
test('recipient can decline without consenting and revoked link offers no response controls',async({page})=>{
 const writes=await receiver(page,'decline');await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await page.getByRole('button',{name:'رد دعوت',exact:true}).click();await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('دعوت رد شد');expect(writes()).toBe(1);
 await page.goto('/patient-app/account');await receiver(page,'revoked');await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('پیوند لغو شده');await expect(page.getByRole('button',{name:'پذیرش دعوت',exact:true})).toHaveCount(0);
});
test('malformed invitation cannot show active membership or acceptance buttons',async({page})=>{
 await receiver(page,'malformed');await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await expect(page.getByRole('alert')).toContainText('قابل تأیید نیست');await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toHaveCount(0);
});
test('without a host session no bearer secret is transmitted and source shell stays local at mobile width',async({page})=>{
 let calls=0;page.on('request',r=>{if(r.url().includes('/api/'))calls++});await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await expect(page.getByRole('alert')).toContainText('نشست ورود');expect(calls).toBe(0);for(const [selector,w,h] of [['.appointments-logo',140,63],['.appointments-bell img',22,22],['.appointments-nav img',24,24]] as const)for(const i of await page.locator(selector).all()){expect(await i.evaluate(n=>(n as HTMLImageElement).complete&&(n as HTMLImageElement).naturalWidth>0)).toBe(true);const b=await i.boundingBox();expect(b!.width).toBe(w);expect(b!.height).toBe(h)}expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
});
test('invitation extension renders RTL and fits narrow screens',async({page})=>{
 await owner(page);await page.setViewportSize({width:390,height:844});await page.goto('/patient-app/group-invitation?draft='+draftRef);await page.getByRole('button',{name:'ساخت پیوند دعوت',exact:true}).click();await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:'/tmp/taranom-group-invitation-390.png',fullPage:true});await page.setViewportSize({width:320,height:740});expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
});

test('uncommitted issue and response can retry the exact command after a read confirms it is still absent',async({page})=>{
 await session(page);let issued:Record<string,string>|null=null,issueCalls=0;
 await page.route('**/api/v1/me/group-invitation-drafts**',r=>{const req=r.request();if(new URL(req.url()).pathname.endsWith('/invitations')){if(req.method()==='POST'){issueCalls++;const c=req.postDataJSON();if(!issued){issued=c;return r.abort()}expect(c).toEqual(issued);return r.fulfill({json:{outcome:'Replayed',invitation:{...base,requestId:c.requestId}}})}return r.fulfill({json:{invitation:null}})}return r.fulfill({json:{drafts:[{draftRef,requestId:draftRef,method:'ExistingMember',recipientLabel:'پیش‌نویس آزمایشی',state:'Draft',createdAt,withdrawnAt:null}],nextBefore:null}})});
 await page.goto('/patient-app/group-invitation?draft='+draftRef);await page.getByRole('button',{name:'ساخت پیوند دعوت',exact:true}).click();await page.getByRole('button',{name:'بررسی نتیجهٔ دعوت'}).click();await page.getByRole('button',{name:'تلاش مجدد همان درخواست'}).click();await expect(page.locator('.group-invitation-link textarea')).toBeVisible();expect(issueCalls).toBe(2);
 await page.goto('/patient-app/account');let responses=0,responded=false;
 await page.route('**/api/v1/me/group-invitations/*',r=>{const c=r.request().postDataJSON();if(r.request().url().endsWith('/respond')){responses++;expect(c.decision).toBe('Accepted');if(responses===1)return r.abort();responded=true}return r.fulfill({json:{invitation:{...base,state:responded?'Accepted':'Pending',respondedAt:responded?'2026-11-07T10:00:00Z':null},consentVersion:'group-invitation-v1'}})});
 await page.goto('/patient-app/join-group#'+secret);await page.getByRole('button',{name:'بررسی دعوت',exact:true}).click();await page.getByRole('checkbox').check();await page.getByRole('button',{name:'پذیرش دعوت',exact:true}).click();await page.getByRole('button',{name:'بررسی نتیجهٔ دعوت'}).click();await page.getByRole('button',{name:'تلاش مجدد همان درخواست'}).click();await expect(page.getByRole('region',{name:'وضعیت دعوت'})).toContainText('دعوت پذیرفته شد');expect(responses).toBe(2);
});
