import {expect,test} from '@playwright/test';
// The existing localhost .NET/PostgreSQL support journal is the only truth.
// Simulate the member-side write through its unchanged canonical command.
const base='/internal/demo/v1/support/';
async function seed(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:handoff171-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member171-'+crypto.randomUUID().replaceAll('-','');
 const originalRequestId=crypto.randomUUID(),evidenceRef='test:reference171';
 const first={requestId:originalRequestId,expectedRevision:0,
   action:'SUBMIT_UNVERIFIED',memberRef,topic:'MEDICAL_SERVICE_COMPLAINT',
   unverifiedEvidenceRef:evidenceRef,claimedSubmitterRef:'test:member-claimed-actor',
   originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
   claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null};
 const r=await request.post(base+encodeURIComponent(caseRef)+'/commands',{data:first});
 expect(r.status()).toBe(200);
 expect((await r.json()).revision).toBe(1);
 const receipt={format:'TRN-TECH-180-TEST-SUPPORT-HANDOFF',version:1,
   caseRef,memberRef,topic:'MedicalServiceComplaint',evidenceRef,
   originalRequestId,revision:1};
 return {caseRef,memberRef,receipt,first};
}
async function open(page:import('@playwright/test').Page){
 await page.goto('/');
 return page.getByTestId('internal-support-read');
}
test('HANDOFF171-01 independently rehydrate exact member-originated support case, zero new POST',async({page,request})=>{
 const {caseRef,receipt}=await seed(request);
 const panel=await open(page);
 let writes=0;
 page.on('request',r=>{if(r.method()==='POST')writes++;});
 await panel.getByLabel('ورود رسید آزمایشی عضو').fill(JSON.stringify(receipt));
 await panel.getByRole('button',{name:'راستی‌آزمایی دستی رسید از ژورنال'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText(caseRef);
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 1');
 await expect(panel.getByTestId('center-support-handoff-status')).toContainText(
  'نسخه دقیق از PostgreSQL مستقل خوانده شد');
 expect(await panel.getByLabel('شناسه پرونده پیگیری آزمایشی').inputValue()).toBe(caseRef);
 expect(writes).toBe(0);
 await panel.getByRole('button',{name:'بازخوانی تاریخچه پیگیری'}).click();
 await expect(panel.getByTestId('support-history-events').locator('li')).toHaveCount(1);
 await expect(panel).toContainText('انتشار ملی ممنوع');
});
test('HANDOFF171-02 forged wrong member, wrong case or authority never adopted',async({page,request})=>{
 const {receipt}=await seed(request);
 const panel=await open(page);
 const input=panel.getByLabel('ورود رسید آزمایشی عضو');
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 for(const alteration of [
  {...receipt,memberRef:'test:other-member'},
  {...receipt,caseRef:'test:missing-'+crypto.randomUUID().replaceAll('-','')},
  {...receipt,nationalReleaseAllowed:true}
 ]){
  await input.fill(JSON.stringify(alteration));
  await panel.getByRole('button',{name:'راستی‌آزمایی دستی رسید از ژورنال'}).click();
  await expect(panel.getByTestId('center-support-handoff-status')).toContainText('تطبیق نداشت');
  await expect(panel.getByTestId('support-read-result')).toHaveCount(0);
  expect(await panel.getByLabel('شناسه پرونده پیگیری آزمایشی').inputValue())
   .toBe('test:local-support');
 }
 expect(posts).toBe(0);
});
test('HANDOFF171-03 stale version refuses but a freshly pinned receipt can rehydrate',async({page,request})=>{
 const s=await seed(request);
 const follow={...s.first,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'FOLLOW_UP_UNVERIFIED',
  originalSubmissionRequestId:s.first.requestId,
  claimedFollowUpActorRef:'test:follow-actor',unverifiedFollowUpNoteRef:'test:follow-evidence'};
 const accepted=await request.post(base+encodeURIComponent(s.caseRef)+'/commands',{data:follow});
 expect(accepted.status()).toBe(200);
 expect((await accepted.json()).revision).toBe(2);
 const panel=await open(page),input=panel.getByLabel('ورود رسید آزمایشی عضو');
 await input.fill(JSON.stringify(s.receipt));
 await panel.getByRole('button',{name:'راستی‌آزمایی دستی رسید از ژورنال'}).click();
 await expect(panel.getByTestId('support-read-result')).toHaveCount(0);
 await expect(panel.getByTestId('center-support-handoff-status')).toContainText('تطبیق نداشت');
 await input.fill(JSON.stringify({...s.receipt,revision:2}));
 await panel.getByRole('button',{name:'راستی‌آزمایی دستی رسید از ژورنال'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 2');
 await expect(panel.getByTestId('support-read-result')).toContainText('پیگیری‌ها: 1');
});
test('HANDOFF171-04 forged backend read with official authority cannot validate receipt',async({page,request})=>{
 const {receipt}=await seed(request);
 const panel=await open(page);
 await page.route('**/internal/demo/v1/support/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const upstream=await route.fetch();const x=await upstream.json();
  x.memberAuthenticated=true;x.nationalReleaseAllowed=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await panel.getByLabel('ورود رسید آزمایشی عضو').fill(JSON.stringify(receipt));
 await panel.getByRole('button',{name:'راستی‌آزمایی دستی رسید از ژورنال'}).click();
 await expect(panel.getByTestId('support-read-result')).toHaveCount(0);
 await expect(panel.getByTestId('center-support-handoff-status')).toContainText('تطبیق نداشت');
});
