import {expect,test} from '@playwright/test';
// TRN-TECH-193: operator-clicked independent read of the same synthetic
// PostgreSQL care-journal case versions, not professional verification.
const journal='/internal/demo/v1/care-coordination/';
const headers={Origin:'http://127.0.0.1:4320'};
type Kind='NONE'|'CONTACT'|'SOURCE'|'BOTH';
async function seed(request:import('@playwright/test').APIRequestContext,
 kinds:Kind[]=['NONE','CONTACT','SOURCE','BOTH']){
 const prefix='test:audit184-'+crypto.randomUUID().replaceAll('-')+'-';
 const refs:string[]=[];
 for(let i=0;i<kinds.length;i++){
  const ref=prefix+String(i).padStart(2,'0'),rootId=crypto.randomUUID();
  const root={requestId:rootId,expectedRevision:0,
   action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:member184',
   originalMemberRequestRef:'test:reason184',
   originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
   claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
  expect((await request.post(journal+ref+'/commands',
   {headers,data:root})).status()).toBe(200);
  const followups=kinds[i]==='NONE'?[]:
   kinds[i]==='BOTH'?['RECORD_UNVERIFIED_COORDINATOR_CONTACT',
    'ATTACH_UNVERIFIED_CLINICAL_SOURCE']:
   kinds[i]==='CONTACT'?['RECORD_UNVERIFIED_COORDINATOR_CONTACT']:
    ['ATTACH_UNVERIFIED_CLINICAL_SOURCE'];
  for(let j=0;j<followups.length;j++){
   const action=followups[j];
   expect((await request.post(journal+ref+'/commands',{headers,data:{
    ...root,requestId:crypto.randomUUID(),expectedRevision:j+1,action,
    originalSubmissionRequestId:rootId,
    claimedActorRef:'test:coordinator184',
    unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
     ?'test:note184':'test:clinical184'
   }})).status()).toBe(200);
  }
  refs.push(ref);
 }
 return {prefix,refs};
}
async function open(page:import('@playwright/test').Page,prefix:string){
 await page.goto('/');
 const work=page.getByTestId('center-care-worklist');
 await work.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(prefix);
 await work.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(work.getByTestId('center-care-worklist-rows').locator('li')).toHaveCount(4);
 return work;
}
const inspect='ممیزی مستقل همه پرونده‌های همین صفحه';
test('CARE184-01 full page independent journal attestation matches versions, two claim kinds and denies real authority',async({page,request})=>{
 const {prefix,refs}=await seed(request);
 const work=await open(page,prefix);
 let post=0,individualGet=0;
 page.on('request',req=>{
  if(req.method()==='POST')post++;
  if(req.method()==='GET'&&/\/care-coordination\/test%3A/i.test(req.url()))
   individualGet++;
 });
 await work.getByRole('button',{name:inspect}).click();
 const audit=work.getByTestId('center-care-worklist-audit');
 await expect(audit).toContainText('4 پرونده');
 await expect(audit.locator('li')).toHaveCount(4);
 for(let i=0;i<4;i++){
  const row=audit.locator('li').nth(i);
  await expect(row).toContainText(refs[i]);
  const source=await (await request.get(journal+refs[i])).json();
  await expect(row).toContainText('نسخه '+source.revision);
  await expect(row).toContainText('ادعای تماس '+
   source.events.filter((e:{action:string})=>e.action==='RecordUnverifiedCoordinatorContact').length);
  await expect(row).toContainText('مرجع بالینی تأییدنشده '+
   source.events.filter((e:{action:string})=>e.action==='AttachUnverifiedClinicalSourceReference').length);
  expect(source.realMemberAuthenticated).toBe(false);
  expect(source.nationalReleaseAllowed).toBe(false);
 }
 expect(individualGet).toBe(4);expect(post).toBe(0);
 await work.getByLabel('فیلتر ادعاهای هماهنگی').selectOption('HAS_CONTACT');
 await expect(audit).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
});
test('CARE184-02 case revised after worklist read fails entire batch closed; manual fresh baseline works',async({page,request})=>{
 const {prefix,refs}=await seed(request,['NONE','NONE','NONE','NONE']);
 const work=await open(page,prefix);
 const old=await (await request.get(journal+refs[2])).json();
 const rootId=old.events[0].requestId;
 const write={requestId:crypto.randomUUID(),expectedRevision:1,
 action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',memberRef:'test:member184',
 originalMemberRequestRef:'test:reason184',originalSubmissionRequestId:rootId,
 claimedActorRef:'test:coordinator184',unverifiedNoteOrDocumentRef:'test:late184'};
 expect((await request.post(journal+refs[2]+'/commands',
  {data:write,headers})).status()).toBe(200);
 let browserPosts=0;
 page.on('request',r=>{if(r.method()==='POST')browserPosts++;});
 await work.getByRole('button',{name:inspect}).click();
 await expect(work.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 await work.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(work.getByTestId('center-care-worklist-rows')).toContainText('ادعاهای تماس 1');
 await work.getByRole('button',{name:inspect}).click();
 await expect(work.getByTestId('center-care-worklist-audit').locator('li')).toHaveCount(4);
 expect(browserPosts).toBe(0);
});
test('CARE184-03 forged authority and forged event lineage on independent read invalidate whole page',async({page,request})=>{
 const {prefix,refs}=await seed(request);
 const work=await open(page,prefix);
 let reads=0,posts=0;
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  reads++;
  const response=await route.fetch(),raw=await response.json();
  if(route.request().url().includes(encodeURIComponent(refs[1]))){
   raw.clinicianAuthorized=true;
  }
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(raw)});
 });
 page.on('request',r=>{if(r.method()==='POST')posts++;});
 await work.getByRole('button',{name:inspect}).click();
 await expect(work.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 expect(reads).toBe(2);expect(posts).toBe(0);
 await page.unrouteAll({behavior:'wait'});
 await work.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  const response=await route.fetch(),raw=await response.json();
  if(route.request().url().includes(encodeURIComponent(refs[3]))){
   raw.events[2].originalSubmissionRequestId=crypto.randomUUID();
  }
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(raw)});
 });
 await work.getByRole('button',{name:inspect}).click();
 await expect(work.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 expect(posts).toBe(0);
});
test('CARE184-04 HTTP-denied independent case read cannot leave previous actionable rows',async({page,request})=>{
 const {prefix}=await seed(request);
 const work=await open(page,prefix);
 await page.route('**/internal/demo/v1/care-coordination/*',route=>
  route.fulfill({status:503,contentType:'application/json',body:'{}'}));
 await work.getByRole('button',{name:inspect}).click();
 await expect(work.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
});
