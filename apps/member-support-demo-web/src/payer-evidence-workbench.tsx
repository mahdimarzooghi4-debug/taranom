import {useState} from 'react';

type Market='DIRECT_PUBLIC'|'SUPPORT_INSTITUTION'|'CORPORATE_EMPLOYER';
type Payer='MEMBER'|'SUPPORT_INSTITUTION'|'EMPLOYER';
type Capability='INSPECT_FUNDING_EVIDENCE'|'ACTIVATE_MEMBER_ACCOUNT'|
  'READ_HEALTH_RECORD'|'CONTROL_TREATMENT_CREDIT'|'CHANGE_CLINICAL_CONSENT'|
  'READ_PRIVATE_WALLET'|'WITHDRAW_CASH';
type Evidence='MISSING'|'REJECTED'|'CONFIRMED';
type RequestData={
  market:Market;payerKind:Payer;requestedCapability:Capability;
  memberRef:string;memberTenantRef:string;individualWalletOwnerRef:string;
  claimedPayerRef:string;claimedPayerTenantRef:string;claimedContractMemberRef:string;
  claimedContractTenantRef:string;
  currentContract:Evidence;payerAllowedByContract:Evidence;
  membershipFee:Evidence;independentPayment:Evidence;
};
type Result={contract:'TRN-TECH-112';syntheticOnly:true;
 untrustedEvidenceClaimsOnly:true;outcome:string;market:Market;
 payerKind:Payer;requestedCapability:Capability;
 individualOwnsMembership:true;individualOwnsHealthcareWallet:true;
 sponsorClinicalAccessAuthorized:false;sponsorWalletControlAuthorized:false;
 contractVerified:false;paymentVerified:false;membershipActivated:false;
 treatmentCreditAllocated:false;clinicalConsentGranted:false;
 realMemberAuthenticated:false;nationalReleaseAllowed:false;
};
const marketOptions:{id:Market;name:string}[]=[
 {id:'DIRECT_PUBLIC',name:'فردی'}, {id:'SUPPORT_INSTITUTION',name:'نهاد حمایتی'},
 {id:'CORPORATE_EMPLOYER',name:'سازمان و کارفرما'}
];
const actions:{id:Capability;name:string}[]=[
 {id:'INSPECT_FUNDING_EVIDENCE',name:'بازرسی ادعای تأمین حق اشتراک'},
 {id:'ACTIVATE_MEMBER_ACCOUNT',name:'درخواست فعال‌سازی عضویت (باید رد شود)'},
 {id:'READ_HEALTH_RECORD',name:'دسترسی به پرونده سلامت (باید رد شود)'},
 {id:'CONTROL_TREATMENT_CREDIT',name:'کنترل اعتبار درمان (باید رد شود)'},
 {id:'CHANGE_CLINICAL_CONSENT',name:'تغییر رضایت بالینی (باید رد شود)'},
 {id:'READ_PRIVATE_WALLET',name:'مشاهده کیف پول خصوصی (باید رد شود)'},
 {id:'WITHDRAW_CASH',name:'برداشت نقدی (باید رد شود)'}
];
const checks:{id:keyof Pick<RequestData,'currentContract'|'payerAllowedByContract'|
  'membershipFee'|'independentPayment'>;name:string}[]=[
 {id:'currentContract',name:'ادعای قرارداد جاری با همین عضو و پرداخت‌کننده'},
 {id:'payerAllowedByContract',name:'ادعای مجازبودن پرداخت‌کننده در قرارداد'},
 {id:'membershipFee',name:'ادعای وجود مدرک حق اشتراک'},
 {id:'independentPayment',name:'ادعای تأیید مستقل منبع پرداخت'}
];
const initial:RequestData={
 market:'CORPORATE_EMPLOYER',payerKind:'EMPLOYER',
 requestedCapability:'INSPECT_FUNDING_EVIDENCE',
 memberRef:'test:sample-member',memberTenantRef:'test:sample-tenant',
 individualWalletOwnerRef:'test:sample-member',
 claimedPayerRef:'test:sample-employer',claimedPayerTenantRef:'test:employer-tenant',
 claimedContractMemberRef:'test:sample-member',
 claimedContractTenantRef:'test:sample-tenant',
 currentContract:'MISSING',payerAllowedByContract:'MISSING',
 membershipFee:'MISSING',independentPayment:'MISSING'
};
function valid(v:unknown,x:RequestData):v is Result{
 if(!v||typeof v!=='object')return false;
 const o=v as Partial<Result>;
 return o.contract==='TRN-TECH-112'&&o.syntheticOnly===true&&
   o.untrustedEvidenceClaimsOnly===true&&
   o.market===x.market&&o.payerKind===x.payerKind&&
   o.requestedCapability===x.requestedCapability&&
   ['FundingEvidencePresentOnly','EvidenceMissing','EvidenceRejected',
     'ContractScopeDenied','PayerPrivilegeDenied',
     'SeparateAuthorizationRequired','InvalidInput'].includes(String(o.outcome))&&
   o.individualOwnsMembership===true&&o.individualOwnsHealthcareWallet===true&&
   o.sponsorClinicalAccessAuthorized===false&&o.sponsorWalletControlAuthorized===false&&
   o.contractVerified===false&&o.paymentVerified===false&&
   o.membershipActivated===false&&o.treatmentCreditAllocated===false&&
   o.clinicalConsentGranted===false&&o.realMemberAuthenticated===false&&
   o.nationalReleaseAllowed===false;
}
export function PayerEvidenceWorkbench(){
 const [values,setValues]=useState<RequestData>(initial);
 const [result,setResult]=useState<Result|null>(null);
 const [message,setMessage]=useState('هیچ مدرک، قرارداد یا پرداخت واقعی بررسی نشده است.');
 const [pending,setPending]=useState(false);
 const set=<K extends keyof RequestData>(key:K,value:RequestData[K])=>{
   setValues(v=>({...v,[key]:value}));setResult(null);
   setMessage('ورودی تغییر کرده است؛ نتیجه قبلی معتبر نیست. برای بررسی دستی کلیک کنید.');
 };
 const chooseMarket=(market:Market)=>{
   const payerKind:Payer=market==='DIRECT_PUBLIC'?'MEMBER':
     market==='SUPPORT_INSTITUTION'?'SUPPORT_INSTITUTION':'EMPLOYER';
   setValues(v=>({...v,market,payerKind,
     claimedPayerRef:payerKind==='MEMBER'?v.memberRef:
       payerKind==='SUPPORT_INSTITUTION'?'test:sample-support':'test:sample-employer',
     claimedPayerTenantRef:payerKind==='MEMBER'?v.memberTenantRef:
       payerKind==='SUPPORT_INSTITUTION'?'test:support-tenant':'test:employer-tenant'}));
   setResult(null);setMessage('بازار تغییر کرد؛ هنوز هیچ حقی احراز نشده است.');
 };
 async function inspect(){
   if(pending)return;
   const submitted={...values};
   setResult(null);setPending(true);setMessage('در حال اجرای بازرسی آزمایشی در .NET...');
   try {
     const response=await fetch('/internal/demo/v1/payer-evidence/check',{
       method:'POST',headers:{'Content-Type':'application/json'},
       credentials:'omit',cache:'no-store',body:JSON.stringify(submitted)
     });
     const data:unknown=await response.json();
     if(!valid(data,submitted) ||
        ![200,403,409].includes(response.status) ||
        (response.status===200)!==(data.outcome==='FundingEvidencePresentOnly'))
       throw Error('INVALID_SERVER_RESPONSE');
     setResult(data);setMessage('فقط نتیجه محدودکننده قانون .NET خوانده شد؛ هیچ حقی فعال نشده است.');
   } catch {
     setResult(null);setMessage('نتیجه غیرقابل‌تأیید است؛ دسترسی، عضویت و پرداخت همگی غیرفعال‌اند.');
   } finally{setPending(false);}
 }
 return <section className="payer-workbench" aria-label="بازرسی آزمایشی پرداخت‌کننده سه بازار">
  <h1>بررسی آزمایشی رابطه پرداخت‌کننده، عضو و قرارداد در سه بازار</h1>
  <p className="payer-risk">این فرم صرفاً مقادیر ساختگیِ ادعاشده را به قانون موجود .NET
   می‌فرستد. هیچ قرارداد یا پرداختی احراز نمی‌شود و هیچ عضویت، کیف پول، بیمه یا
   دسترسی بالینی ایجاد نمی‌شود.</p>
  <div className="fields">
   <label>بازار آزمایشی
    <select value={values.market} onChange={e=>chooseMarket(e.target.value as Market)}>
     {marketOptions.map(m=><option key={m.id} value={m.id}>{m.name}</option>)}
    </select></label>
   <label>نوع پرداخت‌کننده
    <select value={values.payerKind} onChange={e=>{
      const value=e.target.value as Payer;
      setValues(v=>({...v,payerKind:value,
        claimedPayerRef:value==='MEMBER'?v.memberRef:'test:sample-sponsor',
        claimedPayerTenantRef:value==='MEMBER'?v.memberTenantRef:'test:sponsor-tenant'}));
      setResult(null);setMessage('نوع پرداخت‌کننده تغییر کرده است.');
    }}>
     <option value="MEMBER">خود عضو</option>
     <option value="SUPPORT_INSTITUTION">نهاد حمایتی</option>
     <option value="EMPLOYER">کارفرما</option>
    </select></label>
   <label>درخواست آزمایشی
    <select value={values.requestedCapability}
      onChange={e=>set('requestedCapability',e.target.value as Capability)}>
     {actions.map(a=><option key={a.id} value={a.id}>{a.name}</option>)}
    </select></label>
  </div>
  <fieldset><legend>شناسه‌های غیرواقعیِ عضو، پرداخت‌کننده و قرارداد</legend>
   <div className="fields">
    {([
     ['memberRef','شناسه عضو آزمایشی'],['memberTenantRef','محدوده عضو'],
     ['individualWalletOwnerRef','مالک کیف پول شخصی'],
     ['claimedPayerRef','شناسه پرداخت‌کننده ادعایی'],
     ['claimedPayerTenantRef','محدوده پرداخت‌کننده'],
     ['claimedContractMemberRef','عضو مندرج در قرارداد ادعایی'],
     ['claimedContractTenantRef','محدوده قرارداد ادعایی']
    ] as const).map(([field,label])=><label key={field}>{label}
      <input dir="ltr" maxLength={128} value={values[field]}
       onChange={e=>set(field,e.target.value)} />
    </label>)}
   </div>
  </fieldset>
  <fieldset><legend>وضعیت شواهد ادعایی؛ هر «تأییدشده» صرفاً ورودیِ غیرقابل‌اعتماد است</legend>
   <div className="fields">
    {checks.map(c=><label key={c.id}>{c.name}
     <select value={values[c.id]}
      onChange={e=>set(c.id,e.target.value as Evidence)}>
      <option value="MISSING">موجود نیست</option>
      <option value="REJECTED">رد شده</option>
      <option value="CONFIRMED">ادعای تأیید</option>
     </select>
    </label>)}
   </div>
  </fieldset>
  <button disabled={pending} type="button" onClick={()=>void inspect()}>
   اجرای دستی بازرسی شواهد در .NET
  </button>
  <p role="status" data-testid="payer-evidence-status">{message}</p>
  {result&&<div className="payer-result" data-testid="payer-evidence-result" role="region"
    aria-label="نتیجه بازرسی آزمایشی">
   <p>نتیجه قانون: <strong>{result.outcome}</strong></p>
   <p>مالک عضویت و کیف پول درمانی: <strong>فقط فرد</strong></p>
   <p>تأیید قرارداد و پرداخت واقعی: <strong>خیر</strong></p>
   <p>فعال‌سازی عضویت، اعتبار درمانی، رضایت و دسترسی حامی: <strong>خیر</strong></p>
   <p>وضعیت انتشار: <strong>DENIED_BY_DEFAULT</strong></p>
   {result.outcome==='FundingEvidencePresentOnly'&&<p className="payer-risk">
     ادعای تکمیل چهار شواهد حتی به‌معنای پرداخت پذیرفته‌شده یا عضویت فعال نیست.
    </p>}
  </div>}
 </section>;
}
