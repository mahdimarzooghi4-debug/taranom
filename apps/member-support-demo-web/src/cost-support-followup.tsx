import {useState} from 'react';

// TRN-TECH-099: two independent canonical test PostgreSQL journal reads,
// followed by a *manual* existing support append-only command. No insurer action.
type Source={previewId:string;memberRef:string;previewRevision:number;
  previewFingerprint:string;choiceRevision:number};
type Link={supportRevision:number;supportFollowUpCount:number;
  costChoiceRevision:number;previewFingerprint:string;
  independentlyReattested:boolean;atomicCrossJournalSnapshot:boolean};
type Preflight={
  contract:string;syntheticOnly:boolean;outcome:string;
  previewId:string;caseRef:string;memberRef:string;
  previewRevision:number;previewFingerprint:string;
  costChoiceRevision:number;supportRevision:number;
  originalSubmissionRequestId:string;claimedSubmitterRef:string;evidenceRef:string;
  independentlyReattested:boolean;atomicCrossJournalSnapshot:boolean;
  memberAuthenticated:boolean;insurerContacted:boolean;coverageAdjudicated:boolean;
  complaintLegallySubmitted:boolean;officialChoiceRecorded:boolean;
  walletMutated:boolean;financialCommitmentCreated:boolean;
  clinicalConsentGranted:boolean;nationalReleaseAllowed:boolean;
};
type Command={
  requestId:string;expectedRevision:number;action:'FOLLOW_UP_UNVERIFIED';
  memberRef:string;topic:'INSURANCE_COVERAGE_DISPUTE';
  unverifiedEvidenceRef:string;claimedSubmitterRef:string;
  originalSubmissionRequestId:string;
  claimedFollowUpActorRef:string;unverifiedFollowUpNoteRef:string;
};
const testRef=(v:string)=>v.length<=128&&/^test:[A-Za-z0-9_:-]+$/.test(v);
const uuid=(v:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(v);
const flags=(v:Preflight)=>v.memberAuthenticated===false &&
  v.insurerContacted===false&&v.coverageAdjudicated===false&&
  v.complaintLegallySubmitted===false&&v.officialChoiceRecorded===false&&
  v.walletMutated===false&&v.financialCommitmentCreated===false&&
  v.clinicalConsentGranted===false&&v.nationalReleaseAllowed===false;
function checked(v:unknown,source:Source,link:Link,caseRef:string):v is Preflight{
  if(!v||typeof v!=='object')return false;
  const x=v as Partial<Preflight>;
  return x.contract==='TRN-TECH-099'&&x.syntheticOnly===true&&
    x.outcome==='UNVERIFIED_FOLLOWUP_PREFLIGHT'&&
    x.previewId===source.previewId&&x.caseRef===caseRef&&
    x.memberRef===source.memberRef&&
    x.previewRevision===source.previewRevision&&
    x.previewFingerprint===source.previewFingerprint&&
    x.costChoiceRevision===source.choiceRevision&&
    x.costChoiceRevision===link.costChoiceRevision&&
    x.supportRevision===link.supportRevision&&
    x.evidenceRef==='test:cost-preview:'+source.previewId&&
    typeof x.originalSubmissionRequestId==='string'&&uuid(x.originalSubmissionRequestId)&&
    typeof x.claimedSubmitterRef==='string'&&testRef(x.claimedSubmitterRef)&&
    x.independentlyReattested===true&&x.atomicCrossJournalSnapshot===false&&
    flags(x as Preflight);
}

export function CostSupportFollowUp({source,caseRef,link,onPending,onRecorded}:{
  source:Source;caseRef:string;link:Link;
  onPending:(state:boolean)=>void;onRecorded:(message:string)=>void;
}){
  const [note,setNote]=useState('test:insurance-followup-'+crypto.randomUUID());
  const [pending,setPending]=useState<Command|null>(null);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('پیگیری هنوز ثبت نشده است.');
  const endpoint='/internal/demo/v1/support/'+encodeURIComponent(caseRef)+'/commands';
  const journal='/internal/demo/v1/support/'+encodeURIComponent(caseRef);
  // Only the exact stored command event can resolve an uncertain write.
  // A global revision or a successful HTTP ACK is insufficient.
  function exactJournal(raw:unknown,command:Command):boolean{
    if(!raw||typeof raw!=='object')return false;
    const x=raw as Record<string,unknown>;
    if(x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||
       x.memberAuthenticated!==false||x.insurerContacted!==false||
       x.complaintLegallySubmitted!==false||x.clinicalReviewCompleted!==false||
       x.nationalReleaseAllowed!==false||
       x.caseRef!==caseRef||x.topic!=='InsuranceCoverageDispute'||
       x.memberRef!==command.memberRef||
       x.evidenceRef!==command.unverifiedEvidenceRef||
       x.originalRequestId!==command.originalSubmissionRequestId||
       !Number.isSafeInteger(x.revision)||
       (x.revision as number)<command.expectedRevision+1||
       x.followUpCount!==(x.revision as number)-1||
       !Array.isArray(x.followUps)||
       x.followUps.length!==x.followUpCount)return false;
    const event=x.followUps[command.expectedRevision-1] as
      Record<string,unknown>|undefined;
    return !!event&&event.revision===command.expectedRevision+1&&
      event.requestId===command.requestId&&
      event.noteRef===command.unverifiedFollowUpNoteRef&&
      event.claimedActorRef===command.claimedFollowUpActorRef&&
      x.followUps.filter((e:unknown)=>e&&typeof e==='object'&&
         (e as Record<string,unknown>).requestId===command.requestId).length===1;
  }
  async function verifyByRead(command:Command):Promise<boolean>{
    const r=await fetch(journal,{credentials:'omit',cache:'no-store'});
    if(!r.ok)return false;
    const value:unknown=await r.json();
    return !!value&&typeof value==='object'&&
      (value as Record<string,unknown>).outcome==='READ'&&
      exactJournal(value,command);
  }
  async function inspectUncertain(){
    if(busy||!pending)return;
    const original=pending;
    setBusy(true);
    try{
      if(!await verifyByRead(original))throw Error('UNVERIFIED_EVENT');
      setPending(null);onPending(false);
      onRecorded('همان پیگیری آزمایشی از ژورنال PostgreSQL بدون POST مجدد تأیید شد؛ رابطه دو Journal باید بازخوانی شود.');
    }catch{
      setMessage('همان UUID و یادداشت در ژورنال تأیید نشد؛ قفل و بازپخش دستی حفظ شد.');
    }finally{setBusy(false);}
  }
  async function exactSend(command:Command){
    if(busy)return;
    setBusy(true);setPending(command);onPending(true);
    try{
      const r=await fetch(endpoint,{method:'POST',credentials:'omit',cache:'no-store',
        headers:{'Content-Type':'application/json'},body:JSON.stringify(command)});
      const value:unknown=await r.json();
      if(r.status===409){
        setPending(null);onPending(false);
        onRecorded('نسخه یا زمینه پرونده تغییر کرده است؛ پیگیری ثبت نشد. بازخوانی مجدد لازم است.');
        return;
      }
      if(!r.ok||!value||typeof value!=='object')throw Error('NOT_VERIFIED');
      const x=value as Record<string,unknown>;
      if(!['RecordedUnverified','Replayed'].includes(String(x.outcome))||
         !exactJournal(x,command)||
         (x.outcome==='RecordedUnverified'&&x.revision!==command.expectedRevision+1)||
         !await verifyByRead(command))
        throw Error('RECEIPT_UNVERIFIABLE');
      setPending(null);onPending(false);
      onRecorded(x.outcome==='Replayed'
        ? 'همان پیگیری بدون ایجاد رویداد دوم بازپخش شد؛ رابطه باید دوباره بررسی شود.'
        : 'یک پیگیری ساختگی در PostgreSQL ثبت شد؛ رابطه دو Journal باید دوباره بررسی شود.');
    }catch{
      // A real transaction may have committed before the ACK was lost.
      // Never invent a replacement requestId or silently assume failure.
      setMessage('نتیجه ثبت پیگیری نامطمئن است؛ فقط تکرار همان UUID مجاز است.');
    }finally{setBusy(false);}
  }
  async function prepare(){
    if(busy||pending||!testRef(note))return;
    setBusy(true);
    let command:Command|null=null;
    try{
      const url='/internal/demo/v1/cost-previews/'+
        encodeURIComponent(source.previewId)+'/support-link/'+
        encodeURIComponent(caseRef)+'/follow-up-preflight?expectedCostChoiceRevision='+
        source.choiceRevision+'&expectedSupportRevision='+link.supportRevision;
      const res=await fetch(url,{cache:'no-store',credentials:'omit'});
      const v:unknown=await res.json();
      if(!res.ok||!checked(v,source,link,caseRef))throw Error('PREFLIGHT_FAILED');
      command={
        requestId:crypto.randomUUID(),expectedRevision:v.supportRevision,
        action:'FOLLOW_UP_UNVERIFIED',memberRef:source.memberRef,
        topic:'INSURANCE_COVERAGE_DISPUTE',
        unverifiedEvidenceRef:v.evidenceRef,
        claimedSubmitterRef:v.claimedSubmitterRef,
        originalSubmissionRequestId:v.originalSubmissionRequestId,
        claimedFollowUpActorRef:'test:cost-support-followup-operator',
        unverifiedFollowUpNoteRef:note,
      };
    }catch{
      setMessage('پیش‌بررسی شکست خورد یا نسخه تغییر کرد؛ هیچ پیگیری ثبت نشد.');
    }finally{setBusy(false);}
    if(command)void exactSend(command);
  }
  return <section aria-label="پیگیری دستی بیمه" data-testid="cost-followup-panel">
    <h4>ثبت یادداشت پیگیری تأییدنشده</h4>
    <p className="muted">اول هر دو Journal مستقل بازبینی می‌شوند، سپس فقط با کلیک شما
      یادداشت ساختگی به همان پرونده اضافه می‌شود. بررسی بین دو Journal اتمیک نیست.</p>
    <label>مرجع یادداشت پیگیری آزمایشی
      <input aria-label="مرجع یادداشت پیگیری" dir="ltr" value={note}
        disabled={busy||pending!==null} onChange={e=>setNote(e.target.value)}/>
    </label>
    <button type="button" disabled={busy||pending!==null||!testRef(note)}
      onClick={()=>void prepare()}>ثبت دستی پیگیری آزمایشی بیمه</button>
    {pending?<div className="actions" data-testid="cost-followup-uncertain">
      <button type="button" disabled={busy}
        onClick={()=>void inspectUncertain()}>بررسی فقط‌خواندنی پیگیری ثبت‌شده</button>
      <button type="button" disabled={busy}
        onClick={()=>void exactSend(pending)}>تکرار دقیق UUID پیگیری نامطمئن</button>
    </div>:null}
    <p role="status" data-testid="cost-followup-message">{message}</p>
  </section>;
}
