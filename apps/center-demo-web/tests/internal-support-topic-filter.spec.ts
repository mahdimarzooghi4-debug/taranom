import {expect,test} from '@playwright/test';
// Topic filtering remains an anonymous CI-only test-source capability.
// Never a medical triage, real member directory, or insurer determination.
type Topic='MEDICAL_SERVICE_COMPLAINT'|'INSURANCE_COVERAGE_DISPUTE';
const root='/internal/demo/v1/support/';
async function seed(request:import('@playwright/test').APIRequestContext,
 caseRef:string,topic:Topic){
 const r=await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:{
  requestId:crypto.randomUUID(),expectedRevision:0,action:'SUBMIT_UNVERIFIED',
  memberRef:'test:topic172-member',topic,
  unverifiedEvidenceRef:'test:topic172-unverified',
  claimedSubmitterRef:'test:topic172-claimed-actor',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null
 }});
 expect(r.status()).toBe(200);
 expect((await r.json()).nationalReleaseAllowed).toBe(false);
}
async function fixture(request:import('@playwright/test').APIRequestContext){
 const prefix='test:topic172-'+crypto.randomUUID().replaceAll('-','')+'-';
 const medical:string[]=[],insurance:string[]=[];
 for(let i=0;i<6;i++){
  const a=prefix+i.toString().padStart(2,'0')+'a';
  const b=prefix+i.toString().padStart(2,'0')+'b';
  await seed(request,a,'MEDICAL_SERVICE_COMPLAINT');
  await seed(request,b,'INSURANCE_COVERAGE_DISPUTE');
  medical.push(a);insurance.push(b);
 }
 return {prefix,medical,insurance};
}
test('TOPIC172-01 center topic filter reads only exact matching canonical PostgreSQL roots',async({page,request})=>{
 const s=await fixture(request);
 await page.goto('/');
 const panel=page.getByTestId('internal-support-worklist');
 await panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill(s.prefix);
 await panel.getByLabel('فیلتر موضوع پرونده پشتیبانی').selectOption('MedicalServiceComplaint');
 // Topic change intentionally resets the cursor to prevent mismatched pagination.
 await expect(panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'})).toHaveValue('');
 await panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill(s.prefix);
 await panel.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(panel.getByRole('status')).toContainText('تأیید و خوانده شد');
 const rows=panel.getByTestId('support-worklist-rows');
 await expect(rows.locator('li')).toHaveCount(4);
 for(const v of s.medical.slice(0,4))await expect(rows).toContainText(v);
 for(const v of s.insurance)await expect(rows).not.toContainText(v);
 await panel.getByRole('button',{name:'صفحه بعد پرونده‌ها'}).click();
 await expect(rows.locator('li')).toHaveCount(2);
 for(const v of s.medical.slice(4))await expect(rows).toContainText(v);
 for(const v of s.insurance)await expect(rows).not.toContainText(v);
 const chosen=s.medical[5];
 await rows.locator('li').filter({hasText:chosen})
  .getByRole('button',{name:'انتخاب این پرونده'}).click();
 const center=page.getByTestId('internal-support-read');
 await expect(center.getByLabel('شناسه پرونده پیگیری آزمایشی')).toHaveValue(chosen);
 await center.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(center.getByTestId('support-read-result')).toContainText(chosen);
 await expect(center.getByTestId('support-read-result')).toContainText('MedicalServiceComplaint');
 await expect(center).toContainText('انتشار ملی ممنوع');
});
test('TOPIC172-02 changing topic prevents cross-topic cursor and shows the other source subset',async({page,request})=>{
 const s=await fixture(request);
 await page.goto('/');
 const panel=page.getByTestId('internal-support-worklist');
 const filter=panel.getByLabel('فیلتر موضوع پرونده پشتیبانی');
 await filter.selectOption('MedicalServiceComplaint');
 await panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill(s.prefix);
 await panel.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(panel.getByTestId('support-worklist-rows')).toContainText(s.medical[0]);
 await filter.selectOption('InsuranceCoverageDispute');
 await expect(panel.getByTestId('support-worklist-rows')).toHaveCount(0);
 await expect(panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'})).toHaveValue('');
 await panel.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill(s.prefix);
 await panel.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(panel.getByTestId('support-worklist-rows')).toContainText(s.insurance[0]);
 for(const v of s.medical)await expect(panel.getByTestId('support-worklist-rows')).not.toContainText(v);
 const direct=await request.get('/internal/demo/v1/support-worklist?afterCaseRef='+
  encodeURIComponent(s.prefix)+'&limit=4&topic=InsuranceCoverageDispute');
 expect(direct.status()).toBe(200);
 const json=await direct.json();
 expect(json.topic).toBe('InsuranceCoverageDispute');
 expect(json.cases.map((c:{caseRef:string})=>c.caseRef)).toEqual(s.insurance.slice(0,4));
 expect(json.hasMore).toBe(true);
 expect(json.nextAfterCaseRef).toBe(s.insurance[3]);
 expect(json.memberAuthenticated).toBe(false);
 expect(json.insurerContacted).toBe(false);
 expect(json.nationalReleaseAllowed).toBe(false);
});
test('TOPIC172-03 invalid, duplicated or forged topic cannot show source rows',async({page,request})=>{
 const invalid=[
 '/internal/demo/v1/support-worklist?afterCaseRef=&limit=4&topic=clinical-priority',
 '/internal/demo/v1/support-worklist?afterCaseRef=&limit=4&topic=ALL&topic=InsuranceCoverageDispute',
 '/internal/demo/v1/support-worklist?afterCaseRef=&limit=4&topic=MedicalServiceComplaint&scopeRef=test:other',
 ];
 for(const u of invalid){
  const response=await request.get(u,{failOnStatusCode:false});
  expect(response.status()).toBe(400);
  expect((await response.json()).nationalReleaseAllowed).toBe(false);
 }
 await page.route('**/internal/demo/v1/support-worklist?*',route=>{
  const url=new URL(route.request().url());
  return route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({
    contract:'TRN-TECH-129',source:'VERIFIED_ISOLATED_POSTGRES_SUPPORT_JOURNAL',
    syntheticOnly:true,memberAuthenticated:false,complaintLegallySubmitted:false,
    insurerContacted:false,clinicalReviewCompleted:false,nationalReleaseAllowed:false,
    afterCaseRef:url.searchParams.get('afterCaseRef'),topic:'MedicalServiceComplaint',
    limit:4,hasMore:false,nextAfterCaseRef:null,cases:[{
     caseRef:'test:forged-insurance',revision:1,followUpCount:0
    }]
   })});
 });
 await page.goto('/');
 const panel=page.getByTestId('internal-support-worklist');
 await panel.getByLabel('فیلتر موضوع پرونده پشتیبانی').selectOption('InsuranceCoverageDispute');
 await panel.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(panel.getByRole('status')).toContainText('قابل تأیید نیست');
 await expect(panel.getByTestId('support-worklist-rows')).toHaveCount(0);
});
