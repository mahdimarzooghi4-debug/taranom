import {test,expect,type Page} from '@playwright/test';

const id=(i:number)=>'550e8400-e29b-41d4-a716-'+String(i).padStart(12,'0');
const draftA=id(101),draftB=id(102);
const createdAt='2026-11-07T09:00:00Z',expiresAt='2026-11-14T09:00:00Z';
type Status='Pending'|'Accepted'|'Declined'|'Revoked'|'Expired';
type Entry={
 invitationRef:string;draftRef:string;recipientLabel:string;draftState:'Draft'|'Withdrawn';
 state:Status;createdAt:string;expiresAt:string;respondedAt:string|null;
};
const card=(i:number,state:Status,draftRef:string=draftA):Entry=>({
 invitationRef:id(i),draftRef,recipientLabel:'دعوت آزمایشی '+i,
 draftState:state==='Revoked'?'Withdrawn':'Draft',state,createdAt,expiresAt,
 respondedAt:state==='Accepted'||state==='Declined'?'2026-11-07T10:00:00Z':null
});
const items:Entry[]=[
 ...Array.from({length:22},(_,i)=>card(23-i,i%2?'Declined':'Revoked',i%2?draftA:draftB)),
 card(1,'Pending',draftA)
];
async function session(page:Page){
 await page.addInitScript(()=>{window.taranomSession={getAccessToken:()=> 'synthetic-pending-worklist'}});
}
async function mock(page:Page,collect:(url:URL)=>void=()=>{}){
 await session(page);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  const req=route.request(),url=new URL(req.url());
  expect(req.method()).toBe('GET');
  expect(req.headers().authorization).toBe('Bearer synthetic-pending-worklist');
  collect(url);
  const exact=url.searchParams.get('invitationRef');
  const draft=url.searchParams.get('draftRef');
  let rows=items.filter(row=>!exact||row.invitationRef===exact);
  if(draft)rows=rows.filter(row=>row.draftRef===draft);
  if(url.searchParams.get('state')==='Pending')
   rows=rows.filter(row=>row.state==='Pending'&&row.draftState==='Draft');
  const start=url.searchParams.has('before')?20:0;
  const invitations=exact?rows:rows.slice(start,start+20);
  return route.fulfill({json:{
   invitations,nextBefore:exact||rows.length<=start+20?null:String(start+20)
  }});
 });
}
test('server Pending filter exposes older actionable link hidden below twenty terminal entries',async({page})=>{
 const calls:URL[]=[];await mock(page,u=>calls.push(u));
 await page.goto('/patient-app/invitations-sent');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 await expect(page.getByRole('heading',{name:'دعوت آزمایشی 1',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await expect(page.getByRole('button',{name:'در انتظار پاسخ'})).toHaveAttribute('aria-pressed','true');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toContainText('دعوت آزمایشی 1');
 await expect(page.getByRole('button',{name:'لغو پیوند در انتظار'})).toBeVisible();
 expect(calls.at(-1)!.searchParams.get('state')).toBe('Pending');
 expect(calls.at(-1)!.searchParams.has('before')).toBe(false);
 await page.getByRole('button',{name:'همه دعوت‌ها'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 expect(calls.at(-1)!.searchParams.has('state')).toBe(false);
});
test('change of server mode drops previous page cursor and does not blend filtered data',async({page})=>{
 const calls:URL[]=[];await mock(page,u=>calls.push(u));
 await page.goto('/patient-app/invitations-sent');
 await page.getByRole('button',{name:'دعوت‌های قدیمی‌تر'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(3);
 expect(calls.at(-1)!.searchParams.get('before')).toBe('20');
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 expect(calls.at(-1)!.searchParams.has('before')).toBe(false);
 expect(calls.at(-1)!.searchParams.get('state')).toBe('Pending');
 await page.getByRole('button',{name:'همه دعوت‌ها'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 expect(calls.at(-1)!.searchParams.has('before')).toBe(false);
});
test('draft card navigates into owner-only draft timeline, with independent Pending mode',async({page})=>{
 const calls:URL[]=[];await mock(page,u=>calls.push(u));
 await page.route('**/api/v1/me/group-invitation-drafts*',route=>{
  expect(route.request().method()).toBe('GET');
  return route.fulfill({json:{
   drafts:[{
    draftRef:draftA,requestId:id(998),method:'ExistingMember',
    recipientLabel:'نمونه غیرهویتی',state:'Draft',createdAt,withdrawnAt:null,latestInvitation:null
   }],nextBefore:null
  }});
 });
 await page.goto('/patient-app/group-members');
 await page.getByRole('link',{name:'تاریخچهٔ دعوت‌های این پیش‌نویس'}).click();
 await expect(page).toHaveURL(new RegExp('invitations-sent\\?draft='+draftA));
 await expect(page.getByText(draftA)).toBeVisible();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(
  items.filter(x=>x.draftRef===draftA).length>20?20:items.filter(x=>x.draftRef===draftA).length
 );
 expect(calls.at(-1)!.searchParams.get('draftRef')).toBe(draftA);
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 expect(calls.at(-1)!.searchParams.get('draftRef')).toBe(draftA);
 expect(calls.at(-1)!.searchParams.get('state')).toBe('Pending');
 await page.getByRole('link',{name:'همهٔ دعوت‌های ارسالی'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
});
test('foreign or forged server-filter reply is rejected without keeping private activity',async({page})=>{
 await mock(page);
 await page.goto('/patient-app/invitations-sent');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(20);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>
  route.fulfill({json:{invitations:[card(23,'Declined',draftA)],nextBefore:null}}));
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await expect(page.getByRole('alert')).toContainText('معتبر نیست');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>
  route.fulfill({json:{invitations:[card(1,'Pending',draftB)],nextBefore:null}}));
 await page.goto('/patient-app/invitations-sent?draft='+draftA);
 await expect(page.getByRole('alert')).toContainText('معتبر نیست');
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(0);
});
test('lost revoke acknowledgement in Pending mode still reconciles by exact unfiltered reference',async({page})=>{
 let current:Status='Pending',writes=0,exactReads=0;
 await session(page);
 await page.route('**/api/v1/me/group-invitation-activity*',route=>{
  const url=new URL(route.request().url()),exact=url.searchParams.get('invitationRef');
  if(exact){
   exactReads++;
   expect(exact).toBe(id(1));
   expect(url.searchParams.size).toBe(1);
  }
  const rows=exact?[card(1,current)]:
   url.searchParams.get('state')==='Pending'&&current!=='Pending'?[]:
   url.searchParams.get('state')==='Pending'?[card(1,current)]:[card(1,current)];
  return route.fulfill({json:{invitations:rows,nextBefore:null}});
 });
 await page.route('**/api/v1/me/group-invitation-drafts/*/invitations/*/revoke',route=>{
  writes++;expect(route.request().method()).toBe('POST');
  expect(new URL(route.request().url()).pathname)
   .toBe('/api/v1/me/group-invitation-drafts/'+draftA+'/invitations/'+id(1)+'/revoke');
  current='Revoked';return route.abort();
 });
 await page.goto('/patient-app/invitations-sent');
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await page.getByRole('button',{name:'لغو پیوند در انتظار'}).click();
 await page.getByRole('button',{name:'تأیید لغو همین پیوند'}).click();
 await expect(page.getByRole('region',{name:'بازیابی نتیجه لغو'})).toBeVisible();
 await page.getByRole('button',{name:'بررسی نتیجهٔ لغو'}).click();
 await expect(page.getByText('غیرفعال‌شدن پیوند دعوت تأیید شد.')).toBeVisible();
 await expect(page.getByText('در این بخش پیوندی در انتظار پاسخ وجود ندارد.')).toBeVisible();
 expect(writes).toBe(1);expect(exactReads).toBe(1);
});
test('invalid/conflicting draft detail and no host session cannot issue private network traffic',async({page})=>{
 let calls=0;page.on('request',req=>{
  if(new URL(req.url()).pathname.startsWith('/api/'))calls++;
 });
 await page.goto('/patient-app/invitations-sent?draft='+draftA);
 await expect(page.getByRole('alert')).toContainText('نشست ورود');
 expect(calls).toBe(0);
 await page.goto('/patient-app/invitations-sent?draft=invalid');
 await expect(page.getByRole('alert')).toContainText('شناسهٔ دعوت یا پیش‌نویس');
 await page.goto('/patient-app/invitations-sent?invitation='+id(1)+'&draft='+draftA);
 await expect(page.getByRole('alert')).toContainText('شناسهٔ دعوت یا پیش‌نویس');
 expect(calls).toBe(0);
});
test('Pending filter and draft timeline keep Persian RTL and no overflow at 390 and 320px',async({page})=>{
 await mock(page);await page.setViewportSize({width:390,height:844});
 await page.goto('/patient-app/invitations-sent');
 await page.getByRole('button',{name:'در انتظار پاسخ'}).click();
 await expect(page.locator('.owner-invitation-activity .group-draft-card')).toHaveCount(1);
 await page.evaluate(()=>document.fonts.ready);
 for(const selector of ['.appointments-logo','.appointments-bell img','.appointments-nav img'])
  for(const image of await page.locator(selector).all())
   expect(await image.evaluate(e=>(e as HTMLImageElement).complete&&
    (e as HTMLImageElement).naturalWidth>0)).toBe(true);
 await page.setViewportSize({width:320,height:740});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)).toBe(false);
 expect(await page.locator('html').getAttribute('dir')).toBe('rtl');
});
