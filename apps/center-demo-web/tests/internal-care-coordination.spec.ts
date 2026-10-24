import {expect,test} from '@playwright/test';
// Existing TRN-TECH-115 loopback Kestrel + independent PostgreSQL journal.
const root='/internal/demo/v1/care-coordination/';
const Origin={Origin:'http://127.0.0.1:4320'};
const ZERO='00000000-0000-0000-0000-000000000000';
async function origin(request:import('@playwright/test').APIRequestContext){
 const caseRef='test:care175-'+crypto.randomUUID().replaceAll('-','');
 const memberRef='test:care175-member';
 const originRef='test:care175-member-request';
 const requestId=crypto.randomUUID();
 const cmd={requestId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',
  memberRef,originalMemberRequestRef:originRef,
  originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 const res=await request.post(root+encodeURIComponent(caseRef)+'/commands',
  {data:cmd,headers:Origin});
 expect(res.status()).toBe(200);
 expect((await res.json()).revision).toBe(1);
 return {caseRef,memberRef,originRef,requestId};
}
async function center(page:import('@playwright/test').Page,ref:string){
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 return panel;
}
test('CARE175-01 center manually adds actual audited claim, member sees same source without referral',async({page,request})=>{
 const input=await origin(request),panel=await center(page,input.caseRef);
 await panel.getByLabel('مرجع هماهنگ‌کننده آزمایشی').fill('test:care175-coordinator');
 await panel.getByLabel('مرجع یادداشت تماس آزمایشی').fill('test:care175-contact');
 await panel.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(panel.getByTestId('center-care-source')).toContainText('test:care175-contact');
 await expect(panel.getByTestId('center-care-status')).toContainText('جداگانه تأیید شد');
 const saved=await (await request.get(root+encodeURIComponent(input.caseRef))).json();
 expect(saved.revision).toBe(2);
 expect(saved.events[1].action).toBe('RecordUnverifiedCoordinatorContact');
 expect(saved.events[1].originalSubmissionRequestId).toBe(input.requestId);
 expect(saved.events[1].unverifiedNoteOrDocumentRef).toBe('test:care175-contact');
 for(const k of ['realMemberAuthenticated','realCoordinatorVerified','clinicalSourceVerified',
 'clinicianAuthorized','referralOrdered','referralDelivered','appointmentBooked',
 'treatmentStarted','medicalRecordAccessGranted','emergencyServiceDispatched',
 'consentGranted','walletMutated','nationalReleaseAllowed'])expect(saved[k],k).toBe(false);
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(input.caseRef);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const history=page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'});
 await expect(history).toContainText('نسخه پرونده: 2');
 await expect(history).toContainText('test:care175-contact');
});
test('CARE175-02 committed lost HTTP ACK recovers via source-only GET without any additional POST',async({page,request})=>{
 const input=await origin(request),panel=await center(page,input.caseRef);
 let sent='',posts=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  sent=route.request().postData()??'';posts++;
  const accepted=await route.fetch();expect(accepted.status()).toBe(200);
  await route.abort();
 });
 await panel.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(panel.getByTestId('center-care-status')).toContainText('نتیجه ثبت نامعلوم');
 await page.unrouteAll({behavior:'wait'});
 let nextPosts=0;
 page.on('request',r=>{if(r.method()==='POST'&&
   r.url().includes('/care-coordination/'))nextPosts++;});
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی همان تماس'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(panel.getByTestId('center-care-status')).toContainText('بدون POST تازه');
 expect(posts).toBe(1);expect(nextPosts).toBe(0);
 const journal=await (await request.get(root+encodeURIComponent(input.caseRef))).json();
 expect(journal.events).toHaveLength(2);
 expect(journal.events[1].requestId).toBe(JSON.parse(sent).requestId);
 expect(journal.nationalReleaseAllowed).toBe(false);
});
test('CARE175-03 forged successful POST with no persisted event retains exact pending UUID until manual replay',async({page,request})=>{
 const input=await origin(request),panel=await center(page,input.caseRef);
 let savedOriginal='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  savedOriginal=route.request().postData()??'';
  const original=await (await request.get(root+encodeURIComponent(input.caseRef))).json();
  original.outcome='RecordedUnverified';original.revision=2;
  const data=JSON.parse(savedOriginal);
  original.events.push({revision:2,action:'RecordUnverifiedCoordinatorContact',
   requestId:data.requestId,memberRef:data.memberRef,
   originalMemberRequestRef:data.originalMemberRequestRef,
   originalSubmissionRequestId:data.originalSubmissionRequestId,
   claimedActorRef:data.claimedActorRef,
   unverifiedNoteOrDocumentRef:data.unverifiedNoteOrDocumentRef});
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(original)});
 });
 await panel.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(panel.getByTestId('center-care-status')).toContainText('نتیجه ثبت نامعلوم');
 const before=await (await request.get(root+encodeURIComponent(input.caseRef))).json();
 expect(before.events).toHaveLength(1);
 await page.unrouteAll({behavior:'wait'});
 await panel.getByRole('button',{name:'بررسی فقط‌خواندنی همان تماس'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(panel.getByTestId('center-care-status')).toContainText('همان UUID');
 await panel.getByRole('button',{name:'بازپخش دستی دقیق UUID قبلی'}).click();
 await expect(panel.getByTestId('center-care-uncertain')).toHaveCount(0);
 const after=await (await request.get(root+encodeURIComponent(input.caseRef))).json();
 expect(after.events).toHaveLength(2);
 expect(after.events[1].requestId).toBe(JSON.parse(savedOriginal).requestId);
 expect(after.realCoordinatorVerified).toBe(false);
});
test('CARE175-04 forged read claiming real authority fails closed, no new write enabled',async({page,request})=>{
 const input=await origin(request);
 await page.goto('/');
 const panel=page.getByTestId('center-care-coordination');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill(input.caseRef);
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const response=await route.fetch();const body=await response.json();
  body.clinicianAuthorized=true;body.nationalReleaseAllowed=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-status')).toContainText('نامعتبر');
 await page.unrouteAll({behavior:'wait'});
 await panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(panel.getByTestId('center-care-source')).toContainText('نسخه 1');
 await panel.getByLabel('شناسه پرونده هماهنگی مرکز').fill('real:patient');
 await expect(panel.getByTestId('center-care-source')).toHaveCount(0);
 await expect(panel.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'})).toBeDisabled();
});
