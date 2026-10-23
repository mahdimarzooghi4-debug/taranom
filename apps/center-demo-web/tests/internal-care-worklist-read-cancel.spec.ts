import {expect,test} from '@playwright/test';
// TRN-TECH-201. Center worklist cancellation is local revocation of
// GET-only status; never an access grant, contact or medical decision.
const journal='/internal/demo/v1/care-coordination/';
const headers={Origin:'http://127.0.0.1:4320'};
const ZERO='00000000-0000-0000-0000-000000000000';
function deferred(){
 let release!:()=>void,arrived!:()=>void;
 const hold=new Promise<void>(resolve=>{release=resolve;});
 const started=new Promise<void>(resolve=>{arrived=resolve;});
 return {hold,started,release,arrived};
}
async function seed(request:import('@playwright/test').APIRequestContext,n=4){
 const prefix='test:wrk192-'+crypto.randomUUID().replaceAll('-')+'-';
 const refs:string[]=[];
 for(let i=0;i<n;i++){
  const ref=prefix+String(i).padStart(2,'0');
  const data={requestId:crypto.randomUUID(),expectedRevision:0,
   action:'REQUEST_NONCLINICAL_COORDINATION',
   memberRef:'test:member192',originalMemberRequestRef:'test:reason192',
   originalSubmissionRequestId:ZERO,
   claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
  expect((await request.post(journal+ref+'/commands',{headers,data})).status()).toBe(200);
  refs.push(ref);
 }
 return {prefix,refs};
}
const getName='خواندن فهرست هماهنگی از ژورنال';
const cancelName='لغو خواندن یا ممیزی فقط‌خواندنی فهرست';
const auditName='ممیزی مستقل همه پرونده‌های همین صفحه';
async function open(page:import('@playwright/test').Page){
 await page.goto('/');
 return page.getByTestId('center-care-worklist');
}
async function read(work:import('@playwright/test').Locator,prefix:string){
 await work.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(prefix);
 await work.getByRole('button',{name:getName}).click();
 await expect(work.getByTestId('center-care-worklist-rows').locator('li')).toHaveCount(4);
}
async function cancel(work:import('@playwright/test').Locator){
 const cancelPanel=work.getByTestId('center-care-worklist-cancel');
 await expect(cancelPanel).toBeVisible();
 await cancelPanel.getByRole('button',{name:cancelName}).click();
 await expect(cancelPanel).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
}
function isWorklist(url:string,prefix:string){
 return url.includes('/care-coordination-worklist?')&&
  new URL(url).searchParams.get('afterCaseRef')===prefix;
}
test('CARE192-01 canceled slow page A cannot replace a manually fetched B or enable A selection',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 const work=await open(page),delay=deferred();
 const response=await (await request.get('/internal/demo/v1/care-coordination-worklist?afterCaseRef='+
  encodeURIComponent(a.prefix)+'&limit=4&claimFilter=ALL')).json();
 await work.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(a.prefix);
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  if(!isWorklist(route.request().url(),a.prefix))return route.continue();
  delay.arrived();await delay.hold;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(response)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await work.getByRole('button',{name:getName}).click();
 await delay.started;await cancel(work);
 await read(work,b.prefix);
 const rows=work.getByTestId('center-care-worklist-rows');
 for(const ref of b.refs)await expect(rows).toContainText(ref);
 const old=page.waitForResponse(res=>isWorklist(res.url(),a.prefix));
 delay.release();await old;
 for(const ref of b.refs)await expect(rows).toContainText(ref);
 await expect(rows).not.toContainText(a.refs[0]);
 await expect(work.getByRole('status')).toContainText('فهرست ممیزی‌شده');
 expect(posts).toBe(0);
});
test('CARE192-02 canceled late HTTP 503 never clears a newer valid page or status',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 const work=await open(page),delay=deferred();
 await work.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(a.prefix);
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  if(!isWorklist(route.request().url(),a.prefix))return route.continue();
  delay.arrived();await delay.hold;
  await route.fulfill({status:503,contentType:'application/json',body:'{}'});
 });
 await work.getByRole('button',{name:getName}).click();
 await delay.started;await cancel(work);
 await read(work,b.prefix);
 const status=await work.getByRole('status').innerText();
 const old=page.waitForResponse(res=>isWorklist(res.url(),a.prefix));
 delay.release();await old;
 await expect(work.getByRole('status')).toHaveText(status);
 await expect(work.getByTestId('center-care-worklist-rows')).toContainText(b.refs[0]);
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
});
test('CARE192-03 cancel four-case audit after first held GET; no remaining three case GETs and no partial authority',async({page,request})=>{
 const a=await seed(request),b=await seed(request);
 const work=await open(page);await read(work,a.prefix);
 const delay=deferred();let reads=0;
 const first=await (await request.get(journal+a.refs[0])).json();
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  reads++;
  if(!decodeURIComponent(route.request().url()).includes(a.refs[0]))
   return route.continue();
  delay.arrived();await delay.hold;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(first)});
 });
 await work.getByRole('button',{name:auditName}).click();
 await delay.started;await cancel(work);
 await expect(work.getByTestId('center-care-worklist-audit')).toHaveCount(0);
 await read(work,b.prefix);
 const response=page.waitForResponse(res=>decodeURIComponent(res.url()).includes(a.refs[0]));
 delay.release();await response;
 await expect(work.getByTestId('center-care-worklist-rows')).toContainText(b.refs[0]);
 // The previously planned 3 later independent source reads are not issued.
 await page.waitForTimeout(150);
 expect(reads).toBe(1);
});
test('CARE192-04 canceled same-page delta cannot restore obsolete activity after new filter baseline',async({page,request})=>{
 const a=await seed(request);
 const work=await open(page);await read(work,a.prefix);
 const delay=deferred();
 const source=await (await request.get('/internal/demo/v1/care-coordination-worklist?afterCaseRef='+
  encodeURIComponent(a.prefix)+'&limit=4&claimFilter=ALL')).json();
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  if(!isWorklist(route.request().url(),a.prefix))return route.continue();
  delay.arrived();await delay.hold;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(source)});
 });
 await work.getByRole('button',{name:'بررسی دستی تغییرات پرونده‌های همین صفحه'}).click();
 await delay.started;await cancel(work);
 await work.getByLabel('فیلتر ادعاهای هماهنگی').selectOption('HAS_CONTACT');
 await expect(work.getByTestId('center-care-worklist-activity')).toHaveCount(0);
 const old=page.waitForResponse(res=>isWorklist(res.url(),a.prefix));
 delay.release();await old;
 await expect(work.getByTestId('center-care-worklist-activity')).toHaveCount(0);
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 await expect(work.getByRole('status')).toContainText('هنوز فهرستی خوانده نشده');
});
test('CARE192-05 starting original Center write revokes pending worklist GET; no cancel may affect pending UUID',async({page,request})=>{
 const a=await seed(request),work=await open(page);
 const center=page.getByTestId('center-care-coordination');
 await center.getByLabel('شناسه پرونده هماهنگی مرکز').fill(a.refs[0]);
 await center.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(center.getByTestId('center-care-source')).toContainText('نسخه 1');
 const delay=deferred();
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  if(!isWorklist(route.request().url(),a.prefix))return route.continue();
  delay.arrived();await delay.hold;
  await route.fulfill({status:503,contentType:'application/json',body:'{}'});
 });
 await work.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(a.prefix);
 await work.getByRole('button',{name:getName}).click();
 await delay.started;
 await page.route('**/internal/demo/v1/care-coordination/*/commands',async route=>{
  const accepted=await route.fetch();expect(accepted.status()).toBe(200);
  await route.abort();
 });
 await center.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(center.getByTestId('center-care-uncertain')).toBeVisible();
 await expect(work.getByTestId('center-care-worklist-cancel')).toHaveCount(0);
 const old=page.waitForResponse(res=>isWorklist(res.url(),a.prefix));
 delay.release();await old;
 await expect(work.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 const journalSource=await (await request.get(journal+a.refs[0])).json();
 expect(journalSource.revision).toBe(2);
 expect(journalSource.nationalReleaseAllowed).toBe(false);
});
