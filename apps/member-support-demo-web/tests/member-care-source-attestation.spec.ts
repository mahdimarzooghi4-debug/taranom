import {expect,test} from '@playwright/test';
// TRN-TECH-185 — Member client must not treat forged, malformed or stale
// coordination journal responses as verified clinical/member information.
const root='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
const ZERO='00000000-0000-0000-0000-000000000000';
async function seed(request:import('@playwright/test').APIRequestContext,follow=false){
 const caseRef='test:member176-'+crypto.randomUUID().replaceAll('-','');
 const first={requestId:crypto.randomUUID(),expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:'test:member176',
  originalMemberRequestRef:'test:reason176',
  originalSubmissionRequestId:ZERO,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
 let res=await request.post(root+encodeURIComponent(caseRef)+'/commands',
  {data:first,headers:origin});
 expect(res.status()).toBe(200);
 if(follow){
  res=await request.post(root+encodeURIComponent(caseRef)+'/commands',{data:{
   ...first,requestId:crypto.randomUUID(),expectedRevision:1,
   action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
   originalSubmissionRequestId:first.requestId,
   claimedActorRef:'test:actor176',unverifiedNoteOrDocumentRef:'test:note176'
  },headers:origin});
  expect(res.status()).toBe(200);
 }
 return {caseRef,first};
}
async function open(page:import('@playwright/test').Page,ref:string){
 await page.goto('/member-demo/coordination');
 await page.getByLabel('شناسه آزمایشی پرونده').fill(ref);
 return page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'});
}
async function read(page:import('@playwright/test').Page){
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
}
test('CARE176-01 an impossible first action and a forged root UUID do not create a usable member view',async({page,request})=>{
 const s=await seed(request);
 const timeline=await open(page,s.caseRef);
 let violation='root-action';
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const res=await route.fetch();const data=await res.json();
  if(violation==='root-action')data.events[0].action='AttachUnverifiedClinicalSourceReference';
  else data.events[0].originalSubmissionRequestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(data)});
 });
 for(const kind of ['root-action','root-uuid']){
  violation=kind;await read(page);
  await expect(timeline).toHaveCount(0);
  await expect(page.getByTestId('care-status')).toContainText('منبع قابل تأیید نیست');
  await expect(page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'})).toBeDisabled();
 }
 await page.unrouteAll({behavior:'wait'});
 await read(page);
 await expect(timeline).toContainText('نسخه پرونده: 1');
 await expect(timeline).toContainText('RequestNonclinicalCoordination');
});
test('CARE176-02 forged cross-member follow-up and duplicate request IDs cannot enable manual commands',async({page,request})=>{
 const s=await seed(request,true);
 const timeline=await open(page,s.caseRef);
 let violation='scope';
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  const res=await route.fetch();const data=await res.json();
  if(violation==='scope')data.events[1].memberRef='test:other-member';
  else if(violation==='request')data.events[1].originalMemberRequestRef='test:other-request';
  else data.events[1].requestId=data.events[0].requestId;
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(data)});
 });
 for(const kind of ['scope','request','duplicate']){
  violation=kind;await read(page);
  await expect(timeline).toHaveCount(0);
  await expect(page.getByTestId('care-status')).toContainText('منبع قابل تأیید نیست');
  await expect(page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'})).toBeDisabled();
 }
 await page.unrouteAll({behavior:'wait'});
 await read(page);
 await expect(timeline).toContainText('نسخه پرونده: 2');
 await expect(timeline).toContainText('test:note176');
});
test('CARE176-03 a forged successful ACK without committed event remains uncertain until exact replay',async({page,request})=>{
 const ref='test:fake176-'+crypto.randomUUID().replaceAll('-','');
 await open(page,ref);await read(page);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 0');
 let body='',writes=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  if(route.request().method()!=='POST')return route.continue();
  body=route.request().postData()??'';writes++;
  const data=JSON.parse(body);
  const clean=await (await request.get(root+encodeURIComponent(ref))).json();
  clean.outcome='RecordedUnverified';clean.revision=1;
  clean.events=[{revision:1,action:'RequestNonclinicalCoordination',
    requestId:data.requestId,memberRef:data.memberRef,
    originalMemberRequestRef:data.originalMemberRequestRef,
    originalSubmissionRequestId:ZERO,claimedActorRef:null,
    unverifiedNoteOrDocumentRef:null}];
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(clean)});
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toContainText('ممکن است رویداد');
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 expect((await (await request.get(root+encodeURIComponent(ref))).json()).revision).toBe(0);
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await page.getByRole('button',{name:'بازپخش دستی دقیق فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
 const saved=await (await request.get(root+encodeURIComponent(ref))).json();
 expect(saved.events).toHaveLength(1);
 expect(saved.events[0].requestId).toBe(JSON.parse(body).requestId);
 expect(writes).toBe(1);
 expect(saved.clinicianAuthorized).toBe(false);
});
test('CARE176-04 accepted POST with forged subsequent GET stays locked, then GET-only recovers original UUID',async({page,request})=>{
 const ref='test:delayed176-'+crypto.randomUUID().replaceAll('-','');
 await open(page,ref);await read(page);
 let original='',posts=0,reads=0;
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  original=route.request().postData()??'';posts++;
  await route.continue();
 });
 await page.route('**/internal/demo/v1/care-coordination/*',async route=>{
  if(route.request().method()!=='GET')return route.continue();
  reads++;
  const upstream=await route.fetch();const data=await upstream.json();
  data.events[0].originalMemberRequestRef='test:forged-other-reason';
  await route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(data)});
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByTestId('care-status')).toContainText('نامشخص');
 expect(posts).toBe(1);expect(reads).toBe(1);
 await page.unrouteAll({behavior:'wait'});
 let newPosts=0;
 page.on('request',r=>{if(r.method()==='POST'&&
  r.url().includes('/care-coordination/'))newPosts++;});
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
 const saved=await (await request.get(root+encodeURIComponent(ref))).json();
 expect(saved.events).toHaveLength(1);
 expect(saved.events[0].requestId).toBe(JSON.parse(original).requestId);
 expect(newPosts).toBe(0);
});
