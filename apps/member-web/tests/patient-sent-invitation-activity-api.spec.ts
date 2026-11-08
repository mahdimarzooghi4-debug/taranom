import {test,expect,type Page} from '@playwright/test';

const ref=(n:number)=>'550e8400-e29b-41d4-a716-'+n.toString().padStart(12,'0');
const states=['Pending','Accepted','Declined','Revoked','Expired'] as const;
const invitations=Array.from({length:22},(_,i)=>{
 const state=states[i%states.length];
 return {
  invitationRef:ref(i+1),draftRef:ref(i+101),
  recipientLabel:'نام نمایشی پیش‌نویس '+(i+1),
  draftState:state==='Revoked'?'Withdrawn':'Draft',
  state,createdAt:'2026-11-07T09:00:00Z',
  expiresAt:'2026-11-14T09:00:00Z',
  respondedAt:state==='Accepted'||state==='Declined'?'2026-11-07T10:00:00Z':null
 };
});
async function setup(page:Page){
 await page.addInitScript(()=>{window.taranomSession={
  getAccessToken:()=> 'synthetic-owner-activity-test-session'
 }});
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  const request=route.request(),url=new URL(request.url());
  expect(request.method()).toBe('GET');
  expect(request.headers().authorization).toBe('Bearer synthetic-owner-activity-test-session');
  const exact=url.searchParams.get('invitationRef');
  const data=exact?invitations.filter(x=>x.invitationRef===exact)
   :url.searchParams.has('before')?invitations.slice(20):invitations.slice(0,20);
  return route.fulfill({json:{
   invitations:data,
   nextBefore:exact||url.searchParams.has('before')?null:'20'
  }});
 });
}
test('owner reviews all five effective statuses, pages 20+2 and opens exact history',async({page})=>{
 await setup(page);
 await page.goto('/patient-app/invitations-sent');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 for(const state of ['منتظر پاسخ','پاسخ: پذیرفته شد','پاسخ: رد شد','پیوند لغو شده','پیوند منقضی شده'])
  await expect(page.locator('.owner-invitation-activity')).toContainText(state);
 await expect(page.locator('.owner-invitation-activity')).toContainText('نام نمایشی پیش‌نویس 1');
 await expect(page.locator('.group-draft-boundary')).toContainText('هویت گیرنده را تأیید نمی‌کند');
 await page.getByRole('button',{name:'دعوت‌های قدیمی‌تر'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(2);
 await page.getByRole('link',{name:'جزئیات دعوت ثبت‌شده'}).first().click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 await expect(page.locator('.owner-invitation-activity')).toContainText(ref(21));
 await page.reload();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 await page.getByRole('link',{name:'همهٔ دعوت‌های ارسالی'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 expect(new URL(page.url()).hash).toBe('');
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
test('no session makes no private request and never shows a demo recipient',async({page})=>{
 let calls=0;
 page.on('request',req=>{if(req.url().includes('/api/'))calls++});
 await page.goto('/patient-app/invitations-sent');
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 expect(calls).toBe(0);
});
test('denied refresh removes previously visible private invitation labels',async({page})=>{
 await setup(page);
 await page.goto('/patient-app/invitations-sent');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>
  route.fulfill({status:403,json:{code:'FORBIDDEN'}}));
 await page.getByRole('button',{name:'تازه‌سازی وضعیت دعوت‌ها'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 await expect(page.locator('.owner-invitation-activity')).not.toContainText('نام نمایشی پیش‌نویس 1');
});
test('forged authority, extra secrets, duplicate rows and mismatched exact receipts fail closed',async({page})=>{
 await setup(page);
 for(const injected of [
  [{...invitations[0],memberActive:true}],
  [{...invitations[0],tokenHash:'forbidden'}],
  [{...invitations[0],recipientSubject:'identity-leak'}],
  [{...invitations[0],state:'PaidMembership'}],
  [invitations[0],invitations[0]],
  [invitations[1]]
 ]){
  await page.route('**/api/v1/me/group-invitation-activity*',route=>
   route.fulfill({json:{invitations:injected,nextBefore:null}}));
  await page.goto('/patient-app/invitations-sent?invitation='+ref(1));
  await expect(page.getByRole('alert')).toContainText('معتبر نیست');
  await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 }
});
test('empty activity, invalid reference and mobile shell keep truthful no-access states',async({page})=>{
 await setup(page);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>
  route.fulfill({json:{invitations:[],nextBefore:null}}));
 await page.goto('/patient-app/invitations-sent');
 await expect(page.getByRole('status')).toContainText('هنوز پیوند دعوتی');
 let calls=0;
 page.on('request',req=>{if(req.url().includes('/api/'))calls++});
 await page.goto('/patient-app/invitations-sent?invitation=invalid');
 await expect(page.getByRole('alert')).toContainText('شناسهٔ دعوت معتبر نیست');
 expect(calls).toBe(0);
 await page.setViewportSize({width:390,height:844});
 await page.goto('/patient-app/invitations-sent');
 await page.evaluate(()=>document.fonts.ready);
 for(const selector of ['.appointments-logo','.appointments-bell img','.appointments-nav img'])
  for(const img of await page.locator(selector).all())
   expect(await img.evaluate(el=>(el as HTMLImageElement).complete&&
    (el as HTMLImageElement).naturalWidth>0)).toBe(true);
 await page.setViewportSize({width:320,height:740});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
});
test('account navigation opens private sent-invitations view, not recipient history',async({page})=>{
 await setup(page);
 await page.route('**/api/v1/me/profile',route=>route.fulfill({
  json:{profile:{revision:0,fullName:'',birthDate:null,city:''}}
 }));
 await page.goto('/patient-app/account');
 await page.getByRole('link',{name:'پیگیری دعوت‌های ارسالی'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 await expect(page.locator('.owner-invitation-activity')).toContainText('پیگیری پاسخ‌های ثبت‌شده');
});
