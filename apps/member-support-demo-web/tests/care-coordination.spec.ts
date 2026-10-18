import {test,expect} from '@playwright/test';
const ref=(kind:string)=>'test:'+kind+'-'+crypto.randomUUID().replaceAll('-','');
const root='/internal/demo/v1/care-coordination/';
const origin={Origin:'http://127.0.0.1:4320'};
const zero='00000000-0000-0000-0000-000000000000';
function initial(){
 return {requestId:crypto.randomUUID(),expectedRevision:0,
  action:'REQUEST_NONCLINICAL_COORDINATION',memberRef:ref('member'),
  originalMemberRequestRef:ref('request'),originalSubmissionRequestId:zero,
  claimedActorRef:null,unverifiedNoteOrDocumentRef:null};
}
function later(s:ReturnType<typeof initial>,rev:number,act:string){
 return {...s,requestId:crypto.randomUUID(),expectedRevision:rev,action:act,
  originalSubmissionRequestId:s.requestId,claimedActorRef:ref('actor'),
  unverifiedNoteOrDocumentRef:ref('note')};
}
test('CARE106-01 real PG 3-stage nonclinical coordination, event audit, no referral',async({request})=>{
 const c=ref('case'),send=(v:unknown)=>request.post(root+c+'/commands',{data:v,headers:origin});
 const one=initial();
 const a=await send(one);expect(a.status()).toBe(200);
 const two=later(one,1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 expect((await send(two)).status()).toBe(200);
 const three=later(one,2,'ATTACH_UNVERIFIED_CLINICAL_SOURCE');
 const result=await send(three);expect(result.status()).toBe(200);
 const body=await result.json();expect(body.revision).toBe(3);
 expect(body.events.map((x:{action:string})=>x.action)).toEqual([
  'RequestNonclinicalCoordination','RecordUnverifiedCoordinatorContact',
  'AttachUnverifiedClinicalSourceReference']);
 for(const key of ['realMemberAuthenticated','realCoordinatorVerified',
  'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
  'referralDelivered','appointmentBooked','treatmentStarted',
  'medicalRecordAccessGranted','emergencyServiceDispatched',
  'consentGranted','walletMutated','nationalReleaseAllowed'])
  expect(body[key],key).toBe(false);
 expect((await (await request.get(root+c)).json()).revision).toBe(3);
});
test('CARE106-02 original request idempotent; changed UUID, wrong member/source and stale fail',async({request})=>{
 const c=ref('case'),send=(v:unknown)=>request.post(root+c+'/commands',{data:v,headers:origin});
 const one=initial(),two=later(one,1,'RECORD_UNVERIFIED_COORDINATOR_CONTACT');
 expect((await send(one)).status()).toBe(200);
 let r=await send(two);expect(r.status()).toBe(200);
 r=await send(one);expect((await r.json()).outcome).toBe('Replayed');
 r=await send({...two,claimedActorRef:ref('tamper')});
 expect(r.status()).toBe(409);expect((await r.json()).outcome).toBe('ConflictingReplay');
 for(const forged of [
  {...later(one,2,'ATTACH_UNVERIFIED_CLINICAL_SOURCE'),memberRef:ref('other')},
  {...later(one,2,'ATTACH_UNVERIFIED_CLINICAL_SOURCE'),originalMemberRequestRef:ref('other')},
  {...later(one,2,'ATTACH_UNVERIFIED_CLINICAL_SOURCE'),originalSubmissionRequestId:crypto.randomUUID()}
 ]){
  r=await send(forged);expect(r.status()).toBe(409);
  expect((await r.json()).outcome).toBe('ScopeMismatch');
 }
 r=await send(later(one,1,'ATTACH_UNVERIFIED_CLINICAL_SOURCE'));
 expect(r.status()).toBe(409);expect((await r.json()).outcome).toBe('StaleRevision');
 expect((await (await request.get(root+c)).json()).revision).toBe(2);
});
test('CARE106-03 strict origin/host JSON, real scope, unknown/duplicate field refusal',async({request})=>{
 const c=ref('case'),one=initial();
 let response=await request.post(root+c+'/commands',{data:one,
  headers:{Origin:'https://forged.example'}});expect(response.status()).toBe(403);
 for(const x of [
  {...one,memberRef:'real:person'}, {...one,clinicalDiagnosis:'fake'},
  {...one,requestId:'not-guid'}, {...one,expectedRevision:-1},
  {...one,action:'ORDER_REAL_REFERRAL'},{...one,claimedActorRef:'test:staff'}
 ]){
  response=await request.post(root+c+'/commands',{data:x,headers:origin});
  expect(response.status()).toBe(400);
 }
 const dup=JSON.stringify(one).replace('"action":',
  '"action":"ATTACH_UNVERIFIED_CLINICAL_SOURCE","action":');
 response=await request.post(root+c+'/commands',{data:dup,headers:{
  ...origin,'Content-Type':'application/json'}});expect(response.status()).toBe(400);
 expect((await request.get(root+'real:person')).status()).toBe(400);
 const empty=await (await request.get(root+c)).json();
 expect(empty.revision).toBe(0);expect(empty.referralOrdered).toBe(false);
});
test('CARE106-04 Persian Chromium verified 3-step UI and lost ACK manual same-UUID replay',async({page})=>{
 await page.goto('/member-demo/coordination');
 await expect(page.getByRole('heading',{name:'هماهنگی غیرتشخیصی و پیگیری ادعایی عضو'})).toBeVisible();
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 const history=page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'});
 await expect(history).toContainText('نسخه پرونده: 0');
 let payload='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  payload=route.request().postData()??'';await route.fetch();await route.abort();
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toContainText('ممکن است رویداد');
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق فرمان قبلی'}).click();
 await expect(history).toContainText('نسخه پرونده: 1');
 await expect(page.getByTestId('care-status')).toContainText('بازپخش شد');
 expect(JSON.parse(payload).requestId).toBeTruthy();
 await page.getByRole('button',{name:'ثبت ادعای تماس هماهنگ‌کننده'}).click();
 await expect(history).toContainText('نسخه پرونده: 2');
 await page.getByRole('button',{name:'پیوست ارجاع به منبع بالینیِ تأییدنشده'}).click();
 await expect(history).toContainText('نسخه پرونده: 3');
 await expect(history).toContainText('ارجاع پزشکی، تماس واقعی، نوبت و دسترسی به پرونده:');
 expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
});

test('CARE161-01 forged successful ACK cannot clear uncertain original command; exact replay recovers',async({page})=>{
 await page.goto('/member-demo/coordination');
 await page.getByRole('button',{name:'خواندن پرونده هماهنگی'}).click();
 let sent='';
 await page.route('**/internal/demo/v1/care-coordination/**/commands',async route=>{
  sent=route.request().postData()??'';
  const r=await route.fetch();const payload=await r.json();
  payload.events[0].originalMemberRequestRef='test:forged-other-reason';
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(payload)});
 });
 await page.getByRole('button',{name:'ثبت درخواست ادعایی هماهنگی'}).click();
 await expect(page.getByRole('alert')).toContainText('ممکن است رویداد');
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق فرمان قبلی'}).click();
 await expect(page.getByTestId('care-status')).toContainText('بازپخش شد');
 await expect(page.getByRole('region',{name:'تاریخچه هماهنگی ادعایی'})).toContainText('نسخه پرونده: 1');
 expect(JSON.parse(sent).requestId).toBeTruthy();
});
