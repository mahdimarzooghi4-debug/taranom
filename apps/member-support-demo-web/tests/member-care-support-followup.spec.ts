import {test,expect} from '@playwright/test';
const care='/internal/demo/v1/care-coordination/';
const support='/internal/demo/v1/support/';
const ZERO='00000000-0000-0000-0000-000000000000';
const unique=(s:string)=>'test:196-'+s+'-'+crypto.randomUUID().replaceAll('-','');
async function seed(request:import('@playwright/test').APIRequestContext,correct=true){
 const careRef=unique('care'),supportRef=unique('support'),memberRef=unique('member');
 const careRoot=crypto.randomUUID(),supportRoot=crypto.randomUUID();
 const root={requestId:careRoot,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef,
  originalMemberRequestRef:unique('claim'),originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(care+careRef+'/commands',{
  data:root,headers:{Origin:'http://127.0.0.1:4320'}})).status()).toBe(200);
 const first={requestId:supportRoot,expectedRevision:0,action:'SUBMIT_UNVERIFIED',
  memberRef:correct?memberRef:unique('wrong-member'),topic:'MEDICAL_SERVICE_COMPLAINT',
  unverifiedEvidenceRef:careRef,claimedSubmitterRef:unique('submitter'),
  originalSubmissionRequestId:ZERO,claimedFollowUpActorRef:null,
  unverifiedFollowUpNoteRef:null};
 expect((await request.post(support+supportRef+'/commands',{data:first})).status()).toBe(200);
 const centerNote=unique('center-note');
 const center={...first,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'FOLLOW_UP_UNVERIFIED',originalSubmissionRequestId:supportRoot,
  claimedFollowUpActorRef:unique('center'),unverifiedFollowUpNoteRef:centerNote};
 expect((await request.post(support+supportRef+'/commands',{data:center})).status()).toBe(200);
 return {careRef,supportRef,memberRef,careRoot,supportRoot,root,first,center,centerNote};
}
async function open(page:import('@playwright/test').Page,s:Awaited<ReturnType<typeof seed>>){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.careRef);
 await page.getByLabel('شناسه آزمایشی عضو').fill(s.memberRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
 const w=page.getByTestId('member-care-support-followup');
 await w.getByRole('textbox',{name:'شناسه پشتیبانی برای پیگیری عضو'}).fill(s.supportRef);
 return w;
}
const inspect=async(w:import('@playwright/test').Locator)=>
 w.getByRole('button',{name:'بازخوانی مستقل هماهنگی و آخرین پیگیری مرکز'}).click();
const submit=async(w:import('@playwright/test').Locator)=>
 w.getByRole('button',{name:'ثبت دستی پیگیری ادعایی عضو با بررسی دوباره دو منبع'}).click();

