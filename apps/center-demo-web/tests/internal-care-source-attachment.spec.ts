import {expect,test} from '@playwright/test';
// Sprint 178: non-authorizing source-reference claim via the SAME canonical
// care journal. Not a clinical-document upload, verified source or referral.
const root='/internal/demo/v1/care-coordination/';
const originHeader={Origin:'http://127.0.0.1:4320'};
async function seed(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:care178-'+crypto.randomUUID().replaceAll('-','');
 const requestId=crypto.randomUUID(),memberRef='test:care178-member';
 const cmd={requestId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef,
  originalMemberRequestRef:'test:care178-root',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 const res=await request.post(root+encodeURIComponent(caseRef)+'/commands',
  {data:cmd,headers:originHeader});
 expect(res.status()).toBe(200);
 return {caseRef,memberRef,requestId,cmd};
}
async function center(page:import('@playwright/test').Page,caseRef:string){
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(caseRef);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 return panel;
}
const button='ثبت مستقل مرجع بالینی تأییدنشده';
test('SOURCE178-01 center records a source CLAIM separate from contact and member reads same source',async({page,request})=>{
 const s=await seed(request),ui=await center(page,s.caseRef);
 const source='test:original-external-claim178';
 await ui.getByLabel('مرجع بالینی تأییدنشده آزمایشی').fill(source);
 await ui.getByLabel('مرجع هماهنگ‌کننده آزمایشی').fill('test:claiming-actor178');
 await ui.getByRole('button',{name:button}).click();
 await expect(ui.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(ui.getByTestId('center-care-source')).toContainText(source);
 await expect(ui.getByTestId('center-care-status')).toContainText('هیچ اعتبار بالینی');
 const read=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(read.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(read.events[1].unverifiedNoteOrDocumentRef).toBe(source);
 expect(read.events[1].claimedActorRef).toBe('test:claiming-actor178');
 expect(read.events[1].originalSubmissionRequestId).toBe(s.requestId);
 for(const key of ['realMemberAuthenticated','realCoordinatorVerified',
  'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
  'referralDelivered','appointmentBooked','treatmentStarted',
  'medicalRecordAccessGranted','emergencyServiceDispatched',
  'consentGranted','walletMutated','nationalReleaseAllowed'])
  expect(read[key],key).toBe(false);
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(s.caseRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const history=page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'});
 await expect(history).toContainText('نسخه پرونده: 2');
 await expect(history).toContainText(source);
});
test('SOURCE178-02 old revision after another independent write never silently attaches source',async({page,request})=>{
 const s=await seed(request),ui=await center(page,s.caseRef);
 const competing=await request.post(root+encodeURIComponent(s.caseRef)+'/commands',
  {headers:originHeader,data:{...s.cmd,requestId:crypto.randomUUID(),
    expectedRevision:1,action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
    originalSubmissionRequestId:s.requestId,
    claimedActorRef:'test:competing-actor',
    unverifiedNoteOrDocumentRef:'test:competing-note'}});
 expect(competing.status()).toBe(200);
 await ui.getByRole('button',{name:button}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(ui.getByTestId('center-care-uncertain')).toContainText('مرجع بالینی تأییدنشده');
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی همان مرجع'}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toBeVisible();
 const read=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(read.events).toHaveLength(2);
 expect(read.events[1].action).toBe('RecordUnverifiedCoordinatorContact');
 expect(read.clinicalSourceVerified).toBe(false);
});
test('SOURCE178-03 forged successful ACK with wrong action cannot clear latch; identical manual replay commits once',async({page,request})=>{
 const s=await seed(request),ui=await center(page,s.caseRef);
 let original='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  original=route.request().postData()??'';
  const data=JSON.parse(original);
  const forged=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
  forged.outcome='RecordedUnverified';forged.revision=2;
  forged.events.push({revision:2,requestId:data.requestId,
   action:'RecordUnverifiedCoordinatorContact',memberRef:data.memberRef,
   originalMemberRequestRef:data.originalMemberRequestRef,
   originalSubmissionRequestId:data.originalSubmissionRequestId,
   claimedActorRef:data.claimedActorRef,
   unverifiedNoteOrDocumentRef:data.unverifiedNoteOrDocumentRef});
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(forged)});
 });
 await ui.getByRole('button',{name:button}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(ui.getByTestId('center-care-status')).toContainText('نامعلوم');
 const initial=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(initial.events).toHaveLength(1);
 await page.unrouteAll({behavior:'wait'});
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی همان مرجع'}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toBeVisible();
 await ui.getByRole('button',{name:'بازپخش دستی دقیق UUID قبلی'}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toHaveCount(0);
 const result=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(result.events).toHaveLength(2);
 expect(result.events[1].requestId).toBe(JSON.parse(original).requestId);
 expect(result.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(result.clinicianAuthorized).toBe(false);
});
test('SOURCE178-04 committed source attachment with lost ACK reconciles exact UUID read-only and zero extra POST',async({page,request})=>{
 const s=await seed(request),ui=await center(page,s.caseRef);
 let original='',accepted=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  original=route.request().postData()??'';accepted++;
  const real=await route.fetch();expect(real.status()).toBe(200);
  await route.abort();
 });
 await ui.getByRole('button',{name:button}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(ui.getByTestId('center-care-status')).toContainText('نامعلوم');
 await page.unrouteAll({behavior:'wait'});
 let extra=0;page.on('request',r=>{
  if(r.method()==='POST'&&r.url().includes('/care-coordination/'))extra++;
 });
 await ui.getByRole('button',{name:'بررسی فقط‌خواندنی همان مرجع'}).click();
 await expect(ui.getByTestId('center-care-uncertain')).toHaveCount(0);
 await expect(ui.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(ui.getByTestId('center-care-status')).toContainText('بدون POST تازه');
 expect(accepted).toBe(1);expect(extra).toBe(0);
 const result=await (await request.get(root+encodeURIComponent(s.caseRef))).json();
 expect(result.events).toHaveLength(2);
 expect(result.events[1].requestId).toBe(JSON.parse(original).requestId);
 expect(result.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
});
