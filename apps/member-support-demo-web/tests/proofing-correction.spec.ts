import {test,expect} from '@playwright/test';
function testRef(){return 'test:proof-'+crypto.randomUUID().replaceAll('-','');}
const root='/internal/demo/v1/proofing/';
function cmd(caseVersion:number,identity:number,action:string,observation:string){
 const isCorrection=action==='APPLICANT_CORRECTED_IDENTITY';
 return {
  requestId:crypto.randomUUID(),expectedCaseRevision:caseVersion,
  expectedIdentityRevision:identity,action,
  observation:isCorrection?'NONE':observation,
  providerAndEvidenceValidated:isCorrection?'MISSING':'CONFIRMED',
  applicantLinkValidated:isCorrection?'MISSING':'CONFIRMED',
  evidenceCurrent:isCorrection?'MISSING':'CONFIRMED',
  officialActivationStatusKnown:'CONFIRMED',officiallyActivated:false,
  identityDataChangedSinceEvidence:false
 };
}
test('PROOF104-01 real Chromium correction cycle records exact three versions, no rights',async({page,request})=>{
 await page.goto('/member-demo/proofing');
 const ref=await page.getByLabel('شناسه تصادفی پرونده آزمایشی').inputValue();
 expect(ref.startsWith('test:')).toBe(true);
 await page.getByRole('button',{name:'خواندن پرونده آزمایشی از PostgreSQL'}).click();
 const history=page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'});
 await expect(history).toContainText('نسخه پرونده: 0');
 await page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'}).click();
 await expect(history).toContainText('CorrectApplicantInformationThenReverify');
 await expect(history).toContainText('نسخه پرونده: 1');
 await page.getByRole('button',{name:'ثبت صرفاً اعلام اصلاح (بدون داده هویتی)'}).click();
 await expect(history).toContainText('نسخه پرونده: 2');
 await expect(history).toContainText('نسخه اطلاعات هویتی فرضی: 1');
 await expect(history).toContainText('AwaitingAuthoritativeVerification');
 await page.getByRole('button',{name:'ثبت فرضی نتیجه احراز مجدد'}).click();
 await expect(history).toContainText('نسخه پرونده: 3');
 await expect(history).toContainText('ValidatedProofingEvidenceOnly');
 const original=await (await request.get(root+ref)).json();
 expect(original.events.map((v:{identityRevision:number})=>v.identityRevision)).toEqual([0,1,1]);
 expect(original.events).toHaveLength(3);
 for(const k of ['realIdentityVerified','realIdpContacted',
 'officialMembershipActivated','waitingPeriodStarted','walletCredited',
 'clinicalRecordAccessGranted','nationalReleaseAllowed'])expect(original[k],k).toBe(false);
 await page.reload();
 await expect(page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'})).toHaveCount(0);
});
test('PROOF104-02 exact request replay, conflicting payload, stale and active correction denied',async({request})=>{
 const ref=testRef(),url=root+ref+'/commands';
 const post=(value:unknown)=>request.post(url,{data:value,headers:{Origin:'http://127.0.0.1:4320'}});
 const first=cmd(0,0,'OBSERVE_PROOFING','DEFINITIVE_MISMATCH');
 let res=await post(first);expect(res.status()).toBe(200);
 expect((await res.json()).review).toBe('CorrectApplicantInformationThenReverify');
 res=await post(first);expect(res.status()).toBe(200);
 expect((await res.json()).outcome).toBe('Replayed');
 res=await post({...first,observation:'VERIFIED_MATCH'});
 expect(res.status()).toBe(409);expect((await res.json()).outcome).toBe('ConflictingReplay');
 res=await post(cmd(0,0,'APPLICANT_CORRECTED_IDENTITY','NONE'));
 expect(res.status()).toBe(409);expect((await res.json()).outcome).toBe('StaleCaseRevision');
 res=await post({...cmd(1,0,'APPLICANT_CORRECTED_IDENTITY','NONE'),
  officiallyActivated:true});
 expect(res.status()).toBe(409);
 expect((await res.json()).outcome).toBe('NotApplicable');
 const correction=cmd(1,0,'APPLICANT_CORRECTED_IDENTITY','NONE');
 res=await post(correction);expect(res.status()).toBe(200);
 const snap=await res.json();expect(snap.caseRevision).toBe(2);expect(snap.identityRevision).toBe(1);
 res=await post(cmd(2,0,'OBSERVE_PROOFING','VERIFIED_MATCH'));
 expect(res.status()).toBe(409);expect((await res.json()).outcome).toBe('StaleIdentityRevision');
 expect((await (await request.get(root+ref)).json()).events).toHaveLength(2);
});
test('PROOF104-03 absent/invalid/untrusted refs and payload fail without accepted event',async({request})=>{
 const ref=testRef(),base=root+ref,cmd0=cmd(0,0,'OBSERVE_PROOFING','DEFINITIVE_MISMATCH');
 const post=(data:unknown,origin='http://127.0.0.1:4320')=>
  request.post(base+'/commands',{data,headers:{Origin:origin}});
 expect((await post(cmd0,'https://attacker.invalid')).status()).toBe(403);
 for(const bad of [
 {...cmd0,nationalId:'real person'}, {...cmd0,providerAndEvidenceValidated:'TRUST_ME'},
 {...cmd0,requestId:'not-a-uuid'},{...cmd0,officialActivationStatusKnown:'UNVERIFIED'},
 {...cmd0,observation:'NONE'}
 ]){
  let res=await post(bad);expect(res.status()).toBe(400);
  expect((await res.json()).officialMembershipActivated).toBe(false);
 }
 const dup=JSON.stringify(cmd0).replace('"action":',
  '"action":"APPLICANT_CORRECTED_IDENTITY","action":');
 const dupe=await request.post(base+'/commands',{data:dup,headers:{
  Origin:'http://127.0.0.1:4320','Content-Type':'application/json'}});
 expect(dupe.status()).toBe(400);
 const cross=await request.post(root+'real:person/commands',{data:cmd0,
  headers:{Origin:'http://127.0.0.1:4320'}});
 expect(cross.status()).toBe(400);
 expect((await (await request.get(base)).json()).caseRevision).toBe(0);
 expect((await request.get(root+'real:person')).status()).toBe(400);
});
test('PROOF104-04 Chromium lost ACK preserves exact requestId for manual replay',async({page})=>{
 await page.goto('/member-demo/proofing');
 await page.getByRole('button',{name:'خواندن پرونده آزمایشی از PostgreSQL'}).click();
 let captured='';
 await page.route('**/internal/demo/v1/proofing/**/commands',async route=>{
  captured=route.request().postData()??'';
  await route.fetch(); // Commit on server, then browser loses the ACK.
  await route.abort();
 });
 await page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'}).click();
 await expect(page.getByRole('alert')).toContainText('نتیجه قبلی نامطمئن است');
 await expect(page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'})).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق همان درخواست'}).click();
 await expect(page.getByTestId('proofing-status')).toContainText('درخواست اصلی بدون ثبت مجدد');
 await expect(page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'})).toContainText('نسخه پرونده: 1');
 expect(JSON.parse(captured).requestId).toBeTruthy();
 expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
});

test('PROOF161-01 stored request UUID anchors ACK and forged response stays uncertain',async({page,request})=>{
 await page.goto('/member-demo/proofing');
 const caseRef=await page.getByLabel('شناسه تصادفی پرونده آزمایشی').inputValue();
 await page.getByRole('button',{name:'خواندن پرونده آزمایشی از PostgreSQL'}).click();
 let sent='';
 await page.route('**/internal/demo/v1/proofing/**/commands',async route=>{
  sent=route.request().postData()??'';
  const r=await route.fetch();const payload=await r.json();
  payload.events[0].requestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(payload)});
 });
 await page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'}).click();
 await expect(page.getByRole('alert')).toContainText('نتیجه قبلی نامطمئن');
 await expect(page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'})).toHaveCount(0);
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق همان درخواست'}).click();
 await expect(page.getByTestId('proofing-status')).toContainText('درخواست اصلی بدون ثبت مجدد');
 await expect(page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'})).toContainText('نسخه پرونده: 1');
 const body=await (await request.get(root+caseRef)).json();
 expect(body.events[0].requestId).toBe(JSON.parse(sent).requestId);
});

test('PROOF162-01 manual GET reconciles lost command ACK without another POST',async({page,request})=>{
 await page.goto('/member-demo/proofing');
 const ref=await page.getByLabel('شناسه تصادفی پرونده آزمایشی').inputValue();
 await page.getByRole('button',{name:'خواندن پرونده آزمایشی از PostgreSQL'}).click();
 let original='';
 await page.route('**/internal/demo/v1/proofing/**/commands',async route=>{
  original=route.request().postData()??'';
  await route.fetch();await route.abort();
 });
 await page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'}).click();
 await expect(page.getByRole('alert')).toContainText('نتیجه قبلی نامطمئن');
 await page.unrouteAll({behavior:'wait'});
 let posts=0;page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/proofing/'))posts++;});
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByTestId('proofing-status')).toContainText('بدون بازپخش یا ثبت جدید');
 await expect(page.getByRole('alert')).toHaveCount(0);
 await expect(page.getByRole('region',{name:'تاریخچه ممیزی آزمایشی'})).toContainText('نسخه پرونده: 1');
 expect(posts).toBe(0);
 const x=await (await request.get('/internal/demo/v1/proofing/'+ref)).json();
 expect(x.events).toHaveLength(1);
 expect(x.events[0].requestId).toBe(JSON.parse(original).requestId);
 expect(x.realIdentityVerified).toBe(false);
});
test('PROOF162-02 forged READ event UUID keeps uncertain state and blocks writes',async({page})=>{
 await page.goto('/member-demo/proofing');
 await page.getByRole('button',{name:'خواندن پرونده آزمایشی از PostgreSQL'}).click();
 await page.route('**/internal/demo/v1/proofing/**/commands',async route=>{
  await route.fetch();await route.abort();
 });
 await page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'}).click();
 await expect(page.getByRole('alert')).toContainText('نتیجه قبلی نامطمئن');
 await page.unrouteAll({behavior:'wait'});
 await page.route('**/internal/demo/v1/proofing/*',async route=>{
  const r=await route.fetch();const x=await r.json();
  x.events[0].requestId=crypto.randomUUID();
  await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(x)});
 });
 await page.getByRole('button',{name:'بررسی فقط‌خواندنی فرمان قبلی'}).click();
 await expect(page.getByTestId('proofing-status')).toContainText('قفل و بازپخش دستی حفظ شد');
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByRole('button',{name:'ثبت فرضی عدم تطابق قطعی'})).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق همان درخواست'}).click();
 await expect(page.getByTestId('proofing-status')).toContainText('درخواست اصلی بدون ثبت مجدد');
});
