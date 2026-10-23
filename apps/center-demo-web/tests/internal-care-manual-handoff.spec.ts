import {expect,test} from '@playwright/test';
// TRN-TECH-198: human-carried locator for test-only coordination, NOT
// authenticated access, treatment, real referral or notification.
const api='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
const ZERO='00000000-0000-0000-0000-000000000000';
type Seed={ref:string;memberRef:string;rootId:string;requestRef:string;first:{
 requestId:string;expectedRevision:number;action:string;memberRef:string;
 originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:null;unverifiedNoteOrDocumentRef:null}};
async function seed(request:import('@playwright/test').APIRequestContext):Promise<Seed>{
 const ref='test:care189-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member189',requestRef='test:reason189',rootId=crypto.randomUUID();
 const first={requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef,
  originalMemberRequestRef:requestRef,originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(api+ref+'/commands',{data:first,headers:origin})).status()).toBe(200);
 return {ref,memberRef,requestRef,rootId,first};
}
async function member(page:import('@playwright/test').Page,s:Seed){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.ref);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: 1');
 return page.getByTestId('member-care-handoff');
}
async function center(page:import('@playwright/test').Page){
 await page.goto('/');
 return page.getByTestId('center-care-coordination');
}
const importLabel='ورود رسید تحویل هماهنگی عضو';
const acceptLabel='بررسی مستقل رسید هماهنگی عضو از PostgreSQL';
async function accept(work:import('@playwright/test').Locator,text:string){
 await work.getByLabel(importLabel).fill(text);
 await work.getByRole('button',{name:acceptLabel}).click();
}
test('CARE189-01 Member manually creates request, carries source-pinned ticket to Center and Center claims contact only on separate click',async({page,request})=>{
 await page.goto('/member-demo/coordination');
 const ref=await page.getByLabel('شناسه آزمایشی پرونده').inputValue();
 await expect(page.getByTestId('member-care-handoff')).toHaveCount(0);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('member-care-handoff')).toHaveCount(0);
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 const ticketPanel=page.getByTestId('member-care-handoff');
 await expect(ticketPanel).toBeVisible();
 const ticketText=await ticketPanel.getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue();
 const ticket=JSON.parse(ticketText);
 expect(Object.keys(ticket).sort()).toEqual([
  'caseRef','format','lastEventRequestId','memberRef',
  'originalMemberRequestRef','revision','rootRequestId','version'].sort());
 expect(ticket.caseRef).toBe(ref);expect(ticket.revision).toBe(1);
 expect(ticket.lastEventRequestId).toBe(ticket.rootRequestId);
 const verified=await (await request.get(api+ref)).json();
 expect(ticket.rootRequestId).toBe(verified.events[0].requestId);
 expect(verified.nationalReleaseAllowed).toBe(false);
 const work=await center(page);
 let sent=0,reads=0;
 page.on('request',r=>{
  if(r.method()==='POST'&&r.url().includes('/care-coordination/'))sent++;
  if(r.method()==='GET'&&r.url().includes('/care-coordination/'))reads++;
 });
 await accept(work,ticketText);
 await expect(work.getByTestId('center-care-handoff-confirmed')).toContainText(ref);
 await expect(work.getByTestId('center-care-source')).toContainText('نسخه 1');
 expect(sent).toBe(0);expect(reads).toBe(1);
 await work.getByLabel('مرجع یادداشت تماس آزمایشی').fill('test:note189');
 await work.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(work.getByTestId('center-care-source')).toContainText('نسخه 2');
 expect(sent).toBe(1);
 const read=await (await request.get(api+ref)).json();
 expect(read.events[1].action).toBe('RecordUnverifiedCoordinatorContact');
 expect(read.events[1].unverifiedNoteOrDocumentRef).toBe('test:note189');
 expect(read.realCoordinatorVerified).toBe(false);
 expect(read.referralOrdered).toBe(false);
});
test('CARE189-02 malformed/forged/extra authority handoffs rejected before ANY GET or POST',async({page,request})=>{
 const s=await seed(request),p=await member(page,s);
 const good=JSON.parse(await p.getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue());
 const work=await center(page);
 let calls=0;
 page.on('request',r=>{if(r.url().includes('/care-coordination/'))calls++;});
 const forged=[
  {...good,memberRef:'test:other-member'},
  {...good,rootRequestId:crypto.randomUUID()},
  {...good,caseRef:'real:person'},
  {...good,rootRequestId:crypto.randomUUID(),nationalReleaseAllowed:true},
  {...good,revision:0},
  {...good,rootRequestId:'wrong-id'},
  {...good,unexpectedClinicalPermission:true},
  {...good,format:'SIGNED_CLINICAL_REFERRAL'}
 ];
 // Structurally plausible wrong member and root UUID still require GET,
 // but must never be displayed or become a write authority.
 for(const payload of forged){
  await accept(work,JSON.stringify(payload));
  await expect(work.getByTestId('center-care-source')).toHaveCount(0);
  await expect(work.getByTestId('center-care-handoff-confirmed')).toHaveCount(0);
  await expect(work.getByTestId('center-care-status')).toContainText('قابل تأیید نیست');
 }
 expect(calls).toBe(2);
});
test('CARE189-03 stale version rejects manual ticket until Member exports new independently read snapshot',async({page,request})=>{
 const s=await seed(request),p=await member(page,s);
 const old=await p.getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue();
 const write=await request.post(api+s.ref+'/commands',{headers:origin,data:{
  ...s.first,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
  originalSubmissionRequestId:s.rootId,
  claimedActorRef:'test:actor189',unverifiedNoteOrDocumentRef:'test:contact189'
 }});
 expect(write.status()).toBe(200);
 const work=await center(page);
 let sent=0;page.on('request',r=>{if(r.method()==='POST')sent++;});
 await accept(work,old);
 await expect(work.getByTestId('center-care-handoff-confirmed')).toHaveCount(0);
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 await expect(work.getByTestId('center-care-status')).toContainText('قابل تأیید نیست');
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.ref);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const fresh=await page.getByTestId('member-care-handoff')
  .getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue();
 const next=JSON.parse(fresh);
 expect(next.revision).toBe(2);expect(next.lastEventRequestId).not.toBe(next.rootRequestId);
 const c=await center(page);
 await accept(c,fresh);
 await expect(c.getByTestId('center-care-handoff-confirmed')).toContainText('نسخه 2');
 await expect(c.getByTestId('center-care-source')).toContainText('نسخه 2');
 expect(sent).toBe(0);
});
test('CARE189-04 forged journal claiming real clinical authorization fails closed even with otherwise valid ticket',async({page,request})=>{
 const s=await seed(request),p=await member(page,s);
 const ticket=await p.getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue();
 const work=await center(page);
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const upstream=await route.fetch(),body=await upstream.json();
  body.clinicianAuthorized=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await accept(work,ticket);
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 await expect(work.getByTestId('center-care-handoff-confirmed')).toHaveCount(0);
 expect(posts).toBe(0);
 await page.unrouteAll({behavior:'wait'});
 await accept(work,ticket);
 await expect(work.getByTestId('center-care-handoff-confirmed')).toContainText(s.ref);
 const invalid={...JSON.parse(ticket),lastEventRequestId:crypto.randomUUID()};
 await accept(work,JSON.stringify(invalid));
 await expect(work.getByTestId('center-care-handoff-confirmed')).toHaveCount(0);
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 expect(posts).toBe(0);
});
test('CARE189-05 changing Member identity or an uncertain command hides export, and Center pending write blocks import',async({page,request})=>{
 const s=await seed(request),panel=await member(page,s);
 await expect(panel).toBeVisible();
 await page.getByLabel('شناسه آزمایشی عضو').fill('test:changed-member189');
 await expect(panel).toHaveCount(0);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const ticket=await page.getByTestId('member-care-handoff')
  .getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue();
 const work=await center(page);
 await accept(work,ticket);
 await expect(work.getByTestId('center-care-source')).toContainText('نسخه 1');
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  const response=await route.fetch();expect(response.status()).toBe(200);
  await route.abort();
 });
 await work.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(work.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(work.getByRole('button',{name:acceptLabel})).toBeDisabled();
 await expect(work.getByTestId('center-care-handoff-confirmed')).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 const actual=await (await request.get(api+s.ref)).json();
 expect(actual.revision).toBe(2);
 expect(actual.nationalReleaseAllowed).toBe(false);
});
