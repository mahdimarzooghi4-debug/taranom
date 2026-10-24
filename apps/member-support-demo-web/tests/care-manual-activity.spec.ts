import {expect,test} from '@playwright/test';
// TRN-TECH-190: only explicitly requested, source-backed additions;
// no notification, real health-data permission or clinical claim.
const endpoint='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
async function seed(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:delta181-'+crypto.randomUUID().replaceAll('-','');
 const initial={requestId:crypto.randomUUID(),expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:delta181-member',
  originalMemberRequestRef:'test:delta181-root',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(endpoint+caseRef+'/commands',{data:initial,headers:origin})).status()).toBe(200);
 return {caseRef,initial};
}
async function loadMember(page:import('@playwright/test').Page,caseRef:string){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(caseRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
}
test('CARE181-M1 external Center contact appears only on Member manual source re-read',async({page,context,request})=>{
 const {caseRef}=await seed(request);
 await loadMember(page,caseRef);
 await expect(page.getByTestId('member-care-activity')).toHaveCount(0);
 const center=await context.newPage();await center.goto('/');
 const panel=center.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(caseRef);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 await panel.getByLabel('مرجع یادداشت تماس آزمایشی').fill('test:member-visible-delta181');
 await panel.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(page.getByTestId('member-care-activity')).toHaveCount(0);
 let newPosts=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))newPosts++;});
 await page.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی عضو'}).click();
 const delta=page.getByTestId('member-care-activity');
 await expect(delta).toContainText('1 رویداد ادعایی');
 await expect(delta).toContainText('test:member-visible-delta181');
 await expect(delta).toContainText('ادعای تماس تأییدنشده');
 expect(newPosts).toBe(0);
 await page.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی عضو'}).click();
 await expect(delta).toContainText('0 رویداد ادعایی');
 const journal=await (await request.get(endpoint+caseRef)).json();
 expect(journal.revision).toBe(2);
 expect(journal.realCoordinatorVerified).toBe(false);
 expect(journal.nationalReleaseAllowed).toBe(false);
 await center.close();
});
test('CARE181-M2 forged replacement of historical root with otherwise self-valid history fails closed',async({page,request})=>{
 const {caseRef,initial}=await seed(request);await loadMember(page,caseRef);
 expect((await request.post(endpoint+caseRef+'/commands',{headers:origin,data:{
  ...initial,requestId:crypto.randomUUID(),expectedRevision:1,
  action:'ATTACH_UNVERIFIED_CLINICAL_SOURCE',
  originalSubmissionRequestId:initial.requestId,
  claimedActorRef:'test:fake-actor',unverifiedNoteOrDocumentRef:'test:clinical-claim'
 }})).status()).toBe(200);
 let gets=0;
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  gets++;
  const reply=await route.fetch(),raw=await reply.json();
  const replacement=crypto.randomUUID();
  raw.events[0].requestId=replacement;
  raw.events[1].originalSubmissionRequestId=replacement;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(raw)});
 });
 let post=0;page.on('request',r=>{if(r.method()==='POST')post++;});
 await page.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی عضو'}).click();
 await expect(page.getByTestId('member-care-activity')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await expect(page.getByTestId('care-status')).toContainText('تأیید نیست');
 expect(gets).toBe(1);expect(post).toBe(0);
 const genuine=await (await request.get(endpoint+caseRef)).json();
 expect(genuine.events[0].requestId).toBe(initial.requestId);
 expect(genuine.nationalReleaseAllowed).toBe(false);
});
