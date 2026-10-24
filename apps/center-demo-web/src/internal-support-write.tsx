import {useRef,useState} from 'react';
import {exactSupportSource} from './support-exact-source';
import {makeSupportReceipt,parseSupportCommand,parseSupportReceipt} from './support-command-receipt';
import type {PendingSupportWrite,SupportWriteBody} from './support-command-receipt';

type Existing={revision:number;memberRef:string|null;topic:string|null;
 evidenceRef:string|null;claimedSubmitterRef:string|null;originalRequestId:string|null};
const refOk=(value:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(value)&&value.length<=128;
const topics=['MedicalServiceComplaint','InsuranceCoverageDispute'] as const;
type Topic=typeof topics[number];
const wireTopic:Record<Topic,SupportWriteBody['topic']>={
 MedicalServiceComplaint:'MEDICAL_SERVICE_COMPLAINT',
 InsuranceCoverageDispute:'INSURANCE_COVERAGE_DISPUTE'
};
function sameOrigin(input:PendingSupportWrite,existing:Existing){
 const command=input.command;
 if(existing.revision<command.expectedRevision)return false;
 if(command.action==='SUBMIT_UNVERIFIED'){
  return existing.revision===0||
   (existing.originalRequestId===command.requestId&&
    existing.memberRef===command.memberRef&&
    wireTopic[existing.topic as Topic]===command.topic&&
    existing.evidenceRef===command.unverifiedEvidenceRef&&
    existing.claimedSubmitterRef===command.claimedSubmitterRef);
 }
 return existing.revision>0&&existing.originalRequestId===command.originalSubmissionRequestId&&
  existing.memberRef===command.memberRef&&
  wireTopic[existing.topic as Topic]===command.topic&&
  existing.evidenceRef===command.unverifiedEvidenceRef&&
  existing.claimedSubmitterRef===command.claimedSubmitterRef;
}
export function InternalSupportWrite({caseRef,existing,onChanged,onPendingChange}:{
 caseRef:string;existing:Existing;onChanged:()=>void;onPendingChange:(locked:boolean)=>void}){
 const [member,setMember]=useState('test:member-local');
 const [evidence,setEvidence]=useState('test:unverified-document');
 const [submitter,setSubmitter]=useState('test:claimed-submitter');
 const [actor,setActor]=useState('test:claimed-followup-actor');
 const [note,setNote]=useState('test:unverified-followup-note');
 const [topic,setTopic]=useState<Topic>('MedicalServiceComplaint');
 const [phase,setPhase]=useState<'IDLE'|'SUBMITTING'|'CONFIRMED'|'UNCERTAIN'>('IDLE');
 const [pending,setPending]=useState<PendingSupportWrite|null>(null);
 const [importText,setImportText]=useState('');
 const [importError,setImportError]=useState(false);
 const processing=useRef(false);
 const followUp=existing.revision>0;
 const allowed=refOk(caseRef)&&Number.isSafeInteger(existing.revision)&&existing.revision>=0&&
 (followUp?
  !!existing.originalRequestId&&!!existing.memberRef&&
  Object.hasOwn(wireTopic,existing.topic??'')&&
  !!existing.evidenceRef&&!!existing.claimedSubmitterRef&&refOk(actor)&&refOk(note):
  refOk(member)&&refOk(evidence)&&refOk(submitter));
 const canRetry=!!pending&&sameOrigin(pending,existing);
 function build():PendingSupportWrite|null{
  if(!allowed)return null;
  const command:SupportWriteBody={
   requestId:crypto.randomUUID(),expectedRevision:existing.revision,
   action:followUp?'FOLLOW_UP_UNVERIFIED':'SUBMIT_UNVERIFIED',
   memberRef:followUp?existing.memberRef!:member,
   topic:followUp?wireTopic[existing.topic as Topic]:wireTopic[topic],
   unverifiedEvidenceRef:followUp?existing.evidenceRef!:evidence,
   claimedSubmitterRef:followUp?existing.claimedSubmitterRef!:submitter,
   originalSubmissionRequestId:followUp?existing.originalRequestId!:
    '00000000-0000-0000-0000-000000000000',
   claimedFollowUpActorRef:followUp?actor:null,
   unverifiedFollowUpNoteRef:followUp?note:null
  };
  return parseSupportCommand(caseRef,JSON.stringify(command));
 }
 async function submit(){
  if(processing.current)return;
  const command=pending??build();
  if(!command||!sameOrigin(command,existing))return;
  processing.current=true;setPhase('SUBMITTING');setPending(command);onPendingChange(true);
  try{
   const res=await fetch('/internal/demo/v1/support/'+encodeURIComponent(command.caseRef)+'/commands',
    {method:'POST',credentials:'omit',cache:'no-store',
     headers:{'Content-Type':'application/json'},body:command.body});
   if(!res.ok)throw Error('REJECTED_OR_UNCERTAIN');
   const raw:unknown=await res.json();
   if(!exactSupportSource(raw,command.caseRef,command.command,'ACK'))throw Error('UNVERIFIABLE_REPLY');
   const check=await fetch('/internal/demo/v1/support/'+encodeURIComponent(command.caseRef),{credentials:'omit',cache:'no-store'});
   if(!check.ok||!exactSupportSource(await check.json(),command.caseRef,command.command,'READ'))
    throw Error('ORIGINAL_UUID_NOT_COMMITTED');
   setPhase('CONFIRMED');setPending(null);onPendingChange(false);
   onChanged();
  }catch{
   // No new UUID, payload, expectedRevision or fields may be generated here.
   setPhase('UNCERTAIN');
  }finally{processing.current=false;}
 }
 async function inspectPending(){
  if(processing.current||!pending)return;
  processing.current=true;setPhase('SUBMITTING');
  try{
   const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(pending.caseRef),{credentials:'omit',cache:'no-store'});
   if(!response.ok||!exactSupportSource(await response.json(),pending.caseRef,pending.command,'READ'))
    throw Error('ORIGINAL_UUID_NOT_COMMITTED');
   setPending(null);setPhase('CONFIRMED');onPendingChange(false);onChanged();
  }catch{setPhase('UNCERTAIN');}
  finally{processing.current=false;}
 }
 function importReceipt(){
  if(pending||processing.current)return;
  const original=parseSupportReceipt(importText,caseRef);
  if(!original||!sameOrigin(original,existing)){
   setImportError(true);return;
  }
  setPending(original);onPendingChange(true);setPhase('UNCERTAIN');setImportError(false);setImportText('');
 }
 return <section className="panel" data-testid="internal-support-write">
  <h3>{followUp?'ثبت یادداشت پیگیری آزمایشی':'ثبت اولیه پرونده آزمایشی'}</h3>
  <p>صرفاً داده و ادعاهای test:؛ این اقدام شکایت رسمی یا احراز هویت نیست.</p>
  {!followUp?<div>
   <label>عضو آزمایشی<input aria-label="عضو آزمایشی" disabled={!!pending}
    value={member} onChange={e=>setMember(e.target.value)}/></label>
   <label>موضوع آزمایشی<select aria-label="موضوع آزمایشی" disabled={!!pending}
    value={topic} onChange={e=>setTopic(e.target.value as Topic)}>
    {topics.map(t=><option value={t} key={t}>{t}</option>)}</select></label>
   <label>ارجاع شاهد تأییدنشده<input aria-label="ارجاع شاهد تأییدنشده"
    disabled={!!pending} value={evidence} onChange={e=>setEvidence(e.target.value)}/></label>
   <label>ثبت‌کننده ادعایی<input aria-label="ثبت‌کننده ادعایی"
    disabled={!!pending} value={submitter} onChange={e=>setSubmitter(e.target.value)}/></label>
  </div>:<div>
   <label>پیگیر ادعایی<input aria-label="پیگیر ادعایی" disabled={!!pending}
    value={actor} onChange={e=>setActor(e.target.value)}/></label>
   <label>ارجاع یادداشت آزمایشی<input aria-label="ارجاع یادداشت آزمایشی"
    disabled={!!pending} value={note} onChange={e=>setNote(e.target.value)}/></label>
  </div>}
  <button type="button" disabled={!allowed||!!pending||phase==='SUBMITTING'}
   onClick={()=>void submit()}>
   {followUp?'ثبت پیگیری تأییدنشده':'ثبت اولیه تأییدنشده'}</button>
  {pending&&<div data-testid="support-recovery">
   <p>نتیجه درخواست قطعی نیست. متن دقیق و شناسه درخواست حفظ شده است؛
    از ایجاد درخواست تازه یا تغییر محتوا خودداری کنید.</p>
   <label>رسید قابل انتقال درخواست (بدون اعتبار هویتی)
    <textarea aria-label="رسید قابل انتقال درخواست" readOnly
     value={makeSupportReceipt(pending)} rows={5}/></label>
   <button type="button" disabled={phase==='SUBMITTING'} onClick={()=>void inspectPending()}>
    بررسی فقط‌خواندنی همان UUID در PostgreSQL</button>
   <button type="button" disabled={phase==='SUBMITTING'||!canRetry}
    onClick={()=>void submit()}>تکرار ایمن همان درخواست</button>
   {!canRetry&&<p role="alert">نسخه یا مالک پرونده ناسازگار است؛ بازپخش مسدود شد.</p>}
  </div>}
  {!pending&&<div>
   <label>واردکردن رسید دقیق درخواست پس از بازخوانی پرونده
    <textarea aria-label="ورود رسید بازیابی" value={importText} maxLength={2400}
     onChange={e=>{setImportText(e.target.value);setImportError(false);}} rows={3}/></label>
   <button type="button" disabled={!importText}
    onClick={importReceipt}>بررسی و بارگذاری رسید</button>
   {importError&&<p role="alert">رسید نامعتبر یا متعلق به پرونده دیگری است؛
    هیچ درخواستی ارسال نشد.</p>}
  </div>}
  <p role="status">{phase==='IDLE'?'هیچ دستور نوشتنی ارسال نشده است.':
   phase==='SUBMITTING'?'در حال ثبت در PostgreSQL':
   phase==='CONFIRMED'?'همان UUID از PostgreSQL مستقل تأیید شد؛ بازخوانی در جریان است.':
   'نتیجه نامشخص است؛ فقط تکرار دقیق همان درخواست مجاز است.'}</p>
 </section>;
}
