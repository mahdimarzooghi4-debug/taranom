import {expect,test} from '@playwright/test';
// TRN-TECH-182: source-only unverified support note, never official reply.
const root='/internal/demo/v1/support/';
async function seed(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:return173-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member-return173';
 const topic='MEDICAL_SERVICE_COMPLAINT';
 const originalRequestId=crypto.randomUUID(),followUpRequestId=crypto.randomUUID();
 const evidenceRef='test:unverified-evidence-return';
 const claimedActorRef='test:center-claimed-followup-actor';
 const noteRef='test:returned-followup-note';
 const original={requestId:originalRequestId,expectedRevision:0,action:'SUBMIT_UNVERIFIED',
  memberRef,topic,unverifiedEvidenceRef:evidenceRef,
  claimedSubmitterRef:'test:unverified-submitter',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null};
 const first=await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:original});
 expect(first.status()).toBe(200);
 const second=await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:{
  ...original,requestId:followUpRequestId,expectedRevision:1,
  action:'FOLLOW_UP_UNVERIFIED',originalSubmissionRequestId:originalRequestId,
  claimedFollowUpActorRef:claimedActorRef,unverifiedFollowUpNoteRef:noteRef
 }});
 expect(second.status()).toBe(200);
 const ticket={format:'TRN-TECH-182-TEST-FOLLOWUP-RETURN',version:1,
  caseRef,memberRef,topic:'MedicalServiceComplaint',evidenceRef,
  originalRequestId,revision:2,followUpRequestId,noteRef,claimedActorRef};
 return {ticket,caseRef,memberRef,noteRef,followUpRequestId};
}
async function member(page:import('@playwright/test').Page,
 s:{caseRef:string;memberRef:string}){
 await page.goto('/member-demo/');
 await page.locator('#sup-case').fill(s.caseRef);
 await page.locator('#sup-member').fill(s.memberRef);
 return page.getByTestId('member-return-followup');
}
test('RETURN173-01 independently confirm original center-followup event without any POST',async({page,request})=>{
 const s=await seed(request);const panel=await member(page,s);
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await panel.getByLabel('ورود رسید پیگیری آزمایشی مرکز').fill(JSON.stringify(s.ticket));
 await panel.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
 await expect(panel.getByTestId('member-return-followup-confirmed')).toContainText(s.noteRef);
 await expect(panel.getByTestId('member-return-followup-confirmed')).toContainText('نسخه 2');
 await expect(panel.getByTestId('member-return-followup-status')).toContainText('هیچ پاسخ رسمی');
 expect(posts).toBe(0);
 await expect(page.getByTestId('member-support-handoff')).toBeVisible();
 const json=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(json.followUps[0].requestId).toBe(s.followUpRequestId);
 expect(json.memberAuthenticated).toBe(false);
 expect(json.complaintLegallySubmitted).toBe(false);
 expect(json.insurerContacted).toBe(false);
 expect(json.nationalReleaseAllowed).toBe(false);
});
test('RETURN173-02 forged note, UUID, member, unknown key and forged authority all fail closed',async({page,request})=>{
 const s=await seed(request);const panel=await member(page,s);
 let writes=0;page.on('request',r=>{if(r.method()==='POST')writes++;});
 const failures=[
  {...s.ticket,noteRef:'test:forged-note'},
  {...s.ticket,followUpRequestId:crypto.randomUUID()},
  {...s.ticket,memberRef:'test:someone-else'},
  {...s.ticket,nationalReleaseAllowed:true},
  {...s.ticket,revision:1}
 ];
 for(const ticket of failures){
  await panel.getByLabel('ورود رسید پیگیری آزمایشی مرکز').fill(JSON.stringify(ticket));
  await panel.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
  await expect(panel.getByTestId('member-return-followup-status')).toContainText('تأیید نشد');
  await expect(panel.getByTestId('member-return-followup-confirmed')).toHaveCount(0);
 }
 expect(writes).toBe(0);
});
test('RETURN173-03 stale receipt and forged source with official authority rejected',async({page,request})=>{
 const s=await seed(request);const panel=await member(page,s);
 const second=await request.post(root+encodeURIComponent(s.caseRef)+'/commands',{data:{
  requestId:crypto.randomUUID(),expectedRevision:2,action:'FOLLOW_UP_UNVERIFIED',
  memberRef:s.memberRef,topic:'MEDICAL_SERVICE_COMPLAINT',
  unverifiedEvidenceRef:s.ticket.evidenceRef,
  claimedSubmitterRef:'test:unverified-submitter',
  originalSubmissionRequestId:s.ticket.originalRequestId,
  claimedFollowUpActorRef:'test:other-claimed-actor',
  unverifiedFollowUpNoteRef:'test:new-note'
 }});
 expect(second.status()).toBe(200);
 await panel.getByLabel('ورود رسید پیگیری آزمایشی مرکز').fill(JSON.stringify(s.ticket));
 await panel.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
 await expect(panel.getByTestId('member-return-followup-status')).toContainText('تأیید نشد');
 const newer={...s.ticket,revision:3,followUpRequestId:(await second.json()).followUps[1].requestId,
  noteRef:'test:new-note',claimedActorRef:'test:other-claimed-actor'};
 await page.route('**/internal/demo/v1/support/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const upstream=await route.fetch();const x=await upstream.json();
  x.memberAuthenticated=true;x.nationalReleaseAllowed=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await panel.getByLabel('ورود رسید پیگیری آزمایشی مرکز').fill(JSON.stringify(newer));
 await panel.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
 await expect(panel.getByTestId('member-return-followup-confirmed')).toHaveCount(0);
 await expect(panel.getByTestId('member-return-followup-status')).toContainText('تأیید نشد');
 await page.unrouteAll({behavior:'wait'});
 await panel.getByRole('button',{name:'بررسی مستقل پیگیری مرکز از PostgreSQL'}).click();
 await expect(panel.getByTestId('member-return-followup-confirmed')).toContainText('test:new-note');
});
