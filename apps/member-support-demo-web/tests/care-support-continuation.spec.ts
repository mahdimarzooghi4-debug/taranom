import {test,expect} from '@playwright/test';
const id=(s:string)=>'test:194-'+s+'-'+crypto.randomUUID().replaceAll('-','');
const care='/internal/demo/v1/care-coordination/',support='/internal/demo/v1/support/';
async function setup(page:import('@playwright/test').Page){
 await page.goto('/member-demo/coordination');
 const ref=await page.getByLabel('شناسه آزمایشی پرونده').inputValue();
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByTestId('care-status')).toContainText('رویداد ادعایی ثبت شد');
 const panel=page.getByTestId('care-support-continuation');
 const target=id('support');
 await panel.getByRole('textbox',{name:'شناسه پشتیبانی برگرفته از هماهنگی'}).fill(target);
 return {ref,target,panel};
}
test('SUP194-01 writes one manual support request with original care reference and no care mutation',async({page,request})=>{
 const {ref,target,panel}=await setup(page);
 const original=await (await request.get(care+ref)).json();
 await panel.getByRole('button',{name:'ثبت دستی پشتیبانی ادعایی از هماهنگی'}).click();
 await expect(panel.getByTestId('care-support-status')).toContainText('UUID اصلی درخواست پشتیبانی در PostgreSQL');
 const supportCase=await (await request.get(support+target)).json();
 expect(supportCase.revision).toBe(1);
 expect(supportCase.topic).toBe('MedicalServiceComplaint');
 expect(supportCase.evidenceRef).toBe(ref);
 expect(supportCase.memberRef).toBe(original.events[0].memberRef);
 expect(supportCase.complaintLegallySubmitted).toBe(false);
 expect(supportCase.insurerContacted).toBe(false);
 expect((await (await request.get(care+ref)).json()).revision).toBe(1);
});
test('SUP194-02 changing source after display fails before a support POST',async({page,request})=>{
 const {ref,target,panel}=await setup(page);
 const source=await (await request.get(care+ref)).json(),first=source.events[0];
 const update={requestId:crypto.randomUUID(),expectedRevision:1,
  action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',memberRef:first.memberRef,
  originalMemberRequestRef:first.originalMemberRequestRef,
  originalSubmissionRequestId:first.requestId,
  claimedActorRef:id('actor'),unverifiedNoteOrDocumentRef:id('note')};
 const response=await request.post(care+ref+'/commands',{
  data:update,headers:{Origin:'http://127.0.0.1:4320'}});
 expect(response.status()).toBe(200);
 let writes=0;
 page.on('request',r=>{if(r.url().includes('/support/')&&r.method()==='POST')writes++;});
 await panel.getByRole('button',{name:'ثبت دستی پشتیبانی ادعایی از هماهنگی'}).click();
 await expect(panel.getByTestId('care-support-status')).toContainText('هیچ POST ارسال نشد');
 expect(writes).toBe(0);
 expect((await (await request.get(support+target)).json()).revision).toBe(0);
});
test('SUP194-03 lost successful POST reply is reconciled by GET, no duplicate write',async({page,request})=>{
 const {target,panel}=await setup(page);
 let count=0,uuid='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  count++;uuid=route.request().postDataJSON().requestId;
  const real=await route.fetch();expect(real.status()).toBe(200);await route.abort();
 });
 await panel.getByRole('button',{name:'ثبت دستی پشتیبانی ادعایی از هماهنگی'}).click();
 await expect(panel.getByTestId('care-support-uncertain')).toBeVisible();
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی UUID پشتیبانی در PostgreSQL'}).click();
 await expect(panel.getByTestId('care-support-uncertain')).toHaveCount(0);
 await expect(panel.getByTestId('care-support-status')).toContainText('بدون POST جدید');
 expect(count).toBe(1);
 const state=await (await request.get(support+target)).json();
 expect(state.revision).toBe(1);expect(state.originalRequestId).toBe(uuid);
});
test('SUP194-04 a forged plausible support ACK without commit cannot release UUID',async({page,request})=>{
 const {ref,target,panel}=await setup(page);
 const original=await (await request.get(care+ref)).json();
 let count=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  count++;const c=route.request().postDataJSON();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-075',syntheticOnly:true,memberAuthenticated:false,
   complaintLegallySubmitted:false,insurerContacted:false,clinicalReviewCompleted:false,
   nationalReleaseAllowed:false,caseRef:target,revision:1,outcome:'RecordedUnverified',
   topic:'MedicalServiceComplaint',memberRef:original.events[0].memberRef,
   evidenceRef:ref,claimedSubmitterRef:c.claimedSubmitterRef,
   originalRequestId:c.requestId,followUpCount:0,followUps:[]})});
 });
 await panel.getByRole('button',{name:'ثبت دستی پشتیبانی ادعایی از هماهنگی'}).click();
 await expect(panel.getByTestId('care-support-uncertain')).toBeVisible();
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی UUID پشتیبانی در PostgreSQL'}).click();
 await expect(panel.getByTestId('care-support-uncertain')).toBeVisible();
 expect(count).toBe(1);
 expect((await (await request.get(support+target)).json()).revision).toBe(0);
});
