import {useRef,useState} from 'react';

type Existing={revision:number;memberRef:string|null;topic:string|null;
 evidenceRef:string|null;claimedSubmitterRef:string|null;originalRequestId:string|null};
const refOk=(value:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(value)&&value.length<=128;
const topics=['MedicalServiceComplaint','InsuranceCoverageDispute'] as const;
type Topic=typeof topics[number];
const wireTopic:Record<Topic,string>={
 MedicalServiceComplaint:'MEDICAL_SERVICE_COMPLAINT',
 InsuranceCoverageDispute:'INSURANCE_COVERAGE_DISPUTE'
};
export function InternalSupportWrite({caseRef,existing,onChanged}:{
 caseRef:string;existing:Existing;onChanged:()=>void}){
 const [member,setMember]=useState('test:member-local');
 const [evidence,setEvidence]=useState('test:unverified-document');
 const [submitter,setSubmitter]=useState('test:claimed-submitter');
 const [actor,setActor]=useState('test:claimed-followup-actor');
 const [note,setNote]=useState('test:unverified-followup-note');
 const [topic,setTopic]=useState<Topic>('MedicalServiceComplaint');
 const [phase,setPhase]=useState<'IDLE'|'SUBMITTING'|'SAVED'|'UNAVAILABLE'>('IDLE');
 const [requestId,setRequestId]=useState<string|null>(null);
 const processing=useRef(false);
 const followUp=existing.revision>0;
 const allowed=refOk(caseRef)&&existing.revision>=0&&
 (followUp?
  !!existing.originalRequestId&&!!existing.memberRef&&!!existing.topic&&
  !!existing.evidenceRef&&!!existing.claimedSubmitterRef&&refOk(actor)&&refOk(note):
  refOk(member)&&refOk(evidence)&&refOk(submitter));
 async function submit(){
  if(processing.current||!allowed)return;
  processing.current=true;setPhase('SUBMITTING');
  const id=requestId??crypto.randomUUID();
  setRequestId(id);
  const payload={
   requestId:id,expectedRevision:existing.revision,
   action:followUp?'FOLLOW_UP_UNVERIFIED':'SUBMIT_UNVERIFIED',
   memberRef:followUp?existing.memberRef:member,
   topic:followUp?wireTopic[existing.topic as Topic]:wireTopic[topic],
   unverifiedEvidenceRef:followUp?existing.evidenceRef:evidence,
   claimedSubmitterRef:followUp?existing.claimedSubmitterRef:submitter,
   originalSubmissionRequestId:followUp?existing.originalRequestId:'00000000-0000-0000-0000-000000000000',
   claimedFollowUpActorRef:followUp?actor:null,
   unverifiedFollowUpNoteRef:followUp?note:null
  };
  try{
   const res=await fetch('/internal/demo/v1/support/'+encodeURIComponent(caseRef)+'/commands',
    {method:'POST',credentials:'omit',cache:'no-store',
     headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
   if(!res.ok)throw Error('REJECTED');
   const raw:unknown=await res.json();
   const data=raw as Record<string,unknown>;
   if(!data||data.contract!=='TRN-TECH-075'||data.syntheticOnly!==true||
    data.memberAuthenticated!==false||data.complaintLegallySubmitted!==false||
    data.insurerContacted!==false||data.clinicalReviewCompleted!==false||
    data.nationalReleaseAllowed!==false||data.caseRef!==caseRef||
    (data.outcome!=='RecordedUnverified'&&data.outcome!=='Replayed')||
    !Number.isSafeInteger(data.revision)||data.revision!==existing.revision+1)
    throw Error('UNVERIFIED_WRITE');
   setPhase('SAVED');setRequestId(null);onChanged();
  }catch{setPhase('UNAVAILABLE');}
  finally{processing.current=false;}
 }
 return <section className="panel" data-testid="internal-support-write">
  <h3>{followUp?'ثبت یادداشت پیگیری آزمایشی':'ثبت اولیه پرونده آزمایشی'}</h3>
  <p>صرفاً داده و ادعاهای test:؛ این اقدام شکایت رسمی یا احراز هویت نیست.</p>
  {!followUp?<div>
   <label>عضو آزمایشی<input aria-label="عضو آزمایشی" value={member} onChange={e=>{setMember(e.target.value);setRequestId(null);}}/></label>
   <label>موضوع آزمایشی<select aria-label="موضوع آزمایشی" value={topic} onChange={e=>{setTopic(e.target.value as Topic);setRequestId(null);}}>
    {topics.map(t=><option value={t} key={t}>{t}</option>)}</select></label>
   <label>ارجاع شاهد تأییدنشده<input aria-label="ارجاع شاهد تأییدنشده" value={evidence} onChange={e=>{setEvidence(e.target.value);setRequestId(null);}}/></label>
   <label>ثبت‌کننده ادعایی<input aria-label="ثبت‌کننده ادعایی" value={submitter} onChange={e=>{setSubmitter(e.target.value);setRequestId(null);}}/></label>
  </div>:<div>
   <label>پیگیر ادعایی<input aria-label="پیگیر ادعایی" value={actor} onChange={e=>{setActor(e.target.value);setRequestId(null);}}/></label>
   <label>ارجاع یادداشت آزمایشی<input aria-label="ارجاع یادداشت آزمایشی" value={note} onChange={e=>{setNote(e.target.value);setRequestId(null);}}/></label>
  </div>}
  <button type="button" disabled={!allowed||phase==='SUBMITTING'} onClick={()=>void submit()}>
   {followUp?'ثبت پیگیری تأییدنشده':'ثبت اولیه تأییدنشده'}</button>
  <p role="status">{phase==='IDLE'?'هیچ دستور نوشتنی ارسال نشده است.':
   phase==='SUBMITTING'?'در حال ثبت در PostgreSQL':
   phase==='SAVED'?'ثبت آزمایشی انجام شد؛ وضعیت رسمی تغییری نکرد.':
   'نتیجه دستور نامعلوم یا رد شد؛ از ثبت دوباره با شناسه تازه خودداری کنید.'}</p>
 </section>;
}