test('SUP196-01 Member independently sees Center note and records a separate synthetic reply',async({page,request})=>{
 const s=await seed(request),w=await open(page,s);
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toContainText(s.centerNote);
 await submit(w);
 await expect(w.getByTestId('member-care-support-followup-status'))
  .toContainText('UUID اصلی در PostgreSQL تأیید شد');
 const actual=await (await request.get(support+s.supportRef)).json();
 expect(actual.revision).toBe(3);
 expect(actual.followUpCount).toBe(2);
 expect(actual.followUps[0].requestId).toBe(s.center.requestId);
 expect(actual.followUps[1].claimedActorRef).toBe('test:claimed-member-followup');
 expect(actual.followUps[1].noteRef).toBe('test:unverified-member-reply');
 expect(actual.complaintLegallySubmitted).toBe(false);
 expect(actual.clinicalReviewCompleted).toBe(false);
 expect((await (await request.get(care+s.careRef)).json()).revision).toBe(1);
});
test('SUP196-02 mismatched claimed member between the journals blocks all POST',async({page,request})=>{
 const s=await seed(request,false),w=await open(page,s);
 let posts=0;page.on('request',q=>{if(q.method()==='POST'&&q.url().includes('/support/'))posts++;});
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toHaveCount(0);
 await expect(w.getByRole('button',{name:'ثبت دستی پیگیری ادعایی عضو با بررسی دوباره دو منبع'})).toBeDisabled();
 expect(posts).toBe(0);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(2);
});
test('SUP196-03 stale care source after inspection blocks Member follow-up before POST',async({page,request})=>{
 const s=await seed(request),w=await open(page,s);
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toBeVisible();
 const additional={...s.root,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
  originalSubmissionRequestId:s.careRoot,claimedActorRef:unique('actor'),
  unverifiedNoteOrDocumentRef:unique('note')};
 expect((await request.post(care+s.careRef+'/commands',{
  data:additional,headers:{Origin:'http://127.0.0.1:4320'}})).status()).toBe(200);
 let posts=0;page.on('request',q=>{if(q.method()==='POST'&&q.url().includes('/support/'))posts++;});
 await submit(w);
 await expect(w.getByTestId('member-care-support-followup-status')).toContainText('هیچ POST جدیدی ارسال نشد');
 expect(posts).toBe(0);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(2);
});
test('SUP196-04 stale support source after inspection blocks Member follow-up before POST',async({page,request})=>{
 const s=await seed(request),w=await open(page,s);
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toBeVisible();
 const competing={...s.center,requestId:crypto.randomUUID(),expectedRevision:2,
  claimedFollowUpActorRef:unique('other-actor'),unverifiedFollowUpNoteRef:unique('other-note')};
 expect((await request.post(support+s.supportRef+'/commands',{data:competing})).status()).toBe(200);
 let posts=0;page.on('request',q=>{if(q.method()==='POST'&&q.url().includes('/support/'))posts++;});
 await submit(w);
 await expect(w.getByTestId('member-care-support-followup-status')).toContainText('هیچ POST جدیدی ارسال نشد');
 expect(posts).toBe(0);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(3);
});
test('SUP196-05 lost committed ACK is recovered by manual GET and zero subsequent POST',async({page,request})=>{
 const s=await seed(request),w=await open(page,s);
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toBeVisible();
 let posts=0,uuid='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;uuid=route.request().postDataJSON().requestId;
  const real=await route.fetch();expect(real.status()).toBe(200);
  await route.abort();
 });
 await submit(w);
 await expect(w.getByTestId('member-care-support-uncertain')).toBeVisible();
 await w.getByRole('button',{name:'بررسی فقط‌خواندنی UUID پیگیری عضو'}).click();
 await expect(w.getByTestId('member-care-support-uncertain')).toHaveCount(0);
 await expect(w.getByTestId('member-care-support-followup-status')).toContainText('هیچ POST جدیدی');
 expect(posts).toBe(1);
 const source=await (await request.get(support+s.supportRef)).json();
 expect(source.revision).toBe(3);
 expect(source.followUps[1].requestId).toBe(uuid);
});
test('SUP196-06 forged plausible 200 without DB write preserves exact pending UUID and receipt',async({page,request})=>{
 const s=await seed(request),w=await open(page,s);
 await inspect(w);
 await expect(w.getByTestId('member-care-support-matched')).toBeVisible();
 let posts=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;const c=route.request().postDataJSON();
  const real=await (await request.get(support+s.supportRef)).json();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   ...real,outcome:'RecordedUnverified',revision:3,followUpCount:2,
   followUps:[...real.followUps,{revision:3,requestId:c.requestId,
    noteRef:c.unverifiedFollowUpNoteRef,claimedActorRef:c.claimedFollowUpActorRef}]
  })});
 });
 await submit(w);
 await expect(w.getByTestId('member-care-support-uncertain')).toBeVisible();
 const receipt=await w.getByRole('textbox',{name:'رسید پیگیری نامطمئن عضو'}).inputValue();
 await w.getByRole('button',{name:'بررسی فقط‌خواندنی UUID پیگیری عضو'}).click();
 await expect(w.getByTestId('member-care-support-uncertain')).toBeVisible();
 expect(await w.getByRole('textbox',{name:'رسید پیگیری نامطمئن عضو'}).inputValue()).toBe(receipt);
 expect(posts).toBe(1);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(2);
});
