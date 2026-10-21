import {expect,test} from '@playwright/test';
const api='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
async function seed(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:center-delta181-'+crypto.randomUUID().replaceAll('-','');
 const root={requestId:crypto.randomUUID(),expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:member181',
  originalMemberRequestRef:'test:root181',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(api+caseRef+'/commands',{data:root,headers:origin})).status()).toBe(200);
 return {caseRef,root};
}
async function center(page:import('@playwright/test').Page,ref:string){
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 return panel;
}
test('CARE181-C1 Member clinical-source update is manually reattested on Center; no POST on delta read',async({page,request,context})=>{
 const {caseRef}=await seed(request),panel=await center(page,caseRef);
 const member=await context.newPage();
 await member.goto('/member-demo/coordination');
 await member.getByLabel('شناسه آزمایشی پرونده').fill(caseRef);
 await member.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await member.getByLabel('مرجع منبع بالینی ادعایی، تأییدنشده').fill('test:from-member181');
 await member.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await member.getByRole('button',{name:'پیوست ارجاع به منبع بالینیِ تأییدنشده'}).click();
 await expect(member.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 2');
 let posts=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))posts++;});
 await panel.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی مرکز'}).click();
 const delta=panel.getByTestId('center-care-activity');
 await expect(delta).toContainText('1 ادعای جدید');
 await expect(delta).toContainText('AttachUnverifiedClinicalSourceReference');
 await expect(delta).toContainText('test:from-member181');
 expect(posts).toBe(0);
 await panel.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی مرکز'}).click();
 await expect(delta).toContainText('0 ادعای جدید');
 const data=await (await request.get(api+caseRef)).json();
 expect(data.revision).toBe(2);expect(data.clinicalSourceVerified).toBe(false);
 expect(data.nationalReleaseAllowed).toBe(false);
 await member.close();
});
test('CARE181-C2 rollback of previously sourced revision refused without generating a write',async({page,request})=>{
 const {caseRef,root}=await seed(request);
 const previous=await (await request.get(api+caseRef)).json();
 const panel=await center(page,caseRef);
 expect((await request.post(api+caseRef+'/commands',{headers:origin,data:{
  ...root,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
  originalSubmissionRequestId:root.requestId,
  claimedActorRef:'test:coordinator181',unverifiedNoteOrDocumentRef:'test:contact181'
 }})).status()).toBe(200);
 await panel.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی مرکز'}).click();
 await expect(panel.getByTestId('center-care-activity')).toContainText('1 ادعای جدید');
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(previous)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await panel.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی مرکز'}).click();
 await expect(panel.getByTestId('center-care-source')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-activity')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-status')).toContainText('نامعتبر');
 expect(posts).toBe(0);
 const actual=await (await request.get(api+caseRef)).json();
 expect(actual.revision).toBe(2);expect(actual.realCoordinatorVerified).toBe(false);
});
