import {expect,test} from '@playwright/test';
const root='/internal/demo/v1/support/';
test('RETURN173-04 Center ticket exports exactly source-backed final unverified note',async({page,request})=>{
 const caseRef='test:center-return173-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:member-return173';
 const rootId=crypto.randomUUID(),followId=crypto.randomUUID();
 const original={requestId:rootId,expectedRevision:0,action:'SUBMIT_UNVERIFIED',
  memberRef,topic:'INSURANCE_COVERAGE_DISPUTE',unverifiedEvidenceRef:'test:evidence-return',
  claimedSubmitterRef:'test:submitter-return',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null};
 expect((await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:original})).status()).toBe(200);
 await page.goto('/');
 const panel=page.getByTestId('internal-support-read');
 await panel.getByLabel('شناسه پرونده پیگیری آزمایشی').fill(caseRef);
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(panel.getByTestId('support-read-result')).toContainText('نسخه 1');
 await expect(panel.getByTestId('center-member-followup-ticket')).toHaveCount(0);
 const follow={...original,requestId:followId,expectedRevision:1,
  action:'FOLLOW_UP_UNVERIFIED',originalSubmissionRequestId:rootId,
  claimedFollowUpActorRef:'test:claimed-center',unverifiedFollowUpNoteRef:'test:center-note'};
 expect((await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:follow})).status()).toBe(200);
 await panel.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 const ticketPanel=panel.getByTestId('center-member-followup-ticket');
 await expect(ticketPanel).toBeVisible();
 const receipt=JSON.parse(await ticketPanel.getByLabel('رسید خروجی آخرین پیگیری مرکز').inputValue());
 expect(Object.keys(receipt).sort()).toEqual([
  'format','version','caseRef','memberRef','topic','evidenceRef',
  'originalRequestId','revision','followUpRequestId','noteRef','claimedActorRef'
 ].sort());
 expect(receipt.format).toBe('TRN-TECH-182-TEST-FOLLOWUP-RETURN');
 expect(receipt.version).toBe(1);expect(receipt.caseRef).toBe(caseRef);
 expect(receipt.memberRef).toBe(memberRef);expect(receipt.revision).toBe(2);
 expect(receipt.followUpRequestId).toBe(followId);
 expect(receipt.noteRef).toBe('test:center-note');
 expect(receipt.topic).toBe('InsuranceCoverageDispute');
 const source=await (await request.get(root+encodeURIComponent(caseRef))).json();
 expect(source.followUps[0].requestId).toBe(receipt.followUpRequestId);
 expect(source.nationalReleaseAllowed).toBe(false);
 expect(source.insurerContacted).toBe(false);
 // Selecting another case invalidates the previous ticket immediately.
 await panel.getByLabel('شناسه پرونده پیگیری آزمایشی').fill('test:other-case');
 await expect(ticketPanel).toHaveCount(0);
});
