import {test,expect} from '@playwright/test';
// TRN-TECH-194. Source-verified, test-only historical event exploration.
// A past version is display-only and NEVER a write-authority snapshot.
const base='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
async function seeded(request:import('@playwright/test').APIRequestContext,follow=true){
 const ref='test:care185-'+crypto.randomUUID().replaceAll('-','');
 const rootId=crypto.randomUUID();
 const first={requestId:rootId,expectedRevision:0,action:'REQUEST_NONCLINICAL_COORDINATION',
 memberRef:'test:member185',originalMemberRequestRef:'test:reason185',
 originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
 claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 expect((await request.post(base+ref+'/commands',{data:first,headers:origin})).status()).toBe(200);
 if(follow){
  for(const [i,action] of ['RECORD_UNVERIFIED_COORDINATOR_CONTACT',
   'ATTACH_UNVERIFIED_CLINICAL_SOURCE'].entries()){
   expect((await request.post(base+ref+'/commands',{headers:origin,data:{
    ...first,requestId:crypto.randomUUID(),expectedRevision:i+1,
    action,originalSubmissionRequestId:rootId,
    claimedActorRef:'test:claimed-actor185',
    unverifiedNoteOrDocumentRef:i===0?'test:note185':'test:clinical185'
   }})).status()).toBe(200);
  }
 }
 return {ref,rootId};
}
async function load(page:import('@playwright/test').Page,ref:string,version:number){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const panel=page.getByTestId('member-care-version-explorer');
 await expect(panel).toBeVisible();
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'}))
  .toContainText('نسخه پرونده: '+version);
 return panel;
}
test('CARE185-01 member inspects exact root/claim lineage and as-of counters without writes',async({page,request})=>{
 const {ref,rootId}=await seeded(request);
 const panel=await load(page,ref,3);
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 const selection=panel.getByLabel('نسخه تاریخی برای مشاهده');
 const previous=panel.getByTestId('member-care-as-of-events');
 const newer=panel.getByTestId('member-care-after-events');
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('نمای نسخه 3 از 3');
 await expect(previous.locator('li')).toHaveCount(3);
 await expect(newer.locator('li')).toHaveCount(0);
 await selection.selectOption('0');
 await expect(previous.locator('li')).toHaveCount(0);
 await expect(newer.locator('li')).toHaveCount(3);
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('ادعاهای تماس تا این نسخه: 0');
 await selection.selectOption('1');
 await expect(previous.locator('li')).toHaveCount(1);
 await expect(previous).toContainText(rootId);
 await expect(previous).toContainText('test:member185');
 await expect(previous).toContainText('test:reason185');
 await expect(previous).toContainText('00000000-0000-0000-0000-000000000000');
 await expect(newer.locator('li')).toHaveCount(2);
 await expect(page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'})).toBeDisabled();
 await expect(page.getByRole('button',{name:'پیوست ارجاع به منبع بالینیِ تأییدنشده'})).toBeDisabled();
 await selection.selectOption('2');
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('ادعاهای تماس تا این نسخه: 1');
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('منابع بالینی ادعایی تا این نسخه: 0');
 await expect(previous).toContainText('test:note185');
 await expect(previous).toContainText('test:claimed-actor185');
 await expect(newer).toContainText('مرجع بالینی ادعایی تأییدنشده');
 await panel.getByRole('button',{name:'بازگشت به نسخه جاری هماهنگی'}).click();
 await expect(previous.locator('li')).toHaveCount(3);
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('ادعاهای تماس تا این نسخه: 1');
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('منابع بالینی ادعایی تا این نسخه: 1');
 await expect(page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'})).toBeEnabled();
 expect(posts).toBe(0);
 const server=await (await request.get(base+ref)).json();
 expect(server.events).toHaveLength(3);
 expect(server.clinicalSourceVerified).toBe(false);
 expect(server.nationalReleaseAllowed).toBe(false);
});
test('CARE185-02 Member historical selection remains local while Center appends, manual GET resets to latest',async({page,context,request})=>{
 const {ref}=await seeded(request,false);
 const panel=await load(page,ref,1);
 await panel.getByLabel('نسخه تاریخی برای مشاهده').selectOption('0');
 await expect(panel.getByTestId('member-care-after-events').locator('li')).toHaveCount(1);
 const center=await context.newPage();
 await center.goto('/');
 const care=center.getByTestId('center-care-coordination');
 await care.getByLabel('شناسه پرونده هماهنگی مرکز').fill(ref);
 await care.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(care.getByTestId('center-care-source')).toContainText('نسخه 1');
 await care.getByLabel('مرجع یادداشت تماس آزمایشی').fill('test:cross185');
 await care.getByRole('button',{name:'ثبت مستقل ادعای تماس هماهنگ‌کننده'}).click();
 await expect(care.getByTestId('center-care-source')).toContainText('نسخه 2');
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('نمای نسخه 0 از 1');
 let writes=0;page.on('request',r=>{if(r.method()==='POST')writes++;});
 await page.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی عضو'}).click();
 await expect(panel.getByTestId('member-care-as-of-counts')).toContainText('نمای نسخه 2 از 2');
 await expect(panel.getByTestId('member-care-as-of-events')).toContainText('test:cross185');
 await expect(panel.getByTestId('member-care-after-events').locator('li')).toHaveCount(0);
 await expect(page.getByTestId('member-care-activity')).toContainText('1 رویداد ادعایی');
 expect(writes).toBe(0);
 await center.close();
});
test('CARE185-03 forged historical source fails closed and clears the explorer without POST',async({page,request})=>{
 const {ref}=await seeded(request);
 const panel=await load(page,ref,3);
 await panel.getByLabel('نسخه تاریخی برای مشاهده').selectOption('1');
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const reply=await route.fetch(),body=await reply.json();
  body.events[1].originalSubmissionRequestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await page.getByRole('button',{name:'بررسی دستی تغییرات هماهنگی عضو'}).click();
 await expect(panel).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await expect(page.getByTestId('care-status')).toContainText('تأیید نیست');
 expect(posts).toBe(0);
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('member-care-as-of-counts')).toContainText('نمای نسخه 3 از 3');
});
test('CARE185-04 changing case or importing a recovery receipt invalidates old historical view',async({page,request})=>{
 const {ref}=await seeded(request);
 const panel=await load(page,ref,3);
 await panel.getByLabel('نسخه تاریخی برای مشاهده').selectOption('1');
 await page.getByLabel('شناسه آزمایشی پرونده').fill('test:another-case185');
 await expect(panel).toHaveCount(0);
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 await expect(page.getByTestId('member-care-as-of-counts')).toContainText('نمای نسخه 3 از 3');
 const original={contract:'TRN-TECH-189',syntheticOnly:true,surface:'MEMBER',
  caseRef:ref,body:JSON.stringify({requestId:crypto.randomUUID(),expectedRevision:3,
   action:'ATTACH_UNVERIFIED_CLINICAL_SOURCE',
   memberRef:'test:member185',originalMemberRequestRef:'test:reason185',
   originalSubmissionRequestId:(await (await request.get(base+ref)).json()).events[0].requestId,
   claimedActorRef:'test:claimed-actor185',
   unverifiedNoteOrDocumentRef:'test:opaque185'})};
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await page.getByLabel('متن رسید بازیابی هماهنگی عضو').fill(JSON.stringify(original));
 await page.getByRole('button',{name:'بارگذاری رسید هماهنگی عضو بدون ارسال'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(panel).toHaveCount(0);
 expect(posts).toBe(0);
});
