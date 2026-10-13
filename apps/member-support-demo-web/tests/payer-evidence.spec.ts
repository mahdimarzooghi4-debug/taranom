import {expect,test} from '@playwright/test';
const unique=(s:string)=>'test:'+s+'-'+crypto.randomUUID().replaceAll('-','');
const url='/internal/demo/v1/payer-evidence/check';
function payload(market='CORPORATE_EMPLOYER', payerKind='EMPLOYER'){
 const member=unique('member'),tenant=unique('tenant');
 return {market,payerKind,requestedCapability:'INSPECT_FUNDING_EVIDENCE',
 memberRef:member,memberTenantRef:tenant,individualWalletOwnerRef:member,
 claimedPayerRef:payerKind==='MEMBER'?member:unique('sponsor'),
 claimedPayerTenantRef:payerKind==='MEMBER'?tenant:unique('sponsor-tenant'),
 claimedContractMemberRef:member,claimedContractTenantRef:tenant,
 currentContract:'CONFIRMED',payerAllowedByContract:'CONFIRMED',
 membershipFee:'CONFIRMED',independentPayment:'CONFIRMED'};
}
test('PAYER-103-01: real HTTP three-market combinations claim funding only, never activate',async({request})=>{
 for(const [market,payer] of [
  ['DIRECT_PUBLIC','MEMBER'],['SUPPORT_INSTITUTION','MEMBER'],
  ['SUPPORT_INSTITUTION','SUPPORT_INSTITUTION'],
  ['CORPORATE_EMPLOYER','MEMBER'],['CORPORATE_EMPLOYER','EMPLOYER']
 ]){
  const res=await request.post(url,{data:payload(market,payer),
    headers:{Origin:'http://127.0.0.1:4320'}});
  expect(res.status(),market+' '+payer).toBe(200);
  const dto=await res.json();
  expect(dto.contract).toBe('TRN-TECH-112');
  expect(dto.outcome).toBe('FundingEvidencePresentOnly');
  expect(dto.untrustedEvidenceClaimsOnly).toBe(true);
  expect(dto.individualOwnsMembership).toBe(true);
  expect(dto.individualOwnsHealthcareWallet).toBe(true);
  for(const flag of ['paymentVerified','contractVerified','membershipActivated',
    'treatmentCreditAllocated','sponsorClinicalAccessAuthorized',
    'sponsorWalletControlAuthorized','realMemberAuthenticated',
    'clinicalConsentGranted','nationalReleaseAllowed'])expect(dto[flag],flag).toBe(false);
 }
});
test('PAYER-103-02: source-side scope/tenant spoof and unauthorized market payers denied',async({request})=>{
 const base=payload();
 for(const altered of [
  {...base,individualWalletOwnerRef:unique('another-owner')},
  {...base,claimedContractMemberRef:unique('another-member')},
  {...base,claimedContractTenantRef:unique('another-tenant')},
  {...base,claimedPayerRef:base.memberRef},
  payload('DIRECT_PUBLIC','EMPLOYER'),payload('CORPORATE_EMPLOYER','SUPPORT_INSTITUTION')
 ]){
  const res=await request.post(url,{data:altered,headers:{Origin:'http://127.0.0.1:4320'}});
  expect(res.status()).toBe(403);
  const dto=await res.json();
  expect(dto.outcome).toBe('ContractScopeDenied');
  expect(dto.membershipActivated).toBe(false);
  expect(dto.nationalReleaseAllowed).toBe(false);
 }
});
test('PAYER-103-03: even all asserted confirmed, sponsor/member restricted capability rejected',async({request})=>{
 const sponsor=payload();
 for(const act of ['READ_HEALTH_RECORD','CONTROL_TREATMENT_CREDIT',
  'CHANGE_CLINICAL_CONSENT','READ_PRIVATE_WALLET','WITHDRAW_CASH']){
  const res=await request.post(url,{data:{...sponsor,requestedCapability:act},
    headers:{Origin:'http://127.0.0.1:4320'}});
  expect(res.status()).toBe(403);
  expect((await res.json()).outcome).toBe('PayerPrivilegeDenied');
 }
 for(const act of ['ACTIVATE_MEMBER_ACCOUNT','READ_HEALTH_RECORD']){
  const res=await request.post(url,{data:{...payload('DIRECT_PUBLIC','MEMBER'),
    requestedCapability:act},headers:{Origin:'http://127.0.0.1:4320'}});
  expect(res.status()).toBe(403);
  expect((await res.json()).outcome).toBe('SeparateAuthorizationRequired');
 }
 const cash=await request.post(url,{data:{...payload('DIRECT_PUBLIC','MEMBER'),
  requestedCapability:'WITHDRAW_CASH'},headers:{Origin:'http://127.0.0.1:4320'}});
 expect((await cash.json()).outcome).toBe('PayerPrivilegeDenied');
});
test('PAYER-103-04: missing and rejected claims never become an accepted payment',async({request})=>{
 const original=payload();
 for(const [field,outcome] of [
   ['currentContract','EvidenceMissing'],['membershipFee','EvidenceMissing'],
   ['independentPayment','EvidenceRejected']
 ]){
   const res=await request.post(url,{data:{...original,[field]:field==='independentPayment'?
     'REJECTED':'MISSING'},headers:{Origin:'http://127.0.0.1:4320'}});
   expect(res.status()).toBe(409);
   const dto=await res.json();
   expect(dto.outcome).toBe(outcome);
   expect(dto.paymentVerified).toBe(false);
 }
});
test('PAYER-103-05: untrusted origin, malformed, real refs, extra and duplicated keys fail closed',async({request})=>{
 const good=payload();
 const post=(data:unknown,Origin='http://127.0.0.1:4320')=>
  request.post(url,{data,headers:{Origin}});
 expect((await post(good,'https://forged.example')).status()).toBe(403);
 for(const bad of [
   {...good,memberRef:'real:person'},
   {...good,memberAuthenticated:true},
   {...good,currentContract:'SIGNED_REAL'},
   {...good,market:'FOURTH_MARKET'},
   {...good,claimedPayerRef:'test:non\nidentity'},
 ]){
  const res=await post(bad);
  expect(res.status()).toBe(400);
  expect((await res.json()).nationalReleaseAllowed).toBe(false);
 }
 const dup=JSON.stringify(good).replace('"market":',
   '"market":"DIRECT_PUBLIC","market":');
 const duplicate=await request.post(url,{data:dup,
  headers:{Origin:'http://127.0.0.1:4320','Content-Type':'application/json'}});
 expect(duplicate.status()).toBe(400);
 expect((await request.get(url)).status()).toBeGreaterThanOrEqual(400);
 const fakeHost=await request.post(url,{data:good,
  headers:{Origin:'http://127.0.0.1:4320',Host:'hostile.invalid:4320'}});
 expect(fakeHost.status()).toBe(403);
});
test('PAYER-103-06: Chromium UI has real evaluated server result and clears stale result on change/failure',async({page})=>{
 await page.goto('/member-demo/payer');
 await expect(page.getByRole('heading',{name:'بررسی آزمایشی رابطه پرداخت‌کننده، عضو و قرارداد در سه بازار'})).toBeVisible();
 for(const label of ['ادعای قرارداد جاری با همین عضو و پرداخت‌کننده',
  'ادعای مجازبودن پرداخت‌کننده در قرارداد','ادعای وجود مدرک حق اشتراک',
  'ادعای تأیید مستقل منبع پرداخت']){
  await page.getByLabel(label).selectOption('CONFIRMED');
 }
 await page.getByRole('button',{name:'اجرای دستی بازرسی شواهد در .NET'}).click();
 await expect(page.getByTestId('payer-evidence-result')).toContainText('FundingEvidencePresentOnly');
 await expect(page.getByTestId('payer-evidence-result')).toContainText('DENIED_BY_DEFAULT');
 await page.getByLabel('مالک کیف پول شخصی').fill(unique('wrong-owner'));
 await expect(page.getByTestId('payer-evidence-result')).toHaveCount(0);
 await page.getByRole('button',{name:'اجرای دستی بازرسی شواهد در .NET'}).click();
 await expect(page.getByTestId('payer-evidence-result')).toContainText('ContractScopeDenied');
 await page.getByLabel('بازار آزمایشی').selectOption('SUPPORT_INSTITUTION');
 await expect(page.getByTestId('payer-evidence-result')).toHaveCount(0);
 await page.route('**/internal/demo/v1/payer-evidence/check',route=>route.abort());
 await page.getByRole('button',{name:'اجرای دستی بازرسی شواهد در .NET'}).click();
 await expect(page.getByTestId('payer-evidence-status')).toContainText('نتیجه غیرقابل‌تأیید');
 await expect(page.getByTestId('payer-evidence-result')).toHaveCount(0);
 expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
});
