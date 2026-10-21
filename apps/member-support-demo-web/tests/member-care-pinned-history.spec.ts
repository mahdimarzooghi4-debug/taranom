import {test,expect} from '@playwright/test';
// TRN-TECH-195. Real Chromium/Kestrel/PostgreSQL pinned history read only.
const root='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
const zero='00000000-0000-0000-0000-000000000000';
type A='RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
async function seed(request:import('@playwright/test').APIRequestContext,count=6){
 const ref='test:care186-'+crypto.randomUUID().replaceAll('-',''),rootId=crypto.randomUUID();
 const first={requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:member186',
  originalMemberRequestRef:'test:reason186',
  originalSubmissionRequestId:zero,claimedActorRef:null,
  unverifiedNoteOrDocumentRef:null};
 expect((await request.post(root+ref+'/commands',{headers:origin,data:first})).status()).toBe(200);
 for(let rev=1;rev<count;rev++)await append(request,ref,rootId,rev,rev%2
  ?'RECORD_UNVERIFIED_COORDINATOR_CONTACT':'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 return {ref,rootId};
}
async function append(request:import('@playwright/test').APIRequestContext,ref:string,
 rootId:string,revision:number,action:A){
 const res=await request.post(root+ref+'/commands',{headers:origin,data:{
  requestId:crypto.randomUUID(),expectedRevision:revision,action,
  memberRef:'test:member186',originalMemberRequestRef:'test:reason186',
  originalSubmissionRequestId:rootId,claimedActorRef:'test:actor186',
  unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
   ?'test:note186':'test:source186'}});
 expect(res.status()).toBe(200);
}
async function open(page:import('@playwright/test').Page,ref:string,n:number){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: '+n);
 return page.getByTestId('member-care-server-history');
}
const first='خواندن نخستین صفحه تاریخچه';
const next='صفحه بعدی تاریخچه با همان نسخه';
test('CARE186-01 full source-attested six-event care history spans independent fixed-version pages without POST',async({page,request})=>{
 const {ref,rootId}=await seed(request,6),panel=await open(page,ref,6);
 let writes=0,historyReads=0;
 page.on('request',r=>{
  if(r.method()==='POST')writes++;
  if(r.method()==='GET'&&r.url().includes('/history?'))historyReads++;
 });
 await panel.getByRole('button',{name:first}).click();
 const history=panel.getByTestId('member-care-server-history-page');
 await expect(history).toContainText('نسخه پین‌شده: 6');
 await expect(history.locator('li')).toHaveCount(4);
 await expect(history.locator('li').first()).toContainText(rootId);
 await expect(history.locator('li').nth(1)).toContainText('test:note186');
 await expect(panel.getByRole('button',{name:next})).toBeEnabled();
 await panel.getByRole('button',{name:next}).click();
 await expect(history).toContainText('شروع پس از نسخه: 4');
 await expect(history.locator('li')).toHaveCount(2);
 await expect(history.locator('li').first()).toContainText('نسخه 5');
 await expect(history.locator('li').last()).toContainText('test:source186');
 await expect(panel.getByRole('button',{name:next})).toHaveCount(0);
 expect(historyReads).toBe(2);expect(writes).toBe(0);
 const persisted=await (await request.get(root+ref)).json();
 expect(persisted.revision).toBe(6);
 expect(persisted.clinicalSourceVerified).toBe(false);
 expect(persisted.nationalReleaseAllowed).toBe(false);
});
test('CARE186-02 appended event between pages refuses stale revision pin and locks old UI',async({page,request})=>{
 const {ref,rootId}=await seed(request,5),panel=await open(page,ref,5);
 await panel.getByRole('button',{name:first}).click();
 await expect(panel.getByRole('button',{name:next})).toBeVisible();
 await append(request,ref,rootId,5,'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 let memberPosts=0;page.on('request',r=>{if(r.method()==='POST')memberPosts++;});
 await panel.getByRole('button',{name:next}).click();
 await expect(panel).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await expect(page.getByTestId('care-status')).toContainText('قابل تأیید نیست');
 expect(memberPosts).toBe(0);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: 6');
 await page.getByTestId('member-care-server-history').getByRole('button',{name:first}).click();
 await expect(page.getByTestId('member-care-server-history-page')).toContainText('نسخه پین‌شده: 6');
});
test('CARE186-03 forged successful history payload and authority flag fail closed without command',async({page,request})=>{
 const {ref}=await seed(request,5),panel=await open(page,ref,5);
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const response=await route.fetch(),body=await response.json();
  body.events[1].requestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await panel.getByRole('button',{name:first}).click();
 await expect(panel).toHaveCount(0);
 await expect(page.getByTestId('care-status')).toContainText('قابل تأیید نیست');
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const response=await route.fetch(),body=await response.json();
  body.clinicianAuthorized=true;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await page.getByTestId('member-care-server-history').getByRole('button',{name:first}).click();
 await expect(page.getByTestId('member-care-server-history')).toHaveCount(0);
 expect(posts).toBe(0);
});
test('CARE186-04 strict HTTP inputs and new case mutation leave no old page',async({page,request})=>{
 const {ref}=await seed(request,5),panel=await open(page,ref,5);
 for(const args of [
  'afterRevision=00&limit=4&expectedCurrentRevision=5',
  'afterRevision=0&limit=4&expectedCurrentRevision=05',
  'afterRevision=0&limit=4&expectedCurrentRevision=5&actor=doctor',
  'afterRevision=0&limit=4&expectedCurrentRevision=5&limit=4',
  'afterRevision=0&limit=11&expectedCurrentRevision=5',
  'afterRevision=6&limit=4&expectedCurrentRevision=5'
 ]){
  const res=await request.get(root+ref+'/history?'+args,{failOnStatusCode:false});
  expect(res.status(),args).toBe(400);
  const data=await res.json();
  expect(data.nationalReleaseAllowed).toBe(false);
 }
 await panel.getByRole('button',{name:first}).click();
 await expect(panel.getByTestId('member-care-server-history-page')).toBeVisible();
 await page.getByLabel('شناسه آزمایشی پرونده').fill('test:other-care186');
 await expect(panel).toHaveCount(0);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const other=page.getByTestId('member-care-server-history');
 await other.getByRole('button',{name:first}).click();
 await expect(other.getByTestId('member-care-server-history-page').locator('li')).toHaveCount(0);
});
