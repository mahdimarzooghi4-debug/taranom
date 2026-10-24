import {test,expect} from '@playwright/test';
const care='/internal/demo/v1/care-coordination/';
const support='/internal/demo/v1/support/';
const unique=(s:string)=>'test:195-'+s+'-'+crypto.randomUUID().replaceAll('-','');
const ZERO='00000000-0000-0000-0000-000000000000';
async function seed(request:import('@playwright/test').APIRequestContext,match=true){
 const careRef=unique('care'),supportRef=unique('support'),member=unique('member');
 const root=crypto.randomUUID(),supportRoot=crypto.randomUUID();
 const careCmd={requestId:root,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:member,
  originalMemberRequestRef:unique('request'),
  originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(care+careRef+'/commands',{
  data:careCmd,headers:{Origin:'http://127.0.0.1:4320'}})).status()).toBe(200);
 const original={requestId:supportRoot,expectedRevision:0,
  action:'SUBMIT_UNVERIFIED',
  memberRef:match?member:unique('forged-member'),
  topic:'MEDICAL_SERVICE_COMPLAINT',
  unverifiedEvidenceRef:careRef,claimedSubmitterRef:unique('submitter'),
  originalSubmissionRequestId:ZERO,claimedFollowUpActorRef:null,
  unverifiedFollowUpNoteRef:null};
 expect((await request.post(support+supportRef+'/commands',{data:original})).status()).toBe(200);
 return {careRef,supportRef,member,root,careCmd,original};
}
async function open(page:import('@playwright/test').Page,ref:string){
 await page.goto('/');
 const p=page.getByTestId('internal-support-read');
 await p.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill(ref);
 await p.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(p.getByTestId('support-read-result')).toContainText('نسخه 1');
 return p.getByTestId('center-care-support-followup');
}
test('SUP195-01 Center human-inspects both sources and records one unverified follow-up, member can read return',async({page,request})=>{
 const s=await seed(request),panel=await open(page,s.supportRef);
 await expect(panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'})).toBeDisabled();
 await panel.getByRole('button',{name:'تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی'}).click();
 await expect(panel.getByTestId('center-care-support-matched')).toContainText('نسخه 1');
 await panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'}).click();
 const supportView=page.getByTestId('internal-support-read');
 await expect(supportView.getByTestId('support-read-result')).toContainText('نسخه 2');
 const ticket=supportView.getByTestId('center-member-followup-ticket');
 await expect(ticket).toBeVisible();
 const receipt=await ticket.getByLabel('رسید خروجی آخرین پیگیری مرکز').inputValue();
 const json=JSON.parse(receipt);
 expect(json.caseRef).toBe(s.supportRef);
 const saved=await (await request.get(support+s.supportRef)).json();
 expect(saved.revision).toBe(2);
 expect(saved.followUps[0].requestId).toBe(json.followUpRequestId);
 expect(saved.complaintLegallySubmitted).toBe(false);
 expect((await (await request.get(care+s.careRef)).json()).revision).toBe(1);
 // Existing Member workflow receives and independently verifies the return.
 await page.goto('/member-demo/');
 await page.getByLabel('پرونده آزمایشی').fill(s.supportRef);
 await page.getByLabel('شناسه عضو فرضی').fill(s.member);
 await page.getByLabel('ورود رسید پیگیری آزمایشی مرکز').fill(receipt);
 await page.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
 await expect(page.getByTestId('member-return-followup-confirmed')).toBeVisible();
});
test('SUP195-02 claimed Member differs between sources: no follow-up ever enabled',async({page,request})=>{
 const s=await seed(request,false),panel=await open(page,s.supportRef);
 let posts=0;page.on('request',x=>{if(x.method()==='POST'&&x.url().includes('/support/'))posts++;});
 await panel.getByRole('button',{name:'تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی'}).click();
 await expect(panel.getByTestId('center-care-support-matched')).toHaveCount(0);
 await expect(panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'})).toBeDisabled();
 expect(posts).toBe(0);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(1);
});
test('SUP195-03 stale care journal after inspection blocks POST before mutation',async({page,request})=>{
 const s=await seed(request),panel=await open(page,s.supportRef);
 await panel.getByRole('button',{name:'تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی'}).click();
 await expect(panel.getByTestId('center-care-support-matched')).toBeVisible();
 const follow={...s.careCmd,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
  originalSubmissionRequestId:s.root,claimedActorRef:unique('actor'),
  unverifiedNoteOrDocumentRef:unique('note')};
 expect((await request.post(care+s.careRef+'/commands',{data:follow,
  headers:{Origin:'http://127.0.0.1:4320'}})).status()).toBe(200);
 let posted=0;page.on('request',x=>{if(x.method()==='POST'&&x.url().includes('/support/'))posted++;});
 await panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'}).click();
 await expect(panel.getByTestId('center-care-support-status')).toContainText('هیچ پیگیری جدیدی ارسال نشد');
 expect(posted).toBe(0);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(1);
});
test('SUP195-04 lost accepted ACK reconciles exact pending UUID via GET only',async({page,request})=>{
 const s=await seed(request),panel=await open(page,s.supportRef);
 await panel.getByRole('button',{name:'تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی'}).click();
 await expect(panel.getByTestId('center-care-support-matched')).toBeVisible();
 let count=0,uuid='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  count++;uuid=route.request().postDataJSON().requestId;
  const actual=await route.fetch();expect(actual.status()).toBe(200);await route.abort();
 });
 await panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'}).click();
 await expect(panel.getByTestId('center-care-support-uncertain')).toBeVisible();
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی UUID اصلی پیگیری'}).click();
 await expect(page.getByTestId('internal-support-read').getByTestId('support-read-result')).toContainText('نسخه 2');
 expect(count).toBe(1);
 const actual=await (await request.get(support+s.supportRef)).json();
 expect(actual.followUps[0].requestId).toBe(uuid);
});
test('SUP195-05 plausible forged ACK without a committed event preserves same original UUID',async({page,request})=>{
 const s=await seed(request),panel=await open(page,s.supportRef);
 await panel.getByRole('button',{name:'تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی'}).click();
 await expect(panel.getByTestId('center-care-support-matched')).toBeVisible();
 let count=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  count++;const c=route.request().postDataJSON();
  const v=await (await request.get(support+s.supportRef)).json();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   ...v,outcome:'RecordedUnverified',revision:2,followUpCount:1,
   followUps:[{revision:2,requestId:c.requestId,noteRef:c.unverifiedFollowUpNoteRef,
    claimedActorRef:c.claimedFollowUpActorRef}]})});
 });
 await panel.getByRole('button',{name:'ثبت دستی پیگیری تأییدنشده با بررسی دو منبع'}).click();
 await expect(panel.getByTestId('center-care-support-uncertain')).toBeVisible();
 const receipt=await panel.getByLabel('رسید بازیابی پیگیری مرکز').inputValue();
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی UUID اصلی پیگیری'}).click();
 await expect(panel.getByTestId('center-care-support-uncertain')).toBeVisible();
 expect(await panel.getByLabel('رسید بازیابی پیگیری مرکز').inputValue()).toBe(receipt);
 expect(count).toBe(1);
 expect((await (await request.get(support+s.supportRef)).json()).revision).toBe(1);
});
