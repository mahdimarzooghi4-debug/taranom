import {useState} from 'react';
import {CostSupportFollowUp} from './cost-support-followup';
import {CostSupportTimeline} from './cost-support-timeline';

// TRN-TECH-097: manual nonbinding insurance-dispute intake against two
// existing test-only PG journals. No claim of legal receipt or coverage.
type Cost = {
  previewId:string; memberRef:string; previewRevision:number;
  previewFingerprint:string; choiceRevision:number;
};
type Submission = {
  requestId:string; expectedRevision:0; action:'SUBMIT_UNVERIFIED';
  memberRef:string; topic:'INSURANCE_COVERAGE_DISPUTE';
  unverifiedEvidenceRef:string; claimedSubmitterRef:string;
  originalSubmissionRequestId:string;
  claimedFollowUpActorRef:null; unverifiedFollowUpNoteRef:null;
};
type Correlation = {
  contract:string; syntheticOnly:boolean;
  outcome:string; previewId:string; caseRef:string; memberRef:string;
  previewRevision:number; previewFingerprint:string; costChoiceRevision:number;
  supportRevision:number; supportFollowUpCount:number; topic:string; evidenceRef:string;
  independentlyReattested:boolean; atomicCrossJournalSnapshot:boolean;
  memberAuthenticated:boolean; insurerContacted:boolean; coverageAdjudicated:boolean;
  complaintLegallySubmitted:boolean; officialChoiceRecorded:boolean;
  walletMutated:boolean; financialCommitmentCreated:boolean;
  clinicalConsentGranted:boolean; nationalReleaseAllowed:boolean;
};
const testRef=(s:string)=>s.length<=128&&/^test:[A-Za-z0-9_:-]+$/.test(s);
const flags=(v:Correlation)=>v.memberAuthenticated===false &&
  v.insurerContacted===false && v.coverageAdjudicated===false &&
  v.complaintLegallySubmitted===false && v.officialChoiceRecorded===false &&
  v.walletMutated===false && v.financialCommitmentCreated===false &&
  v.clinicalConsentGranted===false && v.nationalReleaseAllowed===false;
