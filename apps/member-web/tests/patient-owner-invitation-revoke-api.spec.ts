import {test,expect,type Page,type Route} from '@playwright/test';
const draftRef='550e8400-e29b-41d4-a716-446655440000';
const invitationRef='550e8400-e29b-41d4-a716-446655440001';
const createdAt='2026-11-07T09:00:00Z',expiresAt='2026-11-14T09:00:00Z';
const revokePath='/api/v1/me/group-invitation-drafts/'+draftRef+'/invitations/'+invitationRef+'/revoke';
type State='Pending'|'Accepted'|'Declined'|'Revoked'|'Expired';
const card=(state:State='Pending',draftState:'Draft'|'Withdrawn'='Draft')=>({
 invitationRef,draftRef,recipientLabel:'نام خصوصی برنامه‌ریزی',draftState,
 state,createdAt,expiresAt,
 respondedAt:state==='Accepted'||state==='Declined'?'2026-11-07T10:00:00Z':null
});
async function signed(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-owner-revoke'}});
}
async function activity(page:Page,get:()=>ReturnType<typeof card>){
 await signed(page);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  expect(route.request().method()).toBe('GET');
  expect(route.request().headers().authorization).toBe('Bearer synthetic-owner-revoke');
  const exact=new URL(route.request().url()).searchParams.get('invitationRef');
  return route.fulfill({json:{invitations:exact&&exact!==invitationRef?[]:[get()],nextBefore:null}});
 });
}
function receipt(state:State='Revoked',extra:Record<string,unknown>={}){
 return {outcome:'Applied',invitation:{
  invitationRef,requestId:'550e8400-e29b-41d4-a716-446655440099',
  state,createdAt,expiresAt,respondedAt:null,...extra
 }};
}
async function firstConfirm(page:Page){
 await page.getByRole('button',{name:'لغو پیوند در انتظار'}).click();
 await expect(page.getByLabel('تأیید لغو پیوند')).toBeVisible();
 await expect(page.getByLabel('تأیید لغو پیوند')).toContainText('پاسخ پذیرفته‌شده یا ردشده تغییر نمی‌کند');
}
test('owner must confirm one pending cancellation and may dismiss it without a POST',async({page})=>{
 let state:State='Pending',writes=0;
 await activity(page,()=>card(state));
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',route=>{
  expect(new URL(route.request().url()).pathname).toBe(revokePath);
  expect(route.request().method()).toBe('POST');
  expect(route.request().postData()).toBeNull();
  writes++;state='Revoked';return route.fulfill({json:receipt()});
 });
 await page.goto('/patient-app/invitations-sent');
 await firstConfirm(page);
 await page.getByRole('button',{name:'انصراف از لغو'}).click();
 expect(writes).toBe(0);
 await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toBeVisible();
 await firstConfirm(page);
 await page.getByRole('button',{name:'تأیید لغو همین پیوند'}).click();
 await expect(page.getByText('لغو پیوند دعوت ثبت و تأیید شد.')).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toContainText('پیوند لغو شده');
 await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toHaveCount(0);
 expect(writes).toBe(1);
 expect(await page.evaluate(()=>localStorage.length+sessionStorage.length)).toBe(0);
});
test('lost acknowledgement is reconciled from exact owner GET with zero duplicate writes',async({page})=>{
 let state:State='Pending',writes=0,exactReads=0;
 await activity(page,()=>card(state));
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  const u=new URL(route.request().url());
  if(u.searchParams.get('invitationRef')===invitationRef)exactReads++;
  return route.fulfill({json:{invitations:[card(state)],nextBefore:null}});
 });
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',route=>{
  writes++;state='Revoked';return route.abort();
 });
 await page.goto('/patient-app/invitations-sent');
 await firstConfirm(page);
 await page.getByRole('button',{name:'تأیید لغو همین پیوند'}).click();
 await expect(page.getByRole('region',{name:'بازیابی نتیجه لغو'})).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 await page.getByRole('button',{name:'بررسی نتیجهٔ لغو'}).click();
 await expect(page.getByText('غیرفعال‌شدن پیوند دعوت تأیید شد.')).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toContainText('پیوند لغو شده');
 expect(exactReads).toBe(1);expect(writes).toBe(1);
});
test('uncommitted cancellation can retry the identical reference only after exact GET confirms Pending',async({page})=>{
 let state:State='Pending',writes=0,read=0;
 await activity(page,()=>card(state));
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  const u=new URL(route.request().url());
  if(u.searchParams.has('invitationRef'))read++;
  return route.fulfill({json:{invitations:[card(state)],nextBefore:null}});
 });
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',route=>{
  expect(new URL(route.request().url()).pathname).toBe(revokePath);
  writes++;if(writes===1)return route.abort();
  state='Revoked';return route.fulfill({json:receipt()});
 });
 await page.goto('/patient-app/invitations-sent');
 await firstConfirm(page);
 await page.getByRole('button',{name:'تأیید لغو همین پیوند'}).click();
 await expect(page.getByRole('button',{name:'تلاش مجدد همان لغو'})).toHaveCount(0);
 await page.getByRole('button',{name:'بررسی نتیجهٔ لغو'}).click();
 await expect(page.getByRole('button',{name:'تلاش مجدد همان لغو'})).toBeVisible();
 await page.getByRole('button',{name:'تلاش مجدد همان لغو'}).click();
 await expect(page.getByText('لغو پیوند دعوت ثبت و تأیید شد.')).toBeVisible();
 expect(read).toBe(1);expect(writes).toBe(2);
});
test('final consent, expired links and archived drafts offer no revocation control',async({page})=>{
 let status:State='Accepted',draft:'Draft'|'Withdrawn'='Draft',writes=0;
 await activity(page,()=>card(status,draft));
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',r=>{writes++;return r.abort()});
 for(const [state,ds] of [
  ['Accepted','Draft'],['Declined','Draft'],['Expired','Draft'],
  ['Revoked','Draft'],['Pending','Withdrawn']
 ] as const){
  status=state;draft=ds;
  await page.goto('/patient-app/invitations-sent');
  await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
  await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toHaveCount(0);
 }
 expect(writes).toBe(0);
});
test('forged successful response is refused; exact read is needed to confirm outcome',async({page})=>{
 let state:State='Pending',writes=0;
 await activity(page,()=>card(state));
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',route=>{
  writes++;state='Revoked';
  return route.fulfill({json:receipt('Revoked',{recipientSubject:'forged-member'})});
 });
 await page.goto('/patient-app/invitations-sent');
 await firstConfirm(page);
 await page.getByRole('button',{name:'تأیید لغو همین پیوند'}).click();
 await expect(page.getByRole('alert')).toContainText('قابل تأیید نیست');
 await expect(page.getByRole('region',{name:'بازیابی نتیجه لغو'})).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 await page.getByRole('button',{name:'بررسی نتیجهٔ لغو'}).click();
 await expect(page.getByText('غیرفعال‌شدن پیوند دعوت تأیید شد.')).toBeVisible();
 expect(writes).toBe(1);
});
test('no host identity denies reads/writes; owner screen remains RTL at 390 and 320',async({page})=>{
 let calls=0;
 page.on('request',req=>{if(new URL(req.url()).pathname.startsWith('/api/'))calls++});
 await page.goto('/patient-app/invitations-sent');
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toHaveCount(0);
 expect(calls).toBe(0);
 await activity(page,()=>card('Pending'));
 await page.setViewportSize({width:390,height:844});
 await page.goto('/patient-app/invitations-sent');
 await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toBeVisible();
 await firstConfirm(page);
 await page.setViewportSize({width:320,height:740});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 expect(await page.locator('html').getAttribute('dir')).toBe('rtl');
});
