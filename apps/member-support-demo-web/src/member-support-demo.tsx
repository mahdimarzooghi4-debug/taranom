import { useState } from 'react';
import {exportRecovery,importRecovery,type SupportCommand,type PendingWrite} from './manual-recovery';

type FollowUp = { revision: number; requestId: string; noteRef: string; claimedActorRef: string };
type Support = {
  contract: 'TRN-TECH-075'; syntheticOnly: true;
  memberAuthenticated: false; complaintLegallySubmitted: false;
  insurerContacted: false; clinicalReviewCompleted: false; nationalReleaseAllowed: false;
  caseRef: string; revision: number; outcome: string;
  topic: 'MedicalServiceComplaint' | 'InsuranceCoverageDispute' | null;
  memberRef: string | null; originalRequestId: string | null;
  evidenceRef: string | null; claimedSubmitterRef: string | null;
  followUpCount: number; followUps: FollowUp[];
};
type SupportHistory = {
  contract: 'TRN-TECH-084'; syntheticOnly: true; memberAuthenticated: false;
  complaintLegallySubmitted: false; insurerContacted: false;
  clinicalReviewCompleted: false; nationalReleaseAllowed: false;
  caseRef: string; currentRevision: number; afterRevision: number;
  hasMore: boolean; nextAfterRevision: number | null;
  events: {revision:number; action:'SUBMIT_UNVERIFIED'|'FOLLOW_UP_UNVERIFIED';
           noteRef:string|null; hypotheticalOnly:true}[];
};
type SupportHistorical = {
  contract:'TRN-TECH-084'; syntheticOnly:true; memberAuthenticated:false;
  complaintLegallySubmitted:false; insurerContacted:false;
  clinicalReviewCompleted:false; nationalReleaseAllowed:false;
  caseRef:string; currentRevision:number; asOfRevision:number; snapshot:Support;
};
function denied(v:Partial<SupportHistory>):boolean {
  return v.contract==='TRN-TECH-084' && v.syntheticOnly===true &&
    v.memberAuthenticated===false && v.complaintLegallySubmitted===false &&
    v.insurerContacted===false && v.clinicalReviewCompleted===false &&
    v.nationalReleaseAllowed===false;
}
function historyValid(data:unknown, current:Support, after:number):data is SupportHistory {
  if(!data || typeof data!=='object')return false;
  const v=data as Partial<SupportHistory>;
  if(!denied(v) || v.caseRef!==current.caseRef ||
    v.currentRevision!==current.revision || v.afterRevision!==after ||
    typeof v.hasMore!=='boolean' || !Array.isArray(v.events) || v.events.length>2)return false;
  let last=after;
  for(const e of v.events) {
    if(!e || !Number.isSafeInteger(e.revision) || e.revision!==last+1 ||
      e.revision>current.revision || e.hypotheticalOnly!==true ||
      (e.action!=='SUBMIT_UNVERIFIED' && e.action!=='FOLLOW_UP_UNVERIFIED') ||
      (e.action==='SUBMIT_UNVERIFIED')!==(e.revision===1) ||
      (e.action==='SUBMIT_UNVERIFIED' ? e.noteRef!==null :
       typeof e.noteRef!=='string' || !testRef(e.noteRef)))return false;
    last=e.revision;
  }
  return v.hasMore
    ? v.events.length===2 && v.nextAfterRevision===last && last<current.revision
    : v.nextAfterRevision===null && last===current.revision;
}
function historicalValid(data:unknown,current:Support,at:number):data is SupportHistorical {
  if(!data || typeof data!=='object')return false;
  const x=data as Partial<SupportHistorical>;
  return denied(x) && x.caseRef===current.caseRef &&
    x.currentRevision===current.revision && x.asOfRevision===at &&
    verified(x.snapshot,current.caseRef) && x.snapshot.revision===at &&
    x.snapshot.outcome==='HISTORICAL_READ';
}

const testRef = (s: string): boolean =>
  s.length <= 128 && /^test:[A-Za-z0-9_:-]+$/.test(s);
