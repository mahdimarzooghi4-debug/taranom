import {expect,test} from '@playwright/test';
// TRN-TECH-192: same-page manual delta of audit-rebuilt synthetic claims.
// No polling, delivery notice, clinical task, authority or automatic write.
const journal='/internal/demo/v1/care-coordination/';
const headers={Origin:'http://127.0.0.1:4320'};
const initial=(id:string)=>({
 requestId:id,expectedRevision:0,action:'REQUEST_NONCLINICAL_COORDINATION',
 memberRef:'test:member183',originalMemberRequestRef:'test:request183',
 originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
 claimedActorRef:null,unverifiedNoteOrDocumentRef:null
});
async function seed(request:import('@playwright/test').APIRequestContext,refs:string[]){
 const roots:string[]=[];
 for(const ref of refs){
  const id=crypto.randomUUID();
  expect((await request.post(journal+ref+'/commands',
   {headers,data:initial(id)})).status()).toBe(200);
  roots.push(id);
 }
 return roots;
}
async function append(request:import('@playwright/test').APIRequestContext,
 ref:string,rootId:string,revision:number,
 action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE'){
 const body={...initial(crypto.randomUUID()),expectedRevision:revision,action,
  originalSubmissionRequestId:rootId,claimedActorRef:'test:claimed183',
  unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
   ?'test:note183':'test:clinical-source183'};
 expect((await request.post(journal+ref+'/commands',{headers,data:body})).status()).toBe(200);
}
function refs(){
 const prefix='test:care183-'+crypto.randomUUID().replaceAll('-')+'-';
 return {prefix,cases:['00','02','04','06','08'].map(s=>prefix+s)};
}
async function start(page:import('@playwright/test').Page,prefix:string){
 await page.goto('/');
 const panel=page.getByTestId('center-care-worklist');
 await panel.getByLabel('شروع فهرست هماهنگی پس از پرونده').fill(prefix);
 await panel.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(panel.getByTestId('center-care-worklist-rows').locator('li')).toHaveCount(4);
 return panel;
}
const check='بررسی دستی تغییرات پرونده‌های همین صفحه';
test('CARE183-01 two independently committed claim types show exact same-page additions on human GET',async({page,request})=>{
 const {prefix,cases}=refs(),roots=await seed(request,cases);
 const panel=await start(page,prefix);
 await expect(panel.getByTestId('center-care-worklist-activity')).toHaveCount(0);
 await append(request,cases[1],roots[1],1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 await append(request,cases[2],roots[2],1,'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 let posts=0;
 page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/care-coordination/'))posts++;});
 await panel.getByRole('button',{name:check}).click();
 const report=panel.getByTestId('center-care-worklist-activity');
 await expect(report).toContainText('2 پرونده');
 const lines=report.locator('li');
 await expect(lines).toHaveCount(2);
 await expect(lines.nth(0)).toContainText(cases[1]);
 await expect(lines.nth(0)).toContainText('ادعای تماس جدید 1');
 await expect(lines.nth(0)).toContainText('مرجع بالینی تأییدنشده جدید 0');
 await expect(lines.nth(1)).toContainText(cases[2]);
 await expect(lines.nth(1)).toContainText('ادعای تماس جدید 0');
 await expect(lines.nth(1)).toContainText('مرجع بالینی تأییدنشده جدید 1');
 expect(posts).toBe(0);
 await panel.getByRole('button',{name:check}).click();
 await expect(report).toContainText('0 پرونده');
 await expect(lines).toHaveCount(0);
 const snapshot=await (await request.get(journal+cases[2])).json();
 expect(snapshot.events[1].action).toBe('AttachUnverifiedClinicalSourceReference');
 expect(snapshot.clinicalSourceVerified).toBe(false);
 expect(snapshot.nationalReleaseAllowed).toBe(false);
 await panel.getByLabel('فیلتر ادعاهای هماهنگی').selectOption('HAS_SOURCE');
 await expect(report).toHaveCount(0);
 await expect(panel.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 expect(posts).toBe(0);
});
test('CARE183-02 forged but contract-valid count rollback cannot be presented as an activity refresh',async({page,request})=>{
 const {prefix,cases}=refs(),roots=await seed(request,cases);
 await append(request,cases[0],roots[0],1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 const panel=await start(page,prefix);
 await expect(panel.getByTestId('center-care-worklist-rows')).toContainText('ادعاهای تماس 1');
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  const response=await route.fetch(),data=await response.json();
  const row=data.cases.find((x:{caseRef:string})=>x.caseRef===cases[0]);
  row.revision=1;row.contactClaims=0;row.sourceClaims=0;
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(data)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await panel.getByRole('button',{name:check}).click();
 await expect(panel.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(panel.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-worklist-activity')).toHaveCount(0);
 expect(posts).toBe(0);
 const source=await (await request.get(journal+cases[0])).json();
 expect(source.revision).toBe(2);
 expect(source.nationalReleaseAllowed).toBe(false);
});
test('CARE183-03 keyset membership drift fails closed, human may reload a NEW baseline',async({page,request})=>{
 const {prefix,cases}=refs();await seed(request,cases);
 const panel=await start(page,prefix);
 const inserted=prefix+'01';
 await seed(request,[inserted]); // shifts the first four rows, not an old-case event.
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await panel.getByRole('button',{name:check}).click();
 await expect(panel.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(panel.getByTestId('center-care-worklist-activity')).toHaveCount(0);
 await expect(panel.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 await panel.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 const rows=panel.getByTestId('center-care-worklist-rows').locator('li');
 await expect(rows).toHaveCount(4);
 await expect(rows.nth(0)).toContainText(cases[0]);
 await expect(rows.nth(1)).toContainText(inserted);
 await expect(rows.nth(2)).toContainText(cases[1]);
 expect(posts).toBe(0);
});
