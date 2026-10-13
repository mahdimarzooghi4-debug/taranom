import {test,expect} from '@playwright/test';
const id=(name:string)=>'test:'+name+'-'+crypto.randomUUID().replaceAll('-','');
const origin={Origin:'http://127.0.0.1:4320'};
const nil='00000000-0000-0000-0000-000000000000';
function submit(caseRef:string,change:Record<string,unknown>={}){
 return {requestId:crypto.randomUUID(),expectedRevision:0,
  action:'SUBMIT_UNVERIFIED_EVIDENCE',memberRef:id('member'),
  periodStartInclusive:'2026-01-01T00:00:00Z',
  periodEndExclusive:'2026-02-01T00:00:00Z',
  evidenceRef:id('evidence'),submittedByClaimRef:id('submitter'),
  originalSubmissionRequestId:nil,reviewerClaimRef:null,reviewNoteEvidenceRef:null,
  ...change};
}
function note(s:ReturnType<typeof submit>,revision=1){
 return {...s,requestId:crypto.randomUUID(),expectedRevision:revision,
  action:'RECORD_UNVERIFIED_HUMAN_NOTE',originalSubmissionRequestId:s.requestId,
  reviewerClaimRef:id('reviewer'),reviewNoteEvidenceRef:id('review-note')};
}
const at=(c:string)=>'/internal/demo/v1/late-evidence/'+c;
test('LATE105-01 real ASP.NET/PG submission, note, exact replay, no money or grants',async({request})=>{
 const c=id('case'),s=submit(c),n=note(s);
 let r=await request.post(at(c)+'/commands',{data:s,headers:origin});
 expect(r.status()).toBe(200);
 let a=await r.json();expect(a.revision).toBe(1);
 expect(a.events).toHaveLength(1);
 r=await request.post(at(c)+'/commands',{data:n,headers:origin});
 expect(r.status()).toBe(200);a=await r.json();
 expect(a.revision).toBe(2);expect(a.events).toHaveLength(2);
 for(const flag of ['entitlementApproved','walletCredited','financialLedgerMutated',
   'realReviewerAuthenticated','officialMembershipVerified','nationalReleaseAllowed'])
  expect(a[flag],flag).toBe(false);
 r=await request.post(at(c)+'/commands',{data:s,headers:origin});
 expect(r.status()).toBe(200);expect((await r.json()).outcome).toBe('Replayed');
 expect((await (await request.get(at(c))).json()).events).toHaveLength(2);
});
test('LATE105-02 no forged scope, self review, wrong period, stale or changed UUID',async({request})=>{
 const c=id('case'),s=submit(c),n=note(s);
 const send=(data:unknown)=>request.post(at(c)+'/commands',{data,headers:origin});
 expect((await send(s)).status()).toBe(200);
 for(const [payload,outcome] of [
  [{...n,memberRef:id('different-member')},'ScopeMismatch'],
  [{...n,evidenceRef:id('different-document')},'ScopeMismatch'],
  [{...n,periodEndExclusive:'2026-03-01T00:00:00Z'},'ScopeMismatch'],
  [{...n,originalSubmissionRequestId:crypto.randomUUID()},'ScopeMismatch'],
  [{...n,reviewerClaimRef:s.submittedByClaimRef},'SameClaimedActor'],
  [{...n,expectedRevision:0},'StaleRevision'],
 ] as [Record<string,unknown>,string][]){
  const res=await send(payload);expect(res.status()).toBe(409);
  expect((await res.json()).outcome).toBe(outcome);
 }
 expect((await send(n)).status()).toBe(200);
 const conflicting=await send({...n,reviewNoteEvidenceRef:id('changed-note')});
 expect(conflicting.status()).toBe(409);
 expect((await conflicting.json()).outcome).toBe('ConflictingReplay');
 expect((await (await request.get(at(c))).json()).revision).toBe(2);
});
test('LATE105-03 strict test references, UTC period, duplicate keys and origin are rejected',async({request})=>{
 const c=id('case'),s=submit(c);
 const send=(data:unknown,Origin=origin.Origin)=>request.post(at(c)+'/commands',{
  data,headers:{Origin}});
 expect((await send(s,'https://untrusted.example')).status()).toBe(403);
 for(const bad of [
 {...s,memberRef:'real:member'},
 {...s,periodEndExclusive:'2026-01-01T00:00:00Z'},
 {...s,periodStartInclusive:'2026-01-01T00:00:00+01:00'},
 {...s,nationalId:'forbidden'},
 {...s,reviewerClaimRef:'test:reviewer'},
 {...s,expectedRevision:-1}
 ]){
  expect((await send(bad)).status()).toBe(400);
 }
 const duplicate=JSON.stringify(s).replace('"action":',
  '"action":"RECORD_UNVERIFIED_HUMAN_NOTE","action":');
 const x=await request.post(at(c)+'/commands',{data:duplicate,
  headers:{...origin,'Content-Type':'application/json'}});
 expect(x.status()).toBe(400);
 expect((await request.get(at('real:member'))).status()).toBe(400);
 const blank=await (await request.get(at(c))).json();
 expect(blank.revision).toBe(0);expect(blank.nationalReleaseAllowed).toBe(false);
});
test('LATE105-04 Persian Chromium two-step with persisted source and no browser storage',async({page})=>{
 await page.goto('/member-demo/late-evidence');
 await expect(page.getByRole('heading',{name:'ثبت آزمایشی مدرک دوره گذشته و یادداشت بررسی انسانی'})).toBeVisible();
 await page.getByRole('button',{name:'خواندن پرونده از PostgreSQL'}).click();
 const history=page.getByRole('region',{name:'تاریخچه رویدادهای آزمایشی'});
 await expect(history).toContainText('نسخه پرونده: 0');
 await page.getByRole('button',{name:'ثبت مدرک تأییدنشده',exact:true}).click();
 await expect(history).toContainText('نسخه پرونده: 1');
 await page.getByRole('button',{name:'ثبت یادداشت بررسی تأییدنشده'}).click();
 await expect(history).toContainText('نسخه پرونده: 2');
 await expect(history).toContainText('RecordUnverifiedHumanNote');
 await expect(history).toContainText('استحقاق تأییدشده، اعتبار درمانی و پرداخت:');
 expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
});
test('LATE105-05 ACK lost after PG commit only exact manual UUID replay allowed',async({page})=>{
 await page.goto('/member-demo/late-evidence');
 await page.getByRole('button',{name:'خواندن پرونده از PostgreSQL'}).click();
 let previous='';
 await page.route('**/internal/demo/v1/late-evidence/**/commands',async route=>{
  previous=route.request().postData()??'';
  await route.fetch();await route.abort();
 });
 await page.getByRole('button',{name:'ثبت مدرک تأییدنشده',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('ACK ثبت نامعلوم است');
 await expect(page.getByRole('button',{name:'ثبت مدرک تأییدنشده',exact:true})).toBeDisabled();
 await page.unrouteAll({behavior:'wait'});
 await page.getByRole('button',{name:'بازپخش دستی دقیق فرمان اولیه'}).click();
 await expect(page.getByTestId('late-status')).toContainText('همان درخواست قبلی');
 await expect(page.getByRole('region',{name:'تاریخچه رویدادهای آزمایشی'})).toContainText('نسخه پرونده: 1');
 expect(JSON.parse(previous).requestId).toBeTruthy();
});
