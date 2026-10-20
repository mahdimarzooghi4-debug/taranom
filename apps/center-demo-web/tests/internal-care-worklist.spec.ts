import {expect,test} from '@playwright/test';
// TRN-TECH-186: real Kestrel + restricted reader and PostgreSQL, CI-only.
const root='/internal/demo/v1/care-coordination/';
const header={Origin:'http://127.0.0.1:4320'};
async function create(request:import('@playwright/test').APIRequestContext,
 caseRef:string,withContact=false){
 const initialId=crypto.randomUUID();
 const base={
  requestId:initialId,expectedRevision:0,action:'REQUEST_NONCLINICAL_COORDINATION',
  memberRef:'test:care177-member',originalMemberRequestRef:'test:care177-reason',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null
 };
 let r=await request.post(root+encodeURIComponent(caseRef)+'/commands',
  {data:base,headers:header});
 expect(r.status()).toBe(200);
 if(withContact){
  r=await request.post(root+encodeURIComponent(caseRef)+'/commands',
   {data:{...base,requestId:crypto.randomUUID(),expectedRevision:1,
    action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
    originalSubmissionRequestId:initialId,
    claimedActorRef:'test:care177-actor',
    unverifiedNoteOrDocumentRef:'test:care177-note'},headers:header});
  expect(r.status()).toBe(200);
 }
}
async function fixture(request:import('@playwright/test').APIRequestContext){
 const prefix='test:care177-'+crypto.randomUUID().replaceAll('-','')+'-';
 const cases:string[]=[];
 for(let i=0;i<6;i++){
  const ref=prefix+String(i).padStart(2,'0');
  await create(request,ref,i===2||i===5);
  cases.push(ref);
 }
 return {prefix,cases};
}
test('CARE177-01 source-backed queue pages in key order and selects existing case without POST',async({page,request})=>{
 const s=await fixture(request);
 await page.goto('/');
 const list=page.getByTestId('center-care-worklist');
 const cursor=list.getByLabel('شروع فهرست هماهنگی پس از پرونده');
 await cursor.fill(s.prefix);
 let writes=0;page.on('request',r=>{if(r.method()==='POST')writes++;});
 await list.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(list.getByRole('status')).toContainText('فهرست ممیزی‌شده');
 const rows=list.getByTestId('center-care-worklist-rows');
 await expect(rows.locator('li')).toHaveCount(4);
 for(const ref of s.cases.slice(0,4))await expect(rows).toContainText(ref);
 await expect(rows.locator('li').nth(2)).toContainText('ادعاهای تماس 1');
 await list.getByRole('button',{name:'صفحه بعد هماهنگی‌ها'}).click();
 await expect(rows.locator('li').nth(0)).toContainText(s.cases[4]);
 await expect(rows.locator('li').nth(1)).toContainText(s.cases[5]);
 await expect(rows.locator('li').nth(1)).toContainText('ادعاهای تماس 1');
 const selected=s.cases[5];
 await rows.locator('li').filter({hasText:selected})
  .getByRole('button',{name:'انتخاب پرونده هماهنگی'}).click();
 const workspace=page.getByTestId('center-care-coordination');
 await expect(workspace.getByLabel('شناسه پرونده هماهنگی مرکز')).toHaveValue(selected);
 await expect(workspace.getByTestId('center-care-source')).toHaveCount(0);
 expect(writes).toBe(0);
 await workspace.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(workspace.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(workspace.getByTestId('center-care-source')).toContainText('test:care177-note');
 expect(writes).toBe(0);
});
test('CARE177-02 exact independent PostgreSQL cursor, counters and legacy case read match',async({request})=>{
 const {prefix,cases}=await fixture(request);
 const route='/internal/demo/v1/care-coordination-worklist?afterCaseRef=';
 const first=await request.get(route+encodeURIComponent(prefix)+'&limit=4');
 expect(first.status()).toBe(200);
 const a=await first.json();
 expect(a.contract).toBe('TRN-TECH-186');
 expect(a.source).toBe('VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL');
 expect(a.cases.map((x:{caseRef:string})=>x.caseRef)).toEqual(cases.slice(0,4));
 expect(a.hasMore).toBe(true);
 expect(a.nextAfterCaseRef).toBe(cases[3]);
 expect(a.cases[2].revision).toBe(2);
 expect(a.cases[2].contactClaims).toBe(1);
 expect(a.cases[2].sourceClaims).toBe(0);
 const second=await request.get(route+encodeURIComponent(cases[3])+'&limit=2');
 expect(second.status()).toBe(200);
 const b=await second.json();
 expect(b.cases.map((x:{caseRef:string})=>x.caseRef)).toEqual(cases.slice(4));
 const selected=await (await request.get(root+encodeURIComponent(cases[2]))).json();
 expect(selected.revision).toBe(a.cases[2].revision);
 for(const key of ['realMemberAuthenticated','realCoordinatorVerified',
  'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
  'referralDelivered','appointmentBooked','treatmentStarted',
  'medicalRecordAccessGranted','emergencyServiceDispatched',
  'consentGranted','walletMutated','nationalReleaseAllowed']){
  expect(a[key],key).toBe(false);
 }
});
test('CARE177-03 invalid cursors, extra parameters and forged source read refuse the worklist',async({page,request})=>{
 const invalid=[
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=&limit=0',
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=&limit=11',
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=&limit=04',
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=&limit=4&limit=5',
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=&limit=4&memberRef=test:other',
  '/internal/demo/v1/care-coordination-worklist?afterCaseRef=real:patient&limit=4'
 ];
 for(const path of invalid){
  const r=await request.get(path,{failOnStatusCode:false});
  expect(r.status(),path).toBe(400);
  expect((await r.json()).nationalReleaseAllowed).toBe(false);
 }
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  const url=new URL(route.request().url());
  const x={contract:'TRN-TECH-186',syntheticOnly:true,unverifiedClaimsOnly:true,
   source:'VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL',
   afterCaseRef:url.searchParams.get('afterCaseRef'),limit:4,hasMore:false,
   nextAfterCaseRef:null,cases:[{caseRef:'test:forged-case',revision:1,
     contactClaims:0,sourceClaims:0}],
   realMemberAuthenticated:false,realCoordinatorVerified:false,
   clinicalSourceVerified:false,clinicianAuthorized:true,
   referralOrdered:false,referralDelivered:false,appointmentBooked:false,
   treatmentStarted:false,medicalRecordAccessGranted:false,
   emergencyServiceDispatched:false,consentGranted:false,walletMutated:false,
   nationalReleaseAllowed:false};
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(x)});
 });
 await page.goto('/');
 const list=page.getByTestId('center-care-worklist');
 await list.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(list.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(list.getByTestId('center-care-worklist-rows')).toHaveCount(0);
});
