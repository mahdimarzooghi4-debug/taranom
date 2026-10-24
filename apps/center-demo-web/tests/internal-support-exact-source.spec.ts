import {test,expect} from '@playwright/test';
const id=()=> 'test:193-center-'+crypto.randomUUID().replaceAll('-','');
async function open(page:import('@playwright/test').Page,name:string){
 await page.goto('/');
 const p=page.getByTestId('internal-support-read');
 await p.getByRole('textbox',{name:'شناسه پرونده پیگیری آزمایشی'}).fill(name);
 await p.getByRole('button',{name:'خواندن از پایگاه داده محلی'}).click();
 await expect(p.getByTestId('support-read-result')).toContainText('نسخه 0');
 return p;
}
test('SUP193-C1 center plausible forged submit ACK with no commit remains uncertain',async({page,request})=>{
 const name=id(),p=await open(page,name);let posts=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;const x=route.request().postDataJSON();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contract:'TRN-TECH-075',syntheticOnly:true,memberAuthenticated:false,
   complaintLegallySubmitted:false,insurerContacted:false,clinicalReviewCompleted:false,
   nationalReleaseAllowed:false,caseRef:name,revision:1,outcome:'RecordedUnverified',
   topic:'MedicalServiceComplaint',memberRef:x.memberRef,evidenceRef:x.unverifiedEvidenceRef,
   claimedSubmitterRef:x.claimedSubmitterRef,originalRequestId:x.requestId,
   followUpCount:0,followUps:[]})});
 });
 await p.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 const w=p.getByTestId('internal-support-write');
 await expect(w.getByTestId('support-recovery')).toBeVisible();
 await expect(w.getByRole('status')).toContainText('نتیجه نامشخص');
 expect(posts).toBe(1);
 expect((await (await request.get('/internal/demo/v1/support/'+name)).json()).revision).toBe(0);
});
test('SUP193-C2 center lost ACK reconciled via PostgreSQL GET with no replay',async({page,request})=>{
 const name=id(),p=await open(page,name);let posts=0,uuid='';
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;uuid=route.request().postDataJSON().requestId;
  const response=await route.fetch();expect(response.status()).toBe(200);await route.abort();
 });
 await p.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 const w=p.getByTestId('internal-support-write');
 await expect(w.getByTestId('support-recovery')).toBeVisible();
 await w.getByRole('button',{name:'بررسی فقط‌خواندنی همان UUID در PostgreSQL'}).click();
 await expect(p.getByTestId('support-read-result')).toContainText('نسخه 1');
 expect(posts).toBe(1);
 const source=await (await request.get('/internal/demo/v1/support/'+name)).json();
 expect(source.originalRequestId).toBe(uuid);
});
test('SUP193-C3 forged follow-up UUID blocked, GET-only exact recovery succeeds',async({page,request})=>{
 const name=id(),p=await open(page,name);
 await p.getByRole('button',{name:'ثبت اولیه تأییدنشده'}).click();
 await expect(p.getByTestId('support-read-result')).toContainText('نسخه 1');
 let posts=0;
 await page.route('**/internal/demo/v1/support/*/commands',async route=>{
  posts++;const reply=await route.fetch();expect(reply.status()).toBe(200);
  const bogus=await reply.json();bogus.followUps[0].requestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(bogus)});
 });
 await p.getByRole('button',{name:'ثبت پیگیری تأییدنشده'}).click();
 const w=p.getByTestId('internal-support-write');
 await expect(w.getByTestId('support-recovery')).toBeVisible();
 await w.getByRole('button',{name:'بررسی فقط‌خواندنی همان UUID در PostgreSQL'}).click();
 await expect(p.getByTestId('support-read-result')).toContainText('نسخه 2');
 expect(posts).toBe(1);
 expect((await (await request.get('/internal/demo/v1/support/'+name)).json()).followUpCount).toBe(1);
});
