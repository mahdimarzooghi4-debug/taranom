import {expect,test} from '@playwright/test';
// TRN-TECH-199. Synthetic PostgreSQL care journal only.
// A hand-carried claim locator is not a delivered message or medical evidence.
const root='/internal/demo/v1/care-coordination/';
const headers={Origin:'http://127.0.0.1:4320'};
const ZERO='00000000-0000-0000-0000-000000000000';
type Action='RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
async function seed(request:import('@playwright/test').APIRequestContext){
 const ref='test:return190-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member190',originRef='test:reason190',rootId=crypto.randomUUID();
 const command={requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef,
  originalMemberRequestRef:originRef,originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(root+ref+'/commands',{data:command,headers})).status()).toBe(200);
 return {ref,memberRef,originRef,rootId,command};
}
type Seed=Awaited<ReturnType<typeof seed>>;
async function append(request:import('@playwright/test').APIRequestContext,
 s:Seed,rev:number,action:Action,ref='test:note190'){
 const cmd={...s.command,requestId:crypto.randomUUID(),expectedRevision:rev,action,
  originalSubmissionRequestId:s.rootId,claimedActorRef:'test:actor190',
  unverifiedNoteOrDocumentRef:ref};
 const result=await request.post(root+s.ref+'/commands',{data:cmd,headers});
 expect(result.status()).toBe(200);
 return cmd.requestId;
}
async function center(page:import('@playwright/test').Page,s:Seed,expected=1){
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(s.ref);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه '+expected);
 return panel;
}
async function member(page:import('@playwright/test').Page,s:Seed){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.ref);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 return page.getByTestId('member-care-claim-return');
}
const locatorLabel='رسید دستی بازگشت ادعای هماهنگی مرکز';
const importLabel='ورود رسید بازگشت ادعای هماهنگی مرکز';
const confirmLabel='بررسی مستقل ادعای بازگشتی از PostgreSQL';
async function confirm(page:import('@playwright/test').Page,ticket:string){
 const panel=page.getByTestId('member-care-claim-return');
 await panel.getByLabel(importLabel).fill(ticket);
 await panel.getByRole('button',{name:confirmLabel}).click();
 return panel;
}
test('CARE190-01 Center separately records unverified contact then Member independently verifies returned claim with only one GET',async({page,request})=>{
 const s=await seed(request),cen=await center(page,s);
 await expect(cen.getByTestId('center-care-claim-return')).toHaveCount(0);
 await cen.getByLabel('مرجع یادداشت تماس آزمایشی').fill('test:contact190');
 await cen.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(cen.getByTestId('center-care-source')).toContainText('نسخه 2');
 const exportPanel=cen.getByTestId('center-care-claim-return');
 await expect(exportPanel).toBeVisible();
 const data=await exportPanel.getByLabel(locatorLabel).inputValue();
 const ticket=JSON.parse(data);
 expect(Object.keys(ticket).sort()).toEqual([
  'format','version','caseRef','memberRef','originalMemberRequestRef',
  'rootRequestId','revision','claimRequestId','claimAction',
  'claimedActorRef','claimRef'].sort());
 expect(ticket.revision).toBe(2);
 expect(ticket.claimAction).toBe('RecordUnverifiedCoordinatorContact');
 expect(ticket.claimRef).toBe('test:contact190');
 expect(ticket.rootRequestId).toBe(s.rootId);
 const mem=await member(page,s);
 let gets=0,posts=0;
 page.on('request',r=>{
  if(r.method()==='GET'&&r.url().includes('/care-coordination/'))gets++;
  if(r.method()==='POST'&&r.url().includes('/care-coordination/'))posts++;
 });
 await confirm(page,data);
 await expect(mem.getByTestId('member-care-claim-return-confirmed'))
  .toContainText('test:contact190');
 await expect(mem.getByTestId('member-care-claim-return-confirmed'))
  .toContainText(ticket.claimRequestId);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: 2');
 expect(gets).toBe(1);expect(posts).toBe(0);
 const source=await (await request.get(root+s.ref)).json();
 expect(source.events[1].requestId).toBe(ticket.claimRequestId);
 expect(source.realCoordinatorVerified).toBe(false);
 expect(source.clinicalSourceVerified).toBe(false);
 expect(source.nationalReleaseAllowed).toBe(false);
});
test('CARE190-02 invalid, extra and real-identifying payloads deny without GET; forged source-bound fields deny with independent GET',async({page,request})=>{
 const s=await seed(request);
 await append(request,s,1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 const cen=await center(page,s,2);
 // Center is manually re-read after existing append.
 await expect(cen.getByTestId('center-care-source')).toContainText('نسخه 2');
 const ticket=JSON.parse(await cen.getByTestId('center-care-claim-return')
  .getByLabel(locatorLabel).inputValue());
 await member(page,s);
 let calls=0,posts=0;
 page.on('request',r=>{
  if(r.url().includes('/care-coordination/')&&r.method()==='GET')calls++;
  if(r.method()==='POST')posts++;
 });
 const invalid=[
  {...ticket,caseRef:'real:member'},
  {...ticket,format:'SIGNED_PROVIDER_REFERRAL'},
  {...ticket,consentGranted:true},
  {...ticket,claimRequestId:'not-a-uuid'},
  {...ticket,revision:1.5},
  {...ticket,claimAction:'DoctorAuthorizedTreatment'}
 ];
 for(const item of invalid){
  await confirm(page,JSON.stringify(item));
  await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
  await expect(page.getByTestId('care-status')).toContainText('مطابقت ندارد');
 }
 expect(calls).toBe(0);expect(posts).toBe(0);
 // Structurally valid but wrong root or note may reach GET but never pass.
 for(const item of [
  {...ticket,rootRequestId:crypto.randomUUID()},
  {...ticket,claimRef:'test:forged190'},
  {...ticket,claimRequestId:crypto.randomUUID()}
 ]){
  await confirm(page,JSON.stringify(item));
  await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
  await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 }
 expect(calls).toBe(3);expect(posts).toBe(0);
});
test('CARE190-03 stale tip is rejected after unrelated append, then Center manual reread enables new return',async({page,request})=>{
 const s=await seed(request);
 await append(request,s,1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 let cen=await center(page,s,2);
 const first=await cen.getByTestId('center-care-claim-return').getByLabel(locatorLabel).inputValue();
 await append(request,s,2,'ATTACH_UNVERIFIED_CLINICAL_SOURCE','test:source190');
 const mem=await member(page,s);
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await confirm(page,first);
 await expect(mem.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
 await expect(page.getByTestId('care-status')).toContainText('مطابقت ندارد');
 cen=await center(page,s,3);
 await expect(cen.getByTestId('center-care-source')).toContainText('نسخه 3');
 const fresh=await cen.getByTestId('center-care-claim-return').getByLabel(locatorLabel).inputValue();
 expect(JSON.parse(fresh).claimAction).toBe('AttachUnverifiedClinicalSourceReference');
 expect(JSON.parse(fresh).claimRef).toBe('test:source190');
 await member(page,s);
 await confirm(page,fresh);
 await expect(page.getByTestId('member-care-claim-return-confirmed'))
  .toContainText('test:source190');
 expect(posts).toBe(0);
});
test('CARE190-04 forged official-authority source fails closed; valid manual retry succeeds and editing inputs clears confirmation',async({page,request})=>{
 const s=await seed(request);
 await append(request,s,1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 const cen=await center(page,s,2);
 const ticket=await cen.getByTestId('center-care-claim-return').getByLabel(locatorLabel).inputValue();
 await member(page,s);
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const response=await route.fetch(),body=await response.json();
  body.clinicianAuthorized=true;body.nationalReleaseAllowed=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await confirm(page,ticket);
 await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 await confirm(page,ticket);
 await expect(page.getByTestId('member-care-claim-return-confirmed')).toBeVisible();
 await page.getByLabel('شناسه آزمایشی عضو').fill('test:changed190');
 await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 const status=await (await request.get(root+s.ref)).json();
 expect(status.revision).toBe(2);
 expect(status.consentGranted).toBe(false);
});
test('CARE190-05 uncertain Center command cannot export a claim and Member pending write prevents importing return',async({page,request})=>{
 const s=await seed(request),cen=await center(page,s);
 let accepted=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  const response=await route.fetch();expect(response.status()).toBe(200);
  accepted++;await route.abort();
 });
 await cen.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(cen.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(cen.getByTestId('center-care-claim-return')).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 await cen.getByRole('button',{name:'بررسی فقط‌خواندنی همان تماس'}).click();
 const ticket=await cen.getByTestId('center-care-claim-return').getByLabel(locatorLabel).inputValue();
 await member(page,s);
 // Import is disabled when the existing exact UUID recovery latch is held.
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  const response=await route.fetch();expect(response.status()).toBe(200);
  await route.abort();
 });
 await page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByTestId('member-care-claim-return').getByLabel(importLabel)).toBeDisabled();
 await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 expect(accepted).toBe(1);
 const persisted=await (await request.get(root+s.ref)).json();
 expect(persisted.nationalReleaseAllowed).toBe(false);
 expect(ticket).toContain('TRN-TECH-199');
});
