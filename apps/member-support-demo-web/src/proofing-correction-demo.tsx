import {useState} from 'react';

type Review='AwaitingAuthoritativeVerification'|
 'CorrectApplicantInformationThenReverify'|'RequiresSeparateActiveMemberIdentityReview'|
 'ValidatedProofingEvidenceOnly';
type Action='OBSERVE_PROOFING'|'APPLICANT_CORRECTED_IDENTITY';
type Command={
 requestId:string;expectedCaseRevision:number;expectedIdentityRevision:number;
 action:Action;observation:string;
 providerAndEvidenceValidated:'CONFIRMED'|'MISSING';
 applicantLinkValidated:'CONFIRMED'|'MISSING';evidenceCurrent:'CONFIRMED'|'MISSING';
 officialActivationStatusKnown:'CONFIRMED';officiallyActivated:false;
 identityDataChangedSinceEvidence:false;
};
type Snapshot={
 contract:'TRN-TECH-113';syntheticOnly:true;claimsNotVerified:true;
 caseRef:string;outcome:string;caseRevision:number;identityRevision:number;
 review:Review;events:{caseRevision:number;identityRevision:number;requestId:string;
 action:string;review:Review}[];
 realMemberAuthenticated:false;realIdentityVerified:false;nationalIdStored:false;
 realIdpContacted:false;officialMembershipActivated:false;waitingPeriodStarted:false;
 walletCredited:false;clinicalRecordAccessGranted:false;
 sponsorClinicalAccessGranted:false;nationalReleaseAllowed:false;
};
const allowed=new Set(['READ','Applied','Replayed']);
const id=()=> 'test:proof-'+crypto.randomUUID().replaceAll('-','');
function valid(x:unknown,ref:string):x is Snapshot{
 if(!x||typeof x!=='object')return false;
 const s=x as Partial<Snapshot>;
 if(s.contract!=='TRN-TECH-113'||s.syntheticOnly!==true||
 s.claimsNotVerified!==true||s.caseRef!==ref||
 !allowed.has(String(s.outcome))||
 !Number.isSafeInteger(s.caseRevision)||s.caseRevision!<0||
 !Number.isSafeInteger(s.identityRevision)||s.identityRevision!<0||
 !['AwaitingAuthoritativeVerification','CorrectApplicantInformationThenReverify',
   'RequiresSeparateActiveMemberIdentityReview','ValidatedProofingEvidenceOnly'].includes(String(s.review))||
 !Array.isArray(s.events)||s.events.length!==s.caseRevision||
 s.identityRevision!>s.caseRevision! ||
 s.realMemberAuthenticated!==false||s.realIdentityVerified!==false||
 s.nationalIdStored!==false||s.realIdpContacted!==false||
 s.officialMembershipActivated!==false||s.waitingPeriodStarted!==false||
 s.walletCredited!==false||s.clinicalRecordAccessGranted!==false||
 s.sponsorClinicalAccessGranted!==false||s.nationalReleaseAllowed!==false)
 return false;
 return s.events.every((e,i)=>e.caseRevision===i+1&&
 Number.isSafeInteger(e.identityRevision)&&e.identityRevision>=0&&
 typeof e.requestId==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(e.requestId)&&
 (e.action==='ObserveProofing'||e.action==='ApplicantCorrectedIdentity'));
}
function acknowledged(s:Snapshot,c:Command,viaRead=false):boolean{
 const rev=c.expectedCaseRevision+1;
 const e=s.events.find(x=>x.caseRevision===rev);
 const action=c.action==='OBSERVE_PROOFING'
  ?'ObserveProofing':'ApplicantCorrectedIdentity';
 return !!e&&e.requestId===c.requestId&&e.action===action&&
  e.identityRevision===(c.expectedIdentityRevision+
   (c.action==='APPLICANT_CORRECTED_IDENTITY'?1:0))&&
  s.events.filter(x=>x.requestId===c.requestId).length===1&&
  (s.outcome==='Replayed'||(viaRead&&s.outcome==='READ')||
   (s.outcome==='Applied'&&s.caseRevision===rev))&&
  s.events[s.events.length-1]?.review===s.review;
}
export function ProofingCorrectionDemo(){
 const [caseRef,setCaseRef]=useState(id);
 const [state,setState]=useState<Snapshot|null>(null);
 const [message,setMessage]=useState('این محیط هیچ کد ملی، نام یا شناسه فرد حقیقی دریافت نمی‌کند.');
 const [pending,setPending]=useState<{caseRef:string;body:string}|null>(null);
 const [busy,setBusy]=useState(false);
 async function read(){
  if(busy||pending)return;
  setBusy(true);setState(null);
  try{
    const res=await fetch('/internal/demo/v1/proofing/'+encodeURIComponent(caseRef),
      {cache:'no-store',credentials:'omit'});
    if(!res.ok)throw Error('GET_FAILED');
    const x:unknown=await res.json();
    if(!valid(x,caseRef))throw Error('INVALID_SOURCE');
    setState(x);setMessage('نسخه ثبت‌شده از ژورنال آزمایشی PostgreSQL خوانده شد.');
  }catch{setMessage('خواندن منبع معتبر نبود. هیچ وضعیت قبلی استفاده نمی‌شود.');}
  finally{setBusy(false);}
 }
 async function send(command:{caseRef:string;body:string}){
  if(busy)return;
  setBusy(true);setState(null);
  try{
   const res=await fetch('/internal/demo/v1/proofing/'+encodeURIComponent(command.caseRef)+'/commands',
    {method:'POST',cache:'no-store',credentials:'omit',
     headers:{'Content-Type':'application/json'},body:command.body});
   if(!res.ok)throw Error('UNCERTAIN_OR_DENIED');
   const x:unknown=await res.json();
   if(!valid(x,command.caseRef) || (x.outcome!=='Applied'&&x.outcome!=='Replayed'))
    throw Error('UNVERIFIABLE_ACK');
   const original=JSON.parse(command.body) as Command;
   if(!acknowledged(x,original))throw Error('UNBOUND_ACK');
   setState(x);setPending(null);
   setMessage(x.outcome==='Replayed'?'درخواست اصلی بدون ثبت مجدد بازیابی شد.':
    'رویداد آزمایشی ثبت و ممیزی شد؛ هیچ هویت واقعی تأیید نشده است.');
  }catch{
   setPending(command);
   setMessage('نتیجه فرمان نامطمئن است؛ احتمال ثبت در سرور وجود دارد. فقط همان درخواست اصلی را دستی بازپخش کنید.');
  }finally{setBusy(false);}
 }
 async function inspectPending(){
  if(busy||!pending)return;
  const original=pending;
  setBusy(true);setState(null);
  try{
   const res=await fetch('/internal/demo/v1/proofing/'+
     encodeURIComponent(original.caseRef),{cache:'no-store',credentials:'omit'});
   if(!res.ok)throw Error('READ_DENIED');
   const x:unknown=await res.json();
   const c=JSON.parse(original.body) as Command;
   if(!valid(x,original.caseRef)||x.outcome!=='READ'||!acknowledged(x,c,true))
    throw Error('UNBOUND_JOURNAL_READ');
   setState(x);setPending(null);
   setMessage('همان فرمان در ژورنال آزمایشی پیدا شد؛ بدون بازپخش یا ثبت جدید بازیابی شد.');
  }catch{
   setMessage('ثبت همان فرمان با خواندن تأیید نشد؛ قفل و بازپخش دستی حفظ شد.');
  }finally{setBusy(false);}
 }
 function emit(action:Action,observation:string){
  if(!state||busy||pending)return;
  const correct=action==='APPLICANT_CORRECTED_IDENTITY';
  const cmd:Command={requestId:crypto.randomUUID(),
   expectedCaseRevision:state.caseRevision,expectedIdentityRevision:state.identityRevision,
   action,observation:correct?'NONE':observation,
   providerAndEvidenceValidated:correct?'MISSING':'CONFIRMED',
   applicantLinkValidated:correct?'MISSING':'CONFIRMED',
   evidenceCurrent:correct?'MISSING':'CONFIRMED',
   officialActivationStatusKnown:'CONFIRMED',
   officiallyActivated:false,identityDataChangedSinceEvidence:false};
  void send({caseRef,body:JSON.stringify(cmd)});
 }
 const rev=state?.caseRevision??0;
 return <main className="proofing-workbench" dir="rtl">
  <h1>چرخه آزمایشی عدم تطابق، اصلاح هویت و احراز مجدد</h1>
  <p className="denial">تمام «شواهد تأییدشده» صرفاً فرض آزمایش هستند؛
   هیچ ارائه‌دهنده واقعی احراز هویت فراخوانی نمی‌شود، کد ملی وارد نمی‌شود،
   هیچ عضویت یا کیف پولی فعال نمی‌شود و مبدأ ۹۰ روز انتظار تغییر نمی‌کند.</p>
  <label>شناسه تصادفی پرونده آزمایشی
   <input dir="ltr" value={caseRef} disabled={busy||!!pending}
    onChange={e=>{setCaseRef(e.target.value);setState(null);}}/>
  </label>
  <button type="button" disabled={busy||!!pending} onClick={()=>void read()}>
   خواندن پرونده آزمایشی از PostgreSQL
  </button>
  <div className="actions">
   <button type="button" disabled={busy||!!pending||!state}
    onClick={()=>emit('OBSERVE_PROOFING','DEFINITIVE_MISMATCH')}>
    ثبت فرضی عدم تطابق قطعی
   </button>
   <button type="button" disabled={busy||!!pending||
      !state||state.review!=='CorrectApplicantInformationThenReverify'}
    onClick={()=>emit('APPLICANT_CORRECTED_IDENTITY','NONE')}>
    ثبت صرفاً اعلام اصلاح (بدون داده هویتی)
   </button>
   <button type="button" disabled={busy||!!pending||!state||state.identityRevision===0}
    onClick={()=>emit('OBSERVE_PROOFING','VERIFIED_MATCH')}>
    ثبت فرضی نتیجه احراز مجدد
   </button>
  </div>
  {pending&&<section role="alert" className="panel">
   <p>نتیجه قبلی نامطمئن است؛ شناسه درخواست و متن JSON اولیه تا تعیین تکلیف در حافظه صفحه نگهداری می‌شود.</p>
   <button disabled={busy} onClick={()=>void inspectPending()}>بررسی فقط‌خواندنی فرمان قبلی</button>
   <button disabled={busy} onClick={()=>void send(pending)}>بازپخش دستی دقیق همان درخواست</button>
  </section>}
  <p data-testid="proofing-status" role="status">{message}</p>
  {state&&<section className="history" role="region" aria-label="تاریخچه ممیزی آزمایشی">
   <p>نسخه پرونده: <strong>{rev}</strong></p>
   <p>نسخه اطلاعات هویتی فرضی: <strong>{state.identityRevision}</strong></p>
   <p>بازبینی جاری: <strong>{state.review}</strong></p>
   <p>هویت قانونی احراز شده؟ <strong>خیر</strong></p>
   <p>فعال‌سازی عضویت، شروع انتظار، اعتبار درمانی: <strong>خیر</strong></p>
   <ol>{state.events.map(e=><li key={e.caseRevision}>
    رویداد {e.caseRevision}: {e.action} — نسخه اطلاعات {e.identityRevision} — {e.review}
   </li>)}</ol>
  </section>}
 </main>;
}