const zero = '00000000-0000-0000-0000-000000000000';
function verified(value: unknown, caseRef: string): value is Support {
  if (!value || typeof value !== 'object') return false;
  const x = value as Partial<Support>;
  if (x.contract !== 'TRN-TECH-075' || x.syntheticOnly !== true ||
      x.memberAuthenticated !== false || x.complaintLegallySubmitted !== false ||
      x.insurerContacted !== false || x.clinicalReviewCompleted !== false ||
      x.nationalReleaseAllowed !== false || x.caseRef !== caseRef ||
      !Number.isSafeInteger(x.revision) || x.revision! < 0 ||
      typeof x.outcome !== 'string' || !Array.isArray(x.followUps) ||
      !Number.isSafeInteger(x.followUpCount) || x.followUpCount !== x.followUps.length) return false;
  if (x.revision === 0)
    return x.originalRequestId === null && x.memberRef === null &&
      x.topic === null && x.followUpCount === 0 && x.followUps.length === 0;
  return x.revision === x.followUpCount! + 1 &&
    (x.topic === 'MedicalServiceComplaint' || x.topic === 'InsuranceCoverageDispute') &&
    typeof x.originalRequestId === 'string' &&
    typeof x.memberRef === 'string' && testRef(x.memberRef) &&
    typeof x.evidenceRef === 'string' && testRef(x.evidenceRef) &&
    typeof x.claimedSubmitterRef === 'string' && testRef(x.claimedSubmitterRef) &&
    x.followUps.every((v,i) => v && Number.isSafeInteger(v.revision) && v.revision===i+2 &&
      typeof v.requestId==='string'&&
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v.requestId) &&
      testRef(v.noteRef) && testRef(v.claimedActorRef));
}

