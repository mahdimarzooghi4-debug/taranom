import {expect,test} from '@playwright/test';
// TRN-TECH-197: exact source-backed synthetic, never clinical or duty status.
const base='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
type Kind='RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
async function append(request:import('@playwright/test').APIRequestContext,
 ref:string,rootId:string,rev:number,action:Kind){
 const data={requestId:crypto.randomUUID(),expectedRevision:rev,action,
  memberRef:'test:member188',originalMemberRequestRef:'test:reason188',
  originalSubmissionRequestId:rootId,claimedActorRef:'test:actor188',
  unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
   ?'test:contact188':'test:source188'};
 expect((await request.post(base+ref+'/commands',{data,headers:origin})).status()).toBe(200);
}
async function seed(request:import('@playwright/test').APIRequestContext,
 count=11){
 const ref='test:filter188-'+crypto.randomUUID().replaceAll('-','');
 const rootId=crypto.randomUUID();
 const original={requestId:rootId,expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',
  memberRef:'test:member188',originalMemberRequestRef:'test:reason188',
  originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(base+ref+'/commands',{data:original,headers:origin})).status()).toBe(200);
 for(let rev=1;rev<count;rev++)await append(request,ref,rootId,rev,
  rev%2?'RECORD_UNVERIFIED_COORDINATOR_CONTACT':'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 return {ref,rootId,count};
}
async function member(page:import('@playwright/test').Page,ref:string,n:number){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: '+n);
 return page.getByTestId('member-care-server-history');
}
async function center(page:import('@playwright/test').Page,ref:string,n:number){
 await page.goto('/');
 const w=page.getByTestId('center-care-coordination');
 await w.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await w.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(w.getByTestId('center-care-source')).toContainText('نسخه '+n);
 return w;
}
test('CARE188-01 Member CONTACT and Center SOURCE preserve sparse original revision cursors, no browser POST',async({page,request})=>{
 const {ref}=await seed(request);
 let posts=0,historyGets=0;
 page.on('request',r=>{
  if(r.method()==='POST')posts++;
  if(r.method()==='GET'&&r.url().includes('/history?'))historyGets++;
 });
 const mem=await member(page,ref,11);
 await mem.getByLabel('فیلتر تاریخچه هماهنگی عضو').selectOption('CONTACT');
 await mem.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه'}).click();
 const m=mem.getByTestId('member-care-server-history-page');
 await expect(m).toContainText('نوع ادعا: CONTACT');
 await expect(m.locator('li')).toHaveCount(4);
 for(const [i,revision] of [2,4,6,8].entries())
  await expect(m.locator('li').nth(i)).toContainText('نسخه '+revision+':');
 await mem.getByRole('button',{name:'صفحه بعدی تاریخچه با همان نسخه'}).click();
 await expect(m).toContainText('شروع پس از نسخه: 8');
 await expect(m.locator('li')).toHaveCount(1);
 await expect(m.locator('li')).toContainText('نسخه 10:');
 await expect(m).not.toContainText('test:source188');
 await mem.getByLabel('فیلتر تاریخچه هماهنگی عضو').selectOption('SOURCE');
 await expect(m).toHaveCount(0);
 await mem.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه'}).click();
 await expect(m.locator('li')).toHaveCount(4);
 for(const [i,revision] of [3,5,7,9].entries())
  await expect(m.locator('li').nth(i)).toContainText('نسخه '+revision+':');
 await mem.getByRole('button',{name:'صفحه بعدی تاریخچه با همان نسخه'}).click();
 await expect(m).toContainText('شروع پس از نسخه: 9');
 await expect(m.locator('li')).toContainText('نسخه 11:');
 const cen=await center(page,ref,11);
 const h=cen.getByTestId('center-care-server-history');
 await h.getByLabel('فیلتر تاریخچه هماهنگی مرکز').selectOption('CONTACT');
 await h.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه مرکز'}).click();
 const c=h.getByTestId('center-care-server-history-page');
 await expect(c).toContainText('نوع ادعا: CONTACT');
 await expect(c.locator('li')).toHaveCount(4);
 for(const [i,revision] of [2,4,6,8].entries())
  await expect(c.locator('li').nth(i)).toContainText('نسخه '+revision+':');
 await h.getByRole('button',{name:'صفحه بعدی تاریخچه مرکز با همان نسخه'}).click();
 await expect(c).toContainText('شروع پس از نسخه: 8');
 await expect(c.locator('li')).toContainText('نسخه 10:');
 expect(posts).toBe(0);expect(historyGets).toBe(6);
 const persisted=await (await request.get(base+ref)).json();
 expect(persisted.revision).toBe(11);
 expect(persisted.clinicalSourceVerified).toBe(false);
 expect(persisted.nationalReleaseAllowed).toBe(false);
});
test('CARE188-02 sparse filter with zero matches is a validated empty page, and legacy ALL is unchanged',async({page,request})=>{
 const {ref}=await seed(request,1),m=await member(page,ref,1);
 await m.getByLabel('فیلتر تاریخچه هماهنگی عضو').selectOption('SOURCE');
 await m.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه'}).click();
 const empty=m.getByTestId('member-care-server-history-page');
 await expect(empty).toBeVisible();
 await expect(empty.locator('li')).toHaveCount(0);
 await expect(m.getByRole('button',{name:'صفحه بعدی تاریخچه با همان نسخه'})).toHaveCount(0);
 const response=await request.get(base+ref+'/history?afterRevision=0&limit=4&expectedCurrentRevision=1');
 const legacy=await response.json();
 expect(response.status()).toBe(200);
 expect(legacy.claimType).toBe('ALL');
 expect(legacy.events.map((x:{revision:number})=>x.revision)).toEqual([1]);
 expect(legacy.nationalReleaseAllowed).toBe(false);
});
test('CARE188-03 forged filter label or sparse row fails closed in both workspaces',async({page,request})=>{
 const {ref}=await seed(request,5),m=await member(page,ref,5);
 await m.getByLabel('فیلتر تاریخچه هماهنگی عضو').selectOption('CONTACT');
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const response=await route.fetch(),json=await response.json();
  json.claimType='SOURCE';
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(json)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await m.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه'}).click();
 await expect(m).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 const cen=await center(page,ref,5);
 const history=cen.getByTestId('center-care-server-history');
 await history.getByLabel('فیلتر تاریخچه هماهنگی مرکز').selectOption('SOURCE');
 await page.route('**/internal/demo/v1/care-coordination/*/history?*',async route=>{
  const response=await route.fetch(),json=await response.json();
  json.events[0].revision=4;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(json)});
 });
 await history.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه مرکز'}).click();
 await expect(cen.getByTestId('center-care-server-history')).toHaveCount(0);
 await expect(cen.getByTestId('center-care-source')).toHaveCount(0);
 expect(posts).toBe(0);
});
test('CARE188-04 concurrent different-type append still invalidates pinned sparse history',async({page,request})=>{
 const {ref,rootId}=await seed(request,10),cen=await center(page,ref,10);
 const history=cen.getByTestId('center-care-server-history');
 await history.getByLabel('فیلتر تاریخچه هماهنگی مرکز').selectOption('CONTACT');
 await history.getByRole('button',{name:'خواندن نخستین صفحه تاریخچه مرکز'}).click();
 await expect(history.getByRole('button',{name:'صفحه بعدی تاریخچه مرکز با همان نسخه'})).toBeVisible();
 // Appending SOURCE, not CONTACT, must still invalidate the global pin.
 await append(request,ref,rootId,10,'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await history.getByRole('button',{name:'صفحه بعدی تاریخچه مرکز با همان نسخه'}).click();
 await expect(cen.getByTestId('center-care-server-history')).toHaveCount(0);
 await expect(cen.getByTestId('center-care-source')).toHaveCount(0);
 await expect(cen.getByTestId('center-care-status')).toContainText('قابل تأیید نیست');
 expect(posts).toBe(0);
 await cen.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(cen.getByTestId('center-care-source')).toContainText('نسخه 11');
});
test('CARE188-05 rejected duplicate, unauthorized, wrong case and lowercase filters have no authority',async({request})=>{
 const {ref}=await seed(request,5);
 const prefix=base+ref+'/history?afterRevision=0&limit=4&expectedCurrentRevision=5';
 for(const tail of ['&claimType=URGENT','&claimType=contact',
  '&claimType=CONTACT&claimType=SOURCE',
  '&claimType=CONTACT&clinicalApproval=true',
  '&claimType=SOURCE&actor=doctor']){
  const res=await request.get(prefix+tail,{failOnStatusCode:false});
  expect(res.status(),tail).toBe(400);
  expect((await res.json()).nationalReleaseAllowed).toBe(false);
 }
 const success=await (await request.get(prefix+'&claimType=SOURCE')).json();
 expect(success.claimType).toBe('SOURCE');
 expect(success.events.map((x:{revision:number})=>x.revision)).toEqual([3,5]);
 expect(success.clinicianAuthorized).toBe(false);
});