function verified(v:unknown,source:Cost,caseRef:string):v is Correlation {
  if(!v||typeof v!=='object')return false;
  const x=v as Correlation;
  return x.contract==='TRN-TECH-097'&&x.syntheticOnly===true &&
    x.outcome==='UNVERIFIED_CROSS_JOURNAL_REFERENCE'&&
    x.previewId===source.previewId&&x.caseRef===caseRef&&
    x.memberRef===source.memberRef&&x.previewRevision===source.previewRevision&&
    x.previewFingerprint===source.previewFingerprint&&
    Number.isSafeInteger(x.costChoiceRevision)&&x.costChoiceRevision>=source.choiceRevision&&
    Number.isSafeInteger(x.supportRevision)&&x.supportRevision>=1&&
    x.supportFollowUpCount===x.supportRevision-1&&
    x.topic==='InsuranceCoverageDispute'&&
    x.evidenceRef==='test:cost-preview:'+source.previewId&&
    x.independentlyReattested===true&&x.atomicCrossJournalSnapshot===false&&flags(x);
}
const zero='00000000-0000-0000-0000-000000000000';
export function CostSupportHandoff({source}:{source:Cost}) {
  const [caseRef,setCaseRef]=useState('test:insurance-'+crypto.randomUUID());
  const [pending,setPending]=useState<Submission|null>(null);
  const [created,setCreated]=useState(false);
  const [review,setReview]=useState<Correlation|null>(null);
  const [busy,setBusy]=useState(false);
  const [followUpPending,setFollowUpPending]=useState(false);
  const [message,setMessage]=useState('رابطه دو Journal هنوز بررسی نشده است.');
  const evidence='test:cost-preview:'+source.previewId;
  function reset(ref:string) {
    setCaseRef(ref);setCreated(false);setReview(null);
    setMessage('پرونده باید مستقل از PostgreSQL بازخوانی شود.');
  }
  async function sendExact(command:Submission) {
    if(busy || !testRef(caseRef))return;
    setBusy(true);setPending(command);setReview(null);
    try {
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef)+'/commands',{
        method:'POST',credentials:'omit',cache:'no-store',
        headers:{'Content-Type':'application/json'},body:JSON.stringify(command)});
      const v:unknown=await response.json();
      if(!response.ok||!v||typeof v!=='object')throw Error('UNVERIFIED');
      const x=v as Record<string,unknown>;
      if(x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||
         x.memberAuthenticated!==false||x.insurerContacted!==false||
         x.complaintLegallySubmitted!==false||x.clinicalReviewCompleted!==false||
         x.nationalReleaseAllowed!==false||
         !['RecordedUnverified','Replayed'].includes(String(x.outcome))||
         x.caseRef!==caseRef||x.topic!=='InsuranceCoverageDispute'||
         x.memberRef!==source.memberRef||x.evidenceRef!==evidence||
         x.originalRequestId!==command.requestId||x.revision!==1)
        throw Error('RECEIPT_UNVERIFIABLE');
      setPending(null);setCreated(true);
      setMessage(x.outcome==='Replayed'
        ? 'بازپخش دقیق بدون رویداد دوم تأیید شد؛ موضوع همچنان غیررسمی است.'
        : 'فقط درخواست ساختگی در Journal پشتیبانی ثبت شد؛ بیمه‌گر تماس گرفته نشده است.');
    } catch {
      setMessage('نتیجه ارسال نامعلوم است؛ فقط تکرار دقیق همان درخواست UUID مجاز است.');
    } finally {setBusy(false);}
  }
  async function create(){
    if(busy||pending||followUpPending||created||!testRef(caseRef))return;
    setBusy(true);setReview(null);
    let command:Submission|null=null;
    try{
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef),{
        credentials:'omit',cache:'no-store'});
      const v:unknown=await response.json();
      if(!response.ok||!v||typeof v!=='object')throw Error('SOURCE_UNAVAILABLE');
      const x=v as Record<string,unknown>;
      if(x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||x.revision!==0||
         x.topic!==null||x.memberAuthenticated!==false||
         x.insurerContacted!==false||x.complaintLegallySubmitted!==false||
         x.nationalReleaseAllowed!==false)throw Error('NOT_FRESH');
      command={
        requestId:crypto.randomUUID(),expectedRevision:0,action:'SUBMIT_UNVERIFIED',
        memberRef:source.memberRef,topic:'INSURANCE_COVERAGE_DISPUTE',
        unverifiedEvidenceRef:evidence,claimedSubmitterRef:'test:cost-handoff-operator',
        originalSubmissionRequestId:zero,
        claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null
      };
    } catch {
      setMessage('پرونده خالی معتبر نیست یا قبلاً ثبت شده؛ هیچ فرمانی ارسال نشد.');
    } finally {setBusy(false);}
    if(command)void sendExact(command);
  }
  async function inspect(){
    if(busy||pending||followUpPending||!testRef(caseRef))return;
    setBusy(true);setReview(null);
    try{
      const res=await fetch('/internal/demo/v1/cost-previews/'+
        encodeURIComponent(source.previewId)+'/support-link/'+encodeURIComponent(caseRef),
        {credentials:'omit',cache:'no-store'});
      const raw:unknown=await res.json();
      if(!res.ok||!verified(raw,source,caseRef))throw Error('UNVERIFIED');
      setReview(raw);setCreated(true);
      setMessage('هر دو Journal مستقل بازخوانی و نسخه‌ها مقایسه شدند؛ این رأی بیمه نیست.');
    }catch{
      setMessage('رابطه دو پرونده قابل تأیید نیست؛ هیچ وضعیت سابقی معتبر فرض نشد.');
    }finally{setBusy(false);}
  }
  return <section data-testid="cost-support-handoff" className="panel"
    aria-label="پیوند آزمایشی برآورد هزینه و اعتراض بیمه">
    <h3>پیگیری دستی اختلاف بیمه برای همین برآورد ساختگی</h3>
    <p className="muted">TRN-TECH-097 · فقط شناسه مرجع و نسخه‌ها پیوند می‌خورند.
      این عمل پوشش بیمه، تقصیر، پرداخت، رضایت درمان یا رأی رسمی ایجاد نمی‌کند.</p>
    <p>مرجع شاهد ساختگی: <code dir="ltr">{evidence}</code></p>
    <label>شناسه پرونده اعتراض ساختگی
      <input aria-label="پرونده اختلاف بیمه" dir="ltr" value={caseRef}
        disabled={busy||pending!==null||followUpPending}
        onChange={e=>reset(e.target.value)}/>
    </label>
    <div className="actions">
      <button type="button" disabled={busy||pending!==null||followUpPending||created||!testRef(caseRef)}
        onClick={()=>void create()}>ثبت دستی اعتراض آزمایشی در Journal پشتیبانی</button>
      <button type="button" disabled={busy||pending!==null||followUpPending||!testRef(caseRef)}
        onClick={()=>void inspect()}>راستی‌آزمایی مستقل رابطه دو Journal</button>
      {pending?<button type="button" disabled={busy}
        onClick={()=>void sendExact(pending)}>تکرار دقیق UUID درخواست نامطمئن</button>:null}
    </div>
    {review?<CostSupportFollowUp source={source} caseRef={caseRef}
      link={review} onPending={setFollowUpPending}
      onRecorded={(result)=>{setReview(null);setCreated(true);setMessage(result);}}/>:null}
    {review&&!followUpPending?<CostSupportTimeline
      key={source.previewId+':'+source.choiceRevision+':'+caseRef+':'+review.supportRevision}
      source={source} caseRef={caseRef} link={review}/>:null}
    {review?<div data-testid="cost-support-linked-result">
      <p>نتیجه: {review.outcome}</p>
      <p>نسخه انتخاب هزینه: {review.costChoiceRevision} · نسخه اعتراض: {review.supportRevision}</p>
      <p>داده‌ها مستقل هستند؛ وضعیت بیمه یا موجودی کیف پول تغییر نکرده است.</p>
    </div>:null}
    <p data-testid="cost-support-message" role="status">{message}</p>
  </section>;
}