export function MemberSupportDemo() {
  const [caseRef, setCaseRef] = useState('test:support-demo');
  const [memberRef, setMemberRef] = useState('test:member-demo');
  const [evidenceRef, setEvidenceRef] = useState('test:evidence-demo');
  const [submitter, setSubmitter] = useState('test:submitter-demo');
  const [actor, setActor] = useState('test:followup-actor-demo');
  const [note, setNote] = useState('test:note-demo');
  const [topic, setTopic] = useState<'MEDICAL_SERVICE_COMPLAINT' | 'INSURANCE_COVERAGE_DISPUTE'>('MEDICAL_SERVICE_COMPLAINT');
  const [current, setCurrent] = useState<Support | null>(null);
  const [busy, setBusy] = useState(false);
  // Immutable snapshot of the one outstanding uncertain command. Never
  // create a fresh requestId for its retry; no browser storage or real actor.
  const [pendingWrite, setPendingWrite] = useState<PendingWrite|null>(null);
  const [manualRecoveryText, setManualRecoveryText] = useState('');
  const [returnedFollowUp, setReturnedFollowUp] = useState('');
  const [returnStatus, setReturnStatus] = useState('رسید پیگیری مرکز هنوز بررسی نشده است.');
  const [confirmedReturn, setConfirmedReturn] = useState<{caseRef:string;revision:number;noteRef:string}|null>(null);
  const [message, setMessage] = useState('این فرم فقط درخواست‌ها و ارجاعات آزمایشی را ذخیره می‌کند.');
  const [timeline, setTimeline] = useState<SupportHistory|null>(null);
  const [past, setPast] = useState<SupportHistorical|null>(null);
  const [asOf, setAsOf] = useState('0');
  const [timelineMessage, setTimelineMessage] = useState('تاریخچه هنوز خوانده نشده است.');

  async function read() {
    setBusy(true); setCurrent(null); setTimeline(null); setPast(null);
    setConfirmedReturn(null);
    if (!testRef(caseRef)) {
      setMessage('فقط شناسه پرونده آزمایشی با پیشوند test: مجاز است.');
      setBusy(false); return;
    }
    try {
      const response = await fetch('/internal/demo/v1/support/' + encodeURIComponent(caseRef),
        { cache: 'no-store', credentials: 'omit' });
      if (!response.ok) throw Error('UNAVAILABLE');
      const data: unknown = await response.json();
      if (!verified(data, caseRef)) throw Error('NOT_VERIFIED');
      setCurrent(data);
      setMessage('نسخه آزمایشی از PostgreSQL خوانده شد؛ هیچ موضوع پزشکی یا بیمه‌ای رسیدگی رسمی نشده است.');
    } catch {
      setMessage('خواندن پرونده آزمایشی ممکن نیست؛ هیچ وضعیت قبلی معتبر فرض نمی‌شود.');
    } finally { setBusy(false); }
  }

  // Untrusted versioned locator, not authentication, delivery or clinical advice.
  async function inspectReturnedFollowUp(){
    if(busy||pendingWrite||returnedFollowUp.length===0||
       returnedFollowUp.length>1300)return;
    setBusy(true);setCurrent(null);setTimeline(null);setPast(null);
    setConfirmedReturn(null);
    try{
      const raw:unknown=JSON.parse(returnedFollowUp);
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw Error('INVALID_RECEIPT');
      const v=raw as Record<string,unknown>;
      const keys=['format','version','caseRef','memberRef','topic','evidenceRef',
        'originalRequestId','revision','followUpRequestId','noteRef','claimedActorRef'];
      if(Object.keys(v).length!==keys.length||
         Object.keys(v).some(k=>!keys.includes(k))||
         v.format!=='TRN-TECH-182-TEST-FOLLOWUP-RETURN'||v.version!==1||
         v.caseRef!==caseRef||v.memberRef!==memberRef||
         typeof v.caseRef!=='string'||!testRef(v.caseRef)||
         typeof v.memberRef!=='string'||!testRef(v.memberRef)||
         typeof v.evidenceRef!=='string'||!testRef(v.evidenceRef)||
         !['MedicalServiceComplaint','InsuranceCoverageDispute'].includes(String(v.topic))||
         typeof v.originalRequestId!=='string'||
         !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v.originalRequestId)||
         typeof v.followUpRequestId!=='string'||
         !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v.followUpRequestId)||
         typeof v.noteRef!=='string'||!testRef(v.noteRef)||
         typeof v.claimedActorRef!=='string'||!testRef(v.claimedActorRef)||
         !Number.isSafeInteger(v.revision)||Number(v.revision)<2)
        throw Error('INVALID_FIELDS');
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef),
        {cache:'no-store',credentials:'omit'});
      if(!response.ok)throw Error('READ_UNAVAILABLE');
      const source:unknown=await response.json();
      if(!verified(source,caseRef)||source.outcome!=='READ'||
         source.revision!==v.revision||source.memberRef!==v.memberRef||
         source.topic!==v.topic||source.evidenceRef!==v.evidenceRef||
         source.originalRequestId!==v.originalRequestId||
         source.followUpCount<1)throw Error('SOURCE_MISMATCH');
      const last=source.followUps[source.followUps.length-1];
      if(last.revision!==v.revision||last.requestId!==v.followUpRequestId||
         last.noteRef!==v.noteRef||last.claimedActorRef!==v.claimedActorRef)
        throw Error('FOLLOWUP_MISMATCH');
      setCurrent(source);
      setConfirmedReturn({caseRef,revision:source.revision,noteRef:last.noteRef});
      setReturnStatus('آخرین یادداشت ادعایی دقیقاً از PostgreSQL بازخوانی شد؛ هیچ پاسخ رسمی تأیید نشده است.');
      setReturnedFollowUp('');
    }catch{
      setCurrent(null);setConfirmedReturn(null);
      setReturnStatus('رسید پیگیری تأیید نشد؛ هیچ پرونده یا پاسخ رسمی بازیابی نشده است.');
    }finally{setBusy(false);}
  }

  // An unknown network result is NOT a failed database transaction.
  // Browser may have lost the response after PostgreSQL committed.
  async function sendExact(pending:PendingWrite) {
    if (busy || !testRef(pending.caseRef)) return;
    setBusy(true);setTimeline(null);setPast(null);
    setTimelineMessage('بعد از دستور نامطمئن، تاریخچه باید دوباره خوانده شود.');
    try {
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(pending.caseRef)+'/commands',{
        method:'POST',cache:'no-store',credentials:'omit',
        headers:{'Content-Type':'application/json'},
        body:JSON.stringify(pending.command)
      });
      if (!response.ok) throw Error('OUTCOME_UNVERIFIABLE');
      const value:unknown=await response.json();
      if (!verified(value,pending.caseRef) ||
          (value.outcome!=='RecordedUnverified' && value.outcome!=='Replayed') ||
          value.revision < pending.command.expectedRevision+1 ||
          value.memberRef!==pending.command.memberRef ||
          value.evidenceRef!==pending.command.unverifiedEvidenceRef ||
          value.claimedSubmitterRef!==pending.command.claimedSubmitterRef ||
          value.topic!==(pending.command.topic==='MEDICAL_SERVICE_COMPLAINT'
            ? 'MedicalServiceComplaint':'InsuranceCoverageDispute') ||
          value.originalRequestId!==(pending.command.action==='SUBMIT_UNVERIFIED'
            ? pending.command.requestId:pending.command.originalSubmissionRequestId) ||
          (pending.command.action==='FOLLOW_UP_UNVERIFIED' &&
            !value.followUps.some(entry=>
              entry.revision===pending.command.expectedRevision+1 &&
              entry.noteRef===pending.command.unverifiedFollowUpNoteRef &&
              entry.claimedActorRef===pending.command.claimedFollowUpActorRef)))
        throw Error('ACCEPTANCE_UNVERIFIABLE');
      setPendingWrite(null);
      setCurrent(value);
      setMessage(value.outcome==='Replayed'
        ? 'تکرار امن همان دستور در PostgreSQL تأیید شد؛ هیچ رویداد دوم ایجاد نشد.'
        : 'فقط ارجاع آزمایشی ثبت شد؛ پرونده قانونی، رسیدگی بالینی یا مکاتبه با بیمه‌گر ایجاد نشده است.');
    }catch{
      setCurrent(null);
      setPendingWrite(pending);
      setMessage('نتیجه شبکه نامطمئن است؛ ممکن است PostgreSQL دستور را ثبت کرده باشد. فقط همان دستور را با همان شناسه تکرار کنید.');
    }finally{setBusy(false);}
  }

  async function write(followUp: boolean) {
    if (!current || busy || pendingWrite) return;
    if (![caseRef,memberRef,evidenceRef,submitter].every(testRef) ||
        followUp && (![actor,note].every(testRef)) ||
        current.revision > 0 && !followUp ||
        current.revision === 0 && followUp ||
        current.revision > 0 && (current.memberRef !== memberRef ||
          current.evidenceRef !== evidenceRef || current.claimedSubmitterRef !== submitter)) {
      setMessage('ارجاعات آزمایشی معتبر نیستند یا به پرونده اولیه تعلق ندارند.');
      return;
    }
    const command:SupportCommand = {
      requestId:crypto.randomUUID(),
      expectedRevision:current.revision,
      action:followUp?'FOLLOW_UP_UNVERIFIED':'SUBMIT_UNVERIFIED',
      memberRef,
      topic:followUp
        ? current.topic==='InsuranceCoverageDispute'
          ? 'INSURANCE_COVERAGE_DISPUTE':'MEDICAL_SERVICE_COMPLAINT'
        : topic,
      unverifiedEvidenceRef:evidenceRef,claimedSubmitterRef:submitter,
      originalSubmissionRequestId:followUp?current.originalRequestId!:zero,
      claimedFollowUpActorRef:followUp?actor:null,
      unverifiedFollowUpNoteRef:followUp?note:null
    };
    const pending={caseRef,command};
    setPendingWrite(pending);
    await sendExact(pending);
  }

  function importManualRecovery() {
    if(busy || pendingWrite)return;
    const restored=importRecovery(manualRecoveryText);
    if(!restored){
      setCurrent(null);setTimeline(null);setPast(null);
      setMessage('رسید بازیابی آزمایشی معتبر نیست؛ هیچ درخواستی ارسال نشد.');
      return;
    }
    // Never POST on import. On explicit manual retry the existing .NET
    // idempotency contract determines whether this was already committed.
    setCurrent(null);setTimeline(null);setPast(null);
    setCaseRef(restored.caseRef);
    setMemberRef(restored.command.memberRef);
    setEvidenceRef(restored.command.unverifiedEvidenceRef);
    setSubmitter(restored.command.claimedSubmitterRef);
    setTopic(restored.command.topic);
    setActor(restored.command.claimedFollowUpActorRef ?? 'test:followup-actor-demo');
    setNote(restored.command.unverifiedFollowUpNoteRef ?? 'test:note-demo');
    setPendingWrite(restored);
    setManualRecoveryText('');
    setTimelineMessage('رسید بازیابی وارد شد؛ برای خواندن تاریخچه باید دستور تعیین تکلیف شود.');
    setMessage('رسید آزمایشی وارد شد؛ هیچ دستوری ارسال نشده است. فقط تکرار دستی همان UUID مجاز است.');
  }

  async function readTimeline(after:number) {
    if(!current || busy || !Number.isSafeInteger(after) || after<0)return;
    setBusy(true); setTimeline(null); setPast(null);
    try {
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef)+
        '/history?afterRevision='+after+'&limit=2',
        {credentials:'omit',cache:'no-store'});
      if(!response.ok)throw Error('HISTORY_UNAVAILABLE');
      const v:unknown=await response.json();
      if(!historyValid(v,current,after))throw Error('HISTORY_NOT_VERIFIED');
      setTimeline(v);
      setTimelineMessage('فقط رخدادهای آزمایشی Journal نمایش داده شدند.');
    }catch{
      setCurrent(null); setTimeline(null); setPast(null);
      setTimelineMessage('تاریخچه قابل تأیید نیست؛ پرونده فعلی نیز باید بازخوانی شود.');
    }finally{setBusy(false);}
  }
  async function readPast() {
    if(!current || busy || !/^(0|[1-9][0-9]*)$/.test(asOf) ||
      !Number.isSafeInteger(Number(asOf))){
      setTimelineMessage('شماره نسخه تاریخی نامعتبر است.');return;
    }
    const revision=Number(asOf);
    setBusy(true);setPast(null);
    try{
      const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef)+
        '/versions/'+revision,{credentials:'omit',cache:'no-store'});
      if(!response.ok)throw Error('HISTORY_UNAVAILABLE');
      const v:unknown=await response.json();
      if(!historicalValid(v,current,revision))throw Error('HISTORY_MISMATCH');
      setPast(v);
      setTimelineMessage('نسخه تاریخی آزمایشی بازسازی شد؛ این وضعیت فعلی نیست.');
    }catch{
      setCurrent(null);setTimeline(null);setPast(null);
      setTimelineMessage('نسخه تاریخی قابل تأیید نیست؛ پرونده را دوباره بخوانید.');
    }finally{setBusy(false);}
  }

  return <section className="panel" aria-labelledby="support-demo-title">
    <h2 id="support-demo-title">ثبت و پیگیری درخواست پشتیبانی آزمایشی عضو</h2>
    <p>این گردش مستقل از مدیریت مرکز است. تنها شناسه‌های ساختگی را ثبت می‌کند و هیچ شکایت واقعی، اقدام بیمه‌ای، تشخیص یا تسویه‌ای را انجام نمی‌دهد.</p>
    <fieldset className="fields" disabled={busy || pendingWrite!==null}>
      <label htmlFor="sup-case">پرونده آزمایشی
        <input id="sup-case" dir="ltr" value={caseRef}
          onChange={e => { setCaseRef(e.target.value); setCurrent(null); setTimeline(null); setPast(null); setConfirmedReturn(null); }} autoComplete="off" />
      </label>
      <label htmlFor="sup-member">شناسه عضو فرضی
        <input id="sup-member" dir="ltr" value={memberRef}
          onChange={e => {setMemberRef(e.target.value);setConfirmedReturn(null);}} autoComplete="off" />
      </label>
      <label htmlFor="sup-evidence">ارجاع مدرک تأییدنشده
        <input id="sup-evidence" dir="ltr" value={evidenceRef}
          onChange={e => setEvidenceRef(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-submitter">ثبت‌کننده ادعایی
        <input id="sup-submitter" dir="ltr" value={submitter}
          onChange={e => setSubmitter(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-topic">موضوع آزمایشی
        <select id="sup-topic" value={topic}
          onChange={e => setTopic(e.target.value as typeof topic)}>
          <option value="MEDICAL_SERVICE_COMPLAINT">نارضایتی از خدمت پزشکی (تأییدنشده)</option>
          <option value="INSURANCE_COVERAGE_DISPUTE">اختلاف پوشش بیمه (تأییدنشده)</option>
        </select>
      </label>
      <label htmlFor="sup-actor">ارجاع پیگیر ادعایی
        <input id="sup-actor" dir="ltr" value={actor}
          onChange={e => setActor(e.target.value)} autoComplete="off" />
      </label>
      <label htmlFor="sup-note">ارجاع یادداشت پیگیری تأییدنشده
        <input id="sup-note" dir="ltr" value={note}
          onChange={e => setNote(e.target.value)} autoComplete="off" />
      </label>
    </fieldset>
    {pendingWrite && <section aria-label="دستور آزمایشی نامطمئن" data-testid="pending-support-write">
      <p>وضعیت ثبت در PostgreSQL نامعلوم است؛ از ساخت دستور دوم جلوگیری شده است.</p>
      <p>شناسه همان درخواست آزمایشی: <code>{pendingWrite.command.requestId}</code></p>
      <p>نسخه مورد انتظار: {pendingWrite.command.expectedRevision}</p>
      <p>این رسید فقط به شناسه‌های ساختگی مربوط است، امضا یا اختیار قانونی ندارد و نباید حاوی داده واقعی یا سلامت باشد.</p>
      <p>برای بازیابی پس از Reload، متن زیر را خودتان کپی و در جایی امن برای آزمون نگه دارید؛ برنامه آن را ذخیره نمی‌کند.</p>
      <label htmlFor="pending-recovery-capsule">رسید دستی بازیابی آزمایشی</label>
      <textarea id="pending-recovery-capsule" dir="ltr" readOnly
        data-testid="pending-recovery-capsule" value={exportRecovery(pendingWrite)}
        rows={5} spellCheck={false} aria-label="رسید دستی بازیابی آزمایشی"/>
      <p>پس از Reload، فقط با واردکردن دستی این رسید می‌توان همان UUID را بازسازی کرد. هیچ تکراری خودکار نیست.</p>
      <button type="button" disabled={busy} onClick={()=>void sendExact(pendingWrite)}>
        تکرار امن همان دستور آزمایشی
      </button>
    </section>}
    <section aria-label="ورود دستی رسید بازیابی آزمایشی" data-testid="manual-recovery-import">
      <h3>بازیابی دستور نامطمئن پس از بازکردن دوباره صفحه</h3>
      <p>فقط متن رسید ساختگی TRN-TECH-092؛ این سند امضا نشده و مجوز عضویت یا شکایت نیست.</p>
      <label htmlFor="manual-recovery-import-text">متن رسید بازیابی آزمایشی</label>
      <textarea id="manual-recovery-import-text" dir="ltr" rows={4}
        value={manualRecoveryText} spellCheck={false} maxLength={4000}
        disabled={busy || pendingWrite!==null}
        onChange={e=>setManualRecoveryText(e.target.value)}/>
      <button type="button" disabled={busy || pendingWrite!==null}
        onClick={importManualRecovery}>بارگذاری رسید بازیابی بدون ارسال</button>
    </section>
    <section data-testid="member-return-followup" aria-label="بازخوانی رسید پیگیری از مرکز">
      <h3>پیگیری ادعایی ثبت‌شده در مرکز — بازخوانی توسط عضو آزمایشی</h3>
      <p>ابتدا شناسه پرونده و عضو فرضی را وارد کنید؛ سپس رسید
        آزمایشی را از مرکز کپی کنید. فقط رویداد PostgreSQL بررسی می‌شود،
        نه صلاحیت مرکز، پاسخ بیمه، تشخیص یا اقدام درمانی.</p>
      <textarea dir="ltr" rows={5} maxLength={1300}
        aria-label="ورود رسید پیگیری آزمایشی مرکز"
        value={returnedFollowUp} spellCheck={false}
        disabled={busy||pendingWrite!==null}
        onChange={e=>{setReturnedFollowUp(e.target.value);setConfirmedReturn(null);}}/>
      <button type="button" disabled={busy||pendingWrite!==null||
          returnedFollowUp.length===0||returnedFollowUp.length>1300}
        onClick={()=>void inspectReturnedFollowUp()}>
        بررسی مستقل پیگیری مرکز از PostgreSQL
      </button>
      <p role="status" data-testid="member-return-followup-status">{returnStatus}</p>
      {confirmedReturn&&<p data-testid="member-return-followup-confirmed">
        پرونده آزمایشی {confirmedReturn.caseRef} | نسخه {confirmedReturn.revision} |
        یادداشت تأییدنشده: <code>{confirmedReturn.noteRef}</code>
      </p>}
    </section>
    <div className="actions">
      <button disabled={busy || pendingWrite!==null} type="button" onClick={() => void read()}>بازخوانی پرونده پشتیبانی آزمایشی</button>
      <button disabled={busy || pendingWrite!==null || !current || current.revision !== 0} type="button"
        onClick={() => void write(false)}>ثبت درخواست اولیه آزمایشی</button>
      <button disabled={busy || pendingWrite!==null || !current || current.revision === 0} type="button"
        onClick={() => void write(true)}>ثبت ارجاع پیگیری آزمایشی</button>
    </div>
    <p data-testid="support-status" aria-live="polite" className="status">
      {busy ? 'در حال ثبت یا خواندن از پایگاه آزمایشی...' : message}
    </p>
    {current && current.revision>0 && !pendingWrite &&
      current.caseRef===caseRef && current.memberRef===memberRef &&
      <section aria-label="رسید دستی تحویل پرونده به محیط مرکز"
        data-testid="member-support-handoff">
       <h3>تحویل دستی مرجع پرونده آزمایشی به محیط مرکز</h3>
       <p>این فقط نشانی نسخه‌دار پرونده و ادعاهای ثبت‌شده است؛
         هیچ دسترسی به پرونده شخصی، مجوز حرفه‌ای، ارجاع درمانی،
         ثبت شکایت قانونی یا اختیار مرکز ایجاد نمی‌کند.
         در محیط آزمایشی مرکز باید همین پرونده مستقلاً از PostgreSQL خوانده شود.</p>
       <textarea aria-label="رسید خروجی تحویل آزمایشی پرونده"
        dir="ltr" readOnly rows={8} spellCheck={false}
        value={JSON.stringify({
         format:'TRN-TECH-180-TEST-SUPPORT-HANDOFF',version:1,
         caseRef:current.caseRef,memberRef:current.memberRef,
         topic:current.topic,evidenceRef:current.evidenceRef,
         originalRequestId:current.originalRequestId,revision:current.revision
        },null,2)}/>
      </section>}
    {current && <section aria-label="وضعیت پرونده پشتیبانی آزمایشی">
      <p><strong>نسخه:</strong> {current.revision} | <strong>پیگیری‌های تأییدنشده:</strong> {current.followUpCount}</p>
      <p>{current.topic === 'MedicalServiceComplaint' ? 'موضوع: نارضایتی از خدمت پزشکی' :
        current.topic === 'InsuranceCoverageDispute' ? 'موضوع: اختلاف پوشش بیمه' : 'درخواست اولیه‌ای وجود ندارد'}</p>
      <p>تأیید عضویت: خیر | شکایت رسمی: خیر | تماس با بیمه‌گر: خیر | رسیدگی بالینی: خیر</p>
      <ul>{current.followUps.map(x => <li key={x.revision}>
        نسخه {x.revision}: یادداشت <code>{x.noteRef}</code> — ثبت‌کننده ادعایی <code>{x.claimedActorRef}</code>
      </li>)}</ul>
    </section>}
    {!current && <p role="status" data-testid="support-history-status">{timelineMessage}</p>}
    {current && <section className="panel" aria-label="تاریخچه پرونده پشتیبانی آزمایشی">
      <h3>تاریخچه نسخه‌ها و وضعیت پیشین پرونده آزمایشی</h3>
      <p>فقط رویدادهای Journal، بدون احراز هویت یا پاسخ رسمی بیمه‌گر.</p>
      <button disabled={busy} type="button" onClick={() => void readTimeline(0)}>
        بارگذاری تاریخچه از ابتدا
      </button>
      {timeline && <>
        <p>از نسخه {timeline.afterRevision} | نسخه جاری {timeline.currentRevision}</p>
        <ol data-testid="support-timeline">{timeline.events.map(e => <li key={e.revision}>
          نسخه {e.revision}: {e.action==='SUBMIT_UNVERIFIED'
            ? 'درخواست اولیه تأییدنشده' : 'پیگیری تأییدنشده'} {e.noteRef||''}
        </li>)}</ol>
        {timeline.hasMore && timeline.nextAfterRevision!==null &&
          <button disabled={busy} type="button" onClick={() => void readTimeline(timeline.nextAfterRevision!)}>
            صفحه بعد تاریخچه
          </button>}
      </>}
      <label htmlFor="support-asof">نسخه گذشته برای بررسی
        <input id="support-asof" dir="ltr" value={asOf} maxLength={19} autoComplete="off"
          onChange={e => { setAsOf(e.target.value); setPast(null); }}/>
      </label>
      <button type="button" disabled={busy} onClick={() => void readPast()}>
        نمایش نسخه تاریخی
      </button>
      <p data-testid="support-history-status" role="status">{timelineMessage}</p>
      {past && <section aria-label="بازسازی تاریخی پرونده پشتیبانی آزمایشی">
        <p>نسخه تاریخی: {past.asOfRevision} | نسخه جاری: {past.currentRevision}</p>
        <p>پیگیری‌های تأییدنشده در نسخه تاریخی: {past.snapshot.followUpCount}</p>
        <p>شکایت قانونی: خیر | تماس با بیمه: خیر | احراز هویت عضو: خیر</p>
      </section>}
    </section>}
  </section>;
}
