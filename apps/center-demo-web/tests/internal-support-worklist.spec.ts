import {expect,test} from '@playwright/test';

const ref=()=>('test:worklistui-'+crypto.randomUUID().replaceAll('-',''));
async function createCase(request:import('@playwright/test').APIRequestContext,id:string){
 const result=await request.post('/internal/demo/v1/support/'+id+'/commands',{
  data:{requestId:crypto.randomUUID(),expectedRevision:0,action:'SUBMIT_UNVERIFIED',
   memberRef:'test:private-member',topic:'MEDICAL_SERVICE_COMPLAINT',
   unverifiedEvidenceRef:'test:private-evidence',
   claimedSubmitterRef:'test:private-submitter',
   originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
   claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null}
 });
 expect(result.status()).toBe(200);
}
test('WORKLIST-120-01 source-backed worklist selects a real PostgreSQL test case',async({page,request})=>{
 const marker=ref();const a=marker+'-a',b=marker+'-b';
 await createCase(request,a);await createCase(request,b);
 await page.goto('/');
 const root=page.getByTestId('internal-support-read');
 const list=root.getByTestId('internal-support-worklist');
 await list.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill(marker);
 await list.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(list.getByRole('status')).toContainText('تأیید و خوانده شد');
 await expect(list.getByTestId('support-worklist-rows').locator('li')).toHaveCount(2);
 await expect(list.getByTestId('support-worklist-rows')).not.toContainText('test:private-member');
 await expect(list.getByTestId('support-worklist-rows')).not.toContainText('test:private-evidence');
 await list.getByTestId('support-worklist-rows').locator('li').filter({hasText:b})
  .getByRole('button',{name:'انتخاب این پرونده'}).click();
 await expect(root.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'})).toHaveValue(b);
 await root.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(root.getByTestId('support-read-result')).toContainText(b);
 await expect(root.getByTestId('support-read-result')).toContainText('نسخه 1');
});
test('WORKLIST-120-02 forged source/authorization flags deny entire list',async({page})=>{
 await page.route('**/internal/demo/v1/support-worklist?*',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-129',source:'FAKE_SOURCE',syntheticOnly:true,
   memberAuthenticated:true,complaintLegallySubmitted:false,
   insurerContacted:false,clinicalReviewCompleted:false,
   nationalReleaseAllowed:false,afterCaseRef:'',limit:4,
   hasMore:false,nextAfterCaseRef:null,cases:[
    {caseRef:'test:forged',revision:1,followUpCount:0}]
  })}));
 await page.goto('/');
 const list=page.getByTestId('internal-support-worklist');
 await list.getByRole('button',{name:'خواندن فهرست داخلی'}).click();
 await expect(list.getByRole('status')).toContainText('قابل تأیید نیست');
 await expect(list.getByTestId('support-worklist-rows')).toHaveCount(0);
});
test('WORKLIST-120-03 invalid cursor cannot issue HTTP',async({page})=>{
 let sent=0;
 page.on('request',r=>{if(r.url().includes('/support-worklist?'))sent++;});
 await page.goto('/');
 const list=page.getByTestId('internal-support-worklist');
 await list.getByRole('textbox',{name:'شروع فهرست پس از پرونده'}).fill('member:private');
 await expect(list.getByRole('button',{name:'خواندن فهرست داخلی'})).toBeDisabled();
 expect(sent).toBe(0);
});
