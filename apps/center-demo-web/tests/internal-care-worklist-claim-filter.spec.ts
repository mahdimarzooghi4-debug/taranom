import {expect,test} from '@playwright/test';
// TRN-TECH-191: claim-existence filter, not triage, duty, urgency or care approval.
const root='/internal/demo/v1/care-coordination/';
const list='/internal/demo/v1/care-coordination-worklist';
const origin={Origin:'http://127.0.0.1:4320'};
type Kind='NONE'|'CONTACT'|'SOURCE'|'BOTH';
async function fixture(request:import('@playwright/test').APIRequestContext){
 const prefix='test:filter182-'+crypto.randomUUID().replaceAll('-')+'-';
 const kinds:Kind[]=['CONTACT','NONE','SOURCE','CONTACT','BOTH','SOURCE','CONTACT','BOTH','CONTACT'];
 const refs:string[]=[];
 for(const [i,kind] of kinds.entries()){
  const caseRef=prefix+String(i).padStart(2,'0');
  const rootId=crypto.randomUUID();
  const first={requestId:rootId,expectedRevision:0,
   action:'REQUEST_NONCLINICAL_COORDINATION',
   memberRef:'test:member-filter182',originalMemberRequestRef:'test:reason-filter182',
   originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
   claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
  expect((await request.post(root+caseRef+'/commands',{data:first,headers:origin})).status()).toBe(200);
  const actions:('RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE')[]=
   kind==='NONE'?[]:kind==='BOTH'
    ?['RECORD_UNVERIFIED_COORDINATOR_CONTACT','ATTACH_UNVERIFIED_CLINICAL_SOURCE']
    :[kind==='CONTACT'?'RECORD_UNVERIFIED_COORDINATOR_CONTACT':'ATTACH_UNVERIFIED_CLINICAL_SOURCE'];
  for(const [idx,action] of actions.entries()){
   expect((await request.post(root+caseRef+'/commands',{headers:origin,
    data:{...first,requestId:crypto.randomUUID(),expectedRevision:idx+1,action,
     originalSubmissionRequestId:rootId,claimedActorRef:'test:actor-filter182',
     unverifiedNoteOrDocumentRef:action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
      ?'test:note-filter182':'test:source-filter182'}})).status()).toBe(200);
  }
  refs.push(caseRef);
 }
 return {prefix,refs};
}
const route=(after:string,filter:string,limit:number)=>
 list+'?afterCaseRef='+encodeURIComponent(after)+'&limit='+limit+'&claimFilter='+filter;
test('CARE182-01 center filter scans past nonmatches BEFORE paging; never posts on selection',async({request,page})=>{
 const {prefix,refs}=await fixture(request);
 await page.goto('/');
 const panel=page.getByTestId('center-care-worklist');
 const cursor=panel.getByLabel('شروع فهرست هماهنگی پس از پرونده');
 await cursor.fill(prefix);
 await panel.getByLabel('فیلتر ادعاهای هماهنگی').selectOption('HAS_CONTACT');
 let sent=0;page.on('request',r=>{if(r.method()==='POST')sent++;});
 await panel.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 const rows=panel.getByTestId('center-care-worklist-rows');
 await expect(rows.locator('li')).toHaveCount(4);
 for(const ref of [refs[0],refs[3],refs[4],refs[6]])await expect(rows).toContainText(ref);
 await expect(rows).not.toContainText(refs[1]);
 await expect(rows).not.toContainText(refs[2]);
 await panel.getByRole('button',{name:'صفحه بعد هماهنگی‌ها'}).click();
 await expect(rows.locator('li').first()).toContainText(refs[7]);
 await expect(rows.locator('li').nth(1)).toContainText(refs[8]);
 await rows.locator('li').filter({hasText:refs[7]})
  .getByRole('button',{name:'انتخاب پرونده هماهنگی'}).click();
 const workspace=page.getByTestId('center-care-coordination');
 await expect(workspace.getByLabel('شناسه پرونده هماهنگی مرکز')).toHaveValue(refs[7]);
 await expect(workspace.getByTestId('center-care-source')).toHaveCount(0);
 expect(sent).toBe(0);
 await workspace.getByRole('button',{name:'خواندن مستقل هماهنگی عضو'}).click();
 await expect(workspace.getByTestId('center-care-source')).toContainText('نسخه 3');
 expect(sent).toBe(0);
});
test('CARE182-02 HAS_SOURCE and HAS_BOTH exact source audit-backed counts, legacy ALL retained',async({request})=>{
 const {prefix,refs}=await fixture(request);
 const source=await request.get(route(prefix,'HAS_SOURCE',10));
 expect(source.status()).toBe(200);
 const a=await source.json();
 expect(a.contract).toBe('TRN-TECH-186');expect(a.claimFilter).toBe('HAS_SOURCE');
 expect(a.cases.map((r:{caseRef:string})=>r.caseRef).slice(0,4))
  .toEqual([refs[2],refs[4],refs[5],refs[7]]);
 const both=await (await request.get(route(prefix,'HAS_BOTH',10))).json();
 expect(both.cases.map((r:{caseRef:string})=>r.caseRef).slice(0,2)).toEqual([refs[4],refs[7]]);
 for(const r of both.cases){
  const source=await (await request.get(root+r.caseRef)).json();
  expect(r.revision).toBe(source.revision);
  expect(r.contactClaims).toBe(1);expect(r.sourceClaims).toBe(1);
 }
 const legacy=await request.get(list+'?afterCaseRef='+encodeURIComponent(prefix)+'&limit=10');
 expect(legacy.status()).toBe(200);
 const old=await legacy.json();
 expect(old.claimFilter).toBe('ALL');
 expect(old.cases.map((r:{caseRef:string})=>r.caseRef).slice(0,9)).toEqual(refs);
 for(const key of ['realMemberAuthenticated','realCoordinatorVerified',
  'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
  'referralDelivered','appointmentBooked','treatmentStarted',
  'medicalRecordAccessGranted','emergencyServiceDispatched',
  'consentGranted','walletMutated','nationalReleaseAllowed'])
   expect(a[key],key).toBe(false);
});
test('CARE182-03 malicious/duplicate filter denied; forged cross-filter page never selectable',async({request,page})=>{
 for(const url of [
  route('','CLINICALLY_URGENT',4),
  list+'?afterCaseRef=&limit=4&claimFilter=HAS_CONTACT&claimFilter=HAS_SOURCE',
  list+'?afterCaseRef=&limit=4&claimFilter=HAS_CONTACT&sort=urgent',
  list+'?afterCaseRef=&limit=4&claimFilter=has_contact',
  route('real:patient','HAS_CONTACT',4)
 ]){
  const res=await request.get(url,{failOnStatusCode:false});
  expect(res.status(),url).toBe(400);
  expect((await res.json()).nationalReleaseAllowed).toBe(false);
 }
 await page.goto('/');
 const panel=page.getByTestId('center-care-worklist');
 await panel.getByLabel('فیلتر ادعاهای هماهنگی').selectOption('HAS_CONTACT');
 await page.route('**/internal/demo/v1/care-coordination-worklist?*',async route=>{
  const r=await route.fetch(),body=await r.json();
  body.claimFilter='HAS_SOURCE';
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 let posts=0;page.on('request',r=>{if(r.method()==='POST')posts++;});
 await panel.getByRole('button',{name:'خواندن فهرست هماهنگی از ژورنال'}).click();
 await expect(panel.getByRole('status')).toContainText('فهرست قابل تأیید نیست');
 await expect(panel.getByTestId('center-care-worklist-rows')).toHaveCount(0);
 expect(posts).toBe(0);
});
