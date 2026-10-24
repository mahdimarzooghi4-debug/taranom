import {expect,test} from '@playwright/test';
// TRN-TECH-196: only synthetic unverified PostgreSQL claims. No patient access.
const root='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
const zero='00000000-0000-0000-0000-000000000000';
type Follow='RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
async function append(request:import('@playwright/test').APIRequestContext,
 ref:string,rootId:string,revision:number,action:Follow){
 const response=await request.post(root+ref+'/commands',{headers:origin,data:{
  requestId:crypto.randomUUID(),expectedRevision:revision,action,
  memberRef:'test:member187',originalMemberRequestRef:'test:reason187',
  originalSubmissionRequestId:rootId,claimedActorRef:'test:actor187',
  unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
   ?'test:note187':'test:source187'
 }});
 expect(response.status()).toBe(200);
}
async function seed(request:import('@playwright/test').APIRequestContext,count=6){
 const ref='test:center187-'+crypto.randomUUID().replaceAll('-','');
 const rootId=crypto.randomUUID();
 expect((await request.post(root+ref+'/commands',{headers:origin,data:{
  requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',
  memberRef:'test:member187',originalMemberRequestRef:'test:reason187',
  originalSubmissionRequestId:zero,claimedActorRef:null,
  unverifiedNoteOrDocumentRef:null
 }})).status()).toBe(200);
 for(let rev=1;rev<count;rev++)await append(request,ref,rootId,rev,
  rev%2?'RECORD_UNVERIFIED_COORDINATOR_CONTACT':'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 return {ref,rootId};
}
async function open(page:import('@playwright/test').Page,ref:string,revision:number){
 await page.goto('/');
 const work=page.getByTestId('center-care-coordination');
 await work.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await work.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(work.getByTestId('center-care-source')).toContainText('نسخه '+revision);
 return work;
}
const first='خواندن نخستین صفحه تاریخچه مرکز';
const next='صفحه بعدی تاریخچه مرکز با همان نسخه';

test('CARE187-01 Center independently reads pinned six-event history in two pages with exact original lineage and no POST',async({page,request})=>{
 const {ref,rootId}=await seed(request),work=await open(page,ref,6);
 let posts=0,reads=0;
 page.on('request',r=>{
  if(r.method()==='POST')posts++;
  if(r.method()==='GET'&&r.url().includes('/history?'))reads++;
 });
 const history=work.getByTestId('center-care-server-history');
 await history.getByRole('button',{name:first}).click();
 const entries=history.getByTestId('center-care-server-history-page');
 await expect(entries).toContainText('نسخه پین‌شده: 6');
 await expect(entries.locator('li')).toHaveCount(4);
 await expect(entries.locator('li').first()).toContainText(rootId);
 await expect(entries.locator('li').nth(1)).toContainText('test:note187');
 await expect(history.getByRole('button',{name:next})).toBeEnabled();
 await history.getByRole('button',{name:next}).click();
 await expect(entries).toContainText('شروع پس از نسخه: 4');
 await expect(entries.locator('li')).toHaveCount(2);
 await expect(entries.locator('li').first()).toContainText('نسخه 5');
 await expect(entries.locator('li').first()).toContainText('test:source187');
 await expect(entries.locator('li').last()).toContainText('نسخه 6');
 await expect(entries.locator('li').last()).toContainText('test:note187');
 await expect(history.getByRole('button',{name:next})).toHaveCount(0);
 expect(reads).toBe(2);expect(posts).toBe(0);
 const journal=await (await request.get(root+ref)).json();
 expect(journal.revision).toBe(6);
 expect(journal.clinicalSourceVerified).toBe(false);
 expect(journal.nationalReleaseAllowed).toBe(false);
});

test('CARE187-02 append after first page yields revision conflict and removes all old actionable Center source',async({page,request})=>{
 const {ref,rootId}=await seed(request,5),work=await open(page,ref,5);
 const history=work.getByTestId('center-care-server-history');
 await history.getByRole('button',{name:first}).click();
 await expect(history.getByRole('button',{name:next})).toBeVisible();
 await append(request,ref,rootId,5,'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await history.getByRole('button',{name:next}).click();
 await expect(work.getByTestId('center-care-server-history')).toHaveCount(0);
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 await expect(work.getByTestId('center-care-status')).toContainText('قابل تأیید نیست');
 expect(posts).toBe(0);
 await work.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(work.getByTestId('center-care-source')).toContainText('نسخه 6');
 await work.getByTestId('center-care-server-history').getByRole('button',{name:first}).click();
 await expect(work.getByTestId('center-care-server-history-page')).toContainText('نسخه پین‌شده: 6');
});

test('CARE187-03 forged history UUID and clinical authorization both deny page and all writes',async({page,request})=>{
 const {ref}=await seed(request,5),work=await open(page,ref,5);
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const result=await route.fetch(),body=await result.json();
  body.events[0].requestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await work.getByTestId('center-care-server-history').getByRole('button',{name:first}).click();
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 await expect(work.getByTestId('center-care-server-history')).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 await work.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const result=await route.fetch(),body=await result.json();
  body.clinicianAuthorized=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await work.getByTestId('center-care-server-history').getByRole('button',{name:first}).click();
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 expect(posts).toBe(0);
});

test('CARE187-04 invalid pin parameters, case change and unavailable page never preserve stale Center history',async({page,request})=>{
 const {ref}=await seed(request,5),work=await open(page,ref,5);
 for(const args of [
  'afterRevision=00&limit=4&expectedCurrentRevision=5',
  'afterRevision=0&limit=4&expectedCurrentRevision=05',
  'afterRevision=0&limit=4&expectedCurrentRevision=5&limit=4',
  'afterRevision=0&limit=4&expectedCurrentRevision=5&clinicalApproval=true',
  'afterRevision=6&limit=4&expectedCurrentRevision=5'
 ]){
  const response=await request.get(root+ref+'/history?'+args,{failOnStatusCode:false});
  expect(response.status(),args).toBe(400);
  expect((await response.json()).nationalReleaseAllowed).toBe(false);
 }
 const history=work.getByTestId('center-care-server-history');
 await history.getByRole('button',{name:first}).click();
 await expect(history.getByTestId('center-care-server-history-page')).toBeVisible();
 await work.getByLabel('شناسه پرونده هماهنگی مرکز').fill('test:other187');
 await expect(history).toHaveCount(0);
 await work.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await work.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',route=>
  route.fulfill({status:503,contentType:'application/json',body:'{}'}));
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await work.getByTestId('center-care-server-history').getByRole('button',{name:first}).click();
 await expect(work.getByTestId('center-care-source')).toHaveCount(0);
 await expect(work.getByTestId('center-care-server-history')).toHaveCount(0);
 expect(posts).toBe(0);
});
