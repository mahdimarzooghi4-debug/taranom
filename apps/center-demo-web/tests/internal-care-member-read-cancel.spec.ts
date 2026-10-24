import {expect,test} from '@playwright/test';
// TRN-TECH-200: a late GET can NEVER resurrect a previously dismissed
// member case, return receipt or history. Original POST/UUID is never canceled.
const api='/internal/demo/v1/care-coordination/';
const headers={Origin:'http://127.0.0.1:4320'};
const emptyUuid='00000000-0000-0000-0000-000000000000';
async function seed(request:import('@playwright/test').APIRequestContext,follow=true){
 const ref='test:race191-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member191-'+crypto.randomUUID().replaceAll('-','');
 const originalMemberRequestRef='test:reason191';
 const rootId=crypto.randomUUID();
 const original={requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef,
  originalMemberRequestRef,originalSubmissionRequestId:emptyUuid,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(api+ref+'/commands',{headers,data:original})).status()).toBe(200);
 if(follow){
  expect((await request.post(api+ref+'/commands',{headers,data:{
   ...original,requestId:crypto.randomUUID(),expectedRevision:1,
   action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
   originalSubmissionRequestId:rootId,claimedActorRef:'test:actor191',
   unverifiedNoteOrDocumentRef:'test:note191'
  }})).status()).toBe(200);
 }
 const source=await (await request.get(api+ref)).json();
 return {ref,memberRef,source,rootId};
}
type Seed=Awaited<ReturnType<typeof seed>>;
async function open(page:import('@playwright/test').Page,s:Seed){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.ref);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
}
async function switchTo(page:import('@playwright/test').Page,s:Seed){
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.ref);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('member-care-handoff')).toBeVisible();
}
function defer(){
 let release!:()=>void,arrived!:()=>void;
 const hold=new Promise<void>(resolve=>{release=resolve;});
 const started=new Promise<void>(resolve=>{arrived=resolve;});
 return {hold,started,release,arrived};
}
function isCase(url:string,ref:string){
 return decodeURIComponent(new URL(url).pathname).endsWith('/care-coordination/'+ref);
}
async function cancel(page:import('@playwright/test').Page){
 const slot=page.getByTestId('member-care-read-cancel');
 await expect(slot).toBeVisible();
 await slot.getByRole('button',{name:'لغو خواندن آزمایشی بدون ثبت فرمان'}).click();
 await expect(slot).toHaveCount(0);
}
test('CARE191-01 Cancel delayed A journal read, manually open B, then late valid A response never replaces B',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 await open(page,a);
 const d=defer();
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(!isCase(route.request().url(),a.ref))return route.continue();
  d.arrived();await d.hold;
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(a.source)});
 });
 let browserPosts=0;
 page.on('request',r=>{if(r.method()==='POST')browserPosts++;});
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await d.started;await cancel(page);
 await switchTo(page,b);
 const exportTicket=page.getByTestId('member-care-handoff')
  .getByLabel('رسید تحویل دستی هماهنگی عضو');
 await expect(exportTicket).toHaveValue(/TRN-TECH-198/);
 expect(JSON.parse(await exportTicket.inputValue()).caseRef).toBe(b.ref);
 const staleResponse=page.waitForResponse(res=>isCase(res.url(),a.ref));
 d.release();await staleResponse;
 await expect(page.getByTestId('member-care-handoff')).toBeVisible();
 expect(JSON.parse(await exportTicket.inputValue()).caseRef).toBe(b.ref);
 await expect(page.getByTestId('care-status')).toContainText('از PostgreSQL');
 expect(browserPosts).toBe(0);
});
test('CARE191-02 Cancel late Center-return reattestation and switch Member case; no old confirmation or write',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 const last=a.source.events[a.source.revision-1];
 const ticket=JSON.stringify({
  format:'TRN-TECH-199-TEST-CARE-CLAIM-RETURN',version:1,caseRef:a.ref,
  memberRef:a.memberRef,originalMemberRequestRef:a.source.events[0].originalMemberRequestRef,
  rootRequestId:a.rootId,revision:a.source.revision,
  claimRequestId:last.requestId,claimAction:last.action,
  claimedActorRef:last.claimedActorRef,claimRef:last.unverifiedNoteOrDocumentRef
 });
 await open(page,a);
 const d=defer();
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(!isCase(route.request().url(),a.ref))return route.continue();
  d.arrived();await d.hold;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(a.source)});
 });
 await page.getByLabel('ورود رسید بازگشت ادعای هماهنگی مرکز').fill(ticket);
 await page.getByRole('button',{name:'بررسی مستقل ادعای بازگشتی از PostgreSQL'}).click();
 await d.started;await cancel(page);
 await switchTo(page,b);
 const stale=page.waitForResponse(res=>isCase(res.url(),a.ref));
 d.release();await stale;
 await expect(page.getByTestId('member-care-claim-return-confirmed')).toHaveCount(0);
 const exportTicket=page.getByTestId('member-care-handoff').getByLabel('رسید تحویل دستی هماهنگی عضو');
 expect(JSON.parse(await exportTicket.inputValue()).caseRef).toBe(b.ref);
});
test('CARE191-03 Cancel delayed pinned history, then manually read different Member case; late page cannot resurrect history',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 await open(page,a);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('member-care-server-history')).toBeVisible();
 const historyPath=api+a.ref+'/history?afterRevision=0&limit=4&expectedCurrentRevision=2&claimType=ALL';
 const sourcePage=await (await request.get(historyPath)).json();
 const d=defer();
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  d.arrived();await d.hold;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(sourcePage)});
 });
 await page.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه'}).click();
 await d.started;await cancel(page);
 await expect(page.getByTestId('member-care-server-history')).toHaveCount(0);
 await switchTo(page,b);
 const stale=page.waitForResponse(res=>res.url().includes('/history?'));
 d.release();await stale;
 await expect(page.getByTestId('member-care-server-history-page')).toHaveCount(0);
 const newTicket=page.getByTestId('member-care-handoff').getByLabel('رسید تحویل دستی هماهنگی عضو');
 expect(JSON.parse(await newTicket.inputValue()).caseRef).toBe(b.ref);
});
test('CARE191-04 Cancel delayed failing source GET; old error must not replace successful new case result',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 await open(page,a);
 const d=defer();
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(!isCase(route.request().url(),a.ref))return route.continue();
  d.arrived();await d.hold;
  await route.fulfill({status:503,contentType:'application/json',body:'{}'});
 });
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await d.started;await cancel(page);
 await switchTo(page,b);
 const previousStatus=await page.getByTestId('care-status').innerText();
 const oldFailure=page.waitForResponse(res=>isCase(res.url(),a.ref));
 d.release();await oldFailure;
 await expect(page.getByTestId('care-status')).toHaveText(previousStatus);
 expect(JSON.parse(await page.getByTestId('member-care-handoff')
  .getByLabel('رسید تحویل دستی هماهنگی عضو').inputValue()).caseRef).toBe(b.ref);
});
test('CARE191-05 Lost POST ACK never offers GET cancellation, maintains original UUID and manual recovery',async({page,request})=>{
 const a=await seed(request,false);
 await open(page,a);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('care-guided-counts')).toContainText('نسخه ژورنال: 1');
 let browserPosts=0;
 await page.route('**/internal/demo/v1/care-coordination/*/commands',async route=>{
  browserPosts++;
  const result=await route.fetch();expect(result.status()).toBe(200);
  await route.abort();
 });
 await page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByTestId('member-care-read-cancel')).toHaveCount(0);
 const receipt=await page.getByTestId('care-recovery-export').inputValue();
 const id=JSON.parse(JSON.parse(receipt).body).requestId;
 const db=await (await request.get(api+a.ref)).json();
 expect(db.events[1].requestId).toBe(id);
 expect(db.revision).toBe(2);expect(db.nationalReleaseAllowed).toBe(false);
 expect(browserPosts).toBe(1);
});
